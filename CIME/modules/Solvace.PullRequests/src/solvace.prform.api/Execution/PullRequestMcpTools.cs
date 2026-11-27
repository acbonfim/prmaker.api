using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.github.application.Contract;
using solvace.github.domain.Responses;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// r5 (1.6): abrir PR pelo PRMake via MCP — o mesmo caminho do <c>POST PullRequest/{card}/github</c> (que o
/// <c>prmake-plan.sh open-pr</c> usa): abre no GitHub com a integração de quem chama (no especialista, de quem pediu na
/// conversa), registra no card e na Timeline. A branch já deve existir; o nome é conferido pelas regras do card. Nunca mescla.
/// </summary>
[McpServerToolType]
public class PullRequestMcpTools(
    IServiceProvider services,
    SkillsRulesService rules,
    IExecutionTimelineWriter timeline,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users,
    SpecialistActions specialist)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "prmake_pr_open", Destructive = true)]
    [Description("Abre um PR pelo PRMake (GitHub) de uma branch QUE JA EXISTE para o destino, registra no card e na Timeline — o mesmo " +
                 "que prmake-plan.sh open-pr. Use os nomes das regras do card (prmake_rules: branch por destino, ex.: hotfix/<card>-rv -> " +
                 "release-version); branch de outro card ou sem prefixo e recusada. title padrao = PrTitlePattern. PR ja aberto para " +
                 "head->base volta o existente. Nunca mescla. Especialista do caso: so com pedido explicito da pessoa (usa a integracao " +
                 "do GitHub de quem pediu).")]
    public Task<string> Open(string card,
        [Description("Repositorio (ex.: edv-solvace)")] string repository,
        [Description("Branch de origem completa, ja existente (ex.: hotfix/77434-rv)")] string source,
        [Description("Branch de destino (ex.: release-version, development)")] string target,
        [Description("Titulo (padrao: PrTitlePattern do card)")] string? title = null,
        [Description("Abrir como rascunho")] bool draft = false,
        [Description("Descricao do PR (markdown; padrao: AB#<card>)")] string? description = null,
        [Description("0091: sub-tarefa (#n) que abriu o PR — vira entrega dela (padrao: a sub-tarefa de PR em andamento no card)")] long? taskNumber = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = SkillsRulesService.NormalizeCard(card);
        if (c.Length == 0) throw new McpException("Informe o card.");
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(target))
            throw new McpException("Informe repository, source e target.");

        var r = await rules.GetAsync(c, ct);
        var branch = (source ?? string.Empty).Trim();
        var to = target.Trim();
        var (error, warning) = BranchRules.CheckPrSource(branch, c, r.MainBranch, to, r.Suffixes);
        if (error is not null) throw new McpException(error);

        var prTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle(r.PrTitlePattern, c, to) : title.Trim();
        var body = specialist.Mark(string.IsNullOrWhiteSpace(description) ? $"AB#{c}" : description.Trim());
        var slash = branch.IndexOf('/');
        var request = new OpenPullRequestGithubRequest
        {
            RepositoryId = repository.Trim(),
            BranchPrefix = branch[..(slash + 1)],
            BranchName = branch[(slash + 1)..],
            TargetBranch = to,
            Title = prTitle,
            Description = body,
            Draft = draft,
            UserId = UserId() ?? Guid.Empty
        };

        return await specialist.RunAsync(c, $"Abrir PR {repository.Trim()}: {branch} → {to}", prTitle, async () =>
        {
            // Resolvido aqui: o GitHubService exige o plugin do GitHub no construtor (sem integração → erro claro).
            var github = services.GetRequiredService<IPullRequestGithubApplication>();
            var pr = await github.Open(c, request, ct);
            var text = pr.AlreadyExisted
                ? $"PR #{pr.Number} já existente registrado pelo CIME: {pr.RepositoryId} ({pr.BranchPrefix}{pr.BranchName} → {pr.TargetBranch}) — {pr.Url}"
                : $"PR #{pr.Number} aberto pelo CIME: {pr.RepositoryId} ({pr.BranchPrefix}{pr.BranchName} → {pr.TargetBranch}) — {pr.Url}";
            var who = await ActorNameAsync(ct);
            await timeline.WriteAsync(c, specialist.Via is { } via ? $"{text} — {via}." : text, UserId(), who, ct);
            if (services.GetService<WorkDeliverableHooks>() is { } hooks)
                await hooks.PullRequestAsync(c, pr, taskNumber, specialist.Via is null, new ExecutionActor(UserId(), who, true), ct);
            var response = JsonSerializer.Serialize(new
            {
                pr.Number, pr.Url, repository = pr.RepositoryId, branch, target = pr.TargetBranch, pr.AlreadyExisted, warning,
                output = new { pr = pr.Url, number = pr.Number.ToString(), repository = pr.RepositoryId, branch, target = pr.TargetBranch, alreadyExisted = pr.AlreadyExisted },
                undo = pr.AlreadyExisted
                    ? new { action = "none", @params = new Dictionary<string, string> { ["reason"] = "o PR ja existia antes" } }
                    : new { action = "close-pr", @params = new Dictionary<string, string> { ["repo"] = pr.RepositoryId, ["number"] = pr.Number.ToString() } },
                reminder = "Nunca mescle: o merge e feito por outra pessoa."
            }, Json);
            return (response, $"PR #{pr.Number}{(pr.AlreadyExisted ? " (já existia)" : string.Empty)}: {pr.Url}" + (warning is null ? string.Empty : $" — atenção: {warning}"));
        }, ct);
    });

    [McpServerTool(Name = "prmake_pr_reviews", ReadOnly = true)]
    [Description("0106: le o REVIEW de um PR do card no GitHub (os repositorios dos cards ficam no GitHub): quem pediu mudancas, as revisoes com o " +
                 "texto, os comentarios nas linhas agrupados por conversa (com id, arquivo:linha, se a linha e CODIGO NOVO do PR, se ficou " +
                 "desatualizado e o trecho) e os comentarios da conversa. Use para avaliar um pedido de mudancas (request changes): avalie so os " +
                 "pontos em codigo novo do PR; o resto e seguimento. Responda depois com prmake_pr_review_reply.")]
    public Task<string> Reviews(string card,
        [Description("Numero do PR")] int number,
        [Description("Repositorio (ex.: edv-solvace); padrao: o do card")] string? repository = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var (c, repo) = await TargetAsync(card, repository, ct);
        var github = services.GetRequiredService<IGitHubService>();
        var r = await github.GetPullRequestReviewsAsync(repo, number, ct);
        if (r.Error is { } error) throw new McpException(error);
        CheckCard(c, r.Head, number);
        return ReviewsMarkdown(r);
    });

    [McpServerTool(Name = "prmake_pr_review_reply", Destructive = true)]
    [Description("0106: responde no PR do card (GitHub) — commentId = o comentario de linha que voce responde (a resposta entra na mesma conversa); " +
                 "sem commentId = comentario na conversa do PR. Use ao terminar os ajustes de um pedido de mudancas: um por ponto, dizendo o que " +
                 "mudou e o commit, ou por que nao mudou. Nunca aprova nem mescla. Especialista do caso: so a pedido da pessoa.")]
    public Task<string> ReviewReply(string card,
        [Description("Numero do PR")] int number,
        [Description("Texto (markdown)")] string body,
        [Description("Id do comentario de linha respondido (de prmake_pr_reviews)")] long? commentId = null,
        [Description("Repositorio; padrao: o do card")] string? repository = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(body)) throw new McpException("Informe o texto da resposta.");
        var (c, repo) = await TargetAsync(card, repository, ct);
        var github = services.GetRequiredService<IGitHubService>();
        var pr = await github.GetPullRequestReviewsAsync(repo, number, ct);
        if (pr.Error is { } error) throw new McpException(error);
        CheckCard(c, pr.Head, number);
        if (commentId is { } id && pr.Comments.All(x => x.Id != id))
            throw new McpException($"O comentario {id} nao e deste PR — use os ids de prmake_pr_reviews.");
        var text = specialist.Mark(body.Trim());
        return await specialist.RunAsync(c, commentId is null ? $"Comentar no PR #{number}" : $"Responder o comentario {commentId} do PR #{number}", null, async () =>
        {
            var url = commentId is { } cid
                ? await github.ReplyToReviewCommentAsync(repo, number, cid, text, ct)
                : await github.CommentOnPullRequestAsync(repo, number, text, ct);
            return (JsonSerializer.Serialize(new { pr = number, repository = repo, commentId, url }, Json), $"resposta no PR #{number}: {url}");
        }, ct);
    });

    [McpServerTool(Name = "prmake_pr_request_review", Destructive = true)]
    [Description("0106: pede nova revisao no PR do card (GitHub) depois de atender o pedido de mudancas. reviewers vazio = quem pediu mudancas (de " +
                 "prmake_pr_reviews); time = '@slug'. Especialista do caso: so a pedido da pessoa.")]
    public Task<string> RequestReview(string card,
        [Description("Numero do PR")] int number,
        [Description("Logins (ou @time); vazio = quem pediu mudancas")] string[]? reviewers = null,
        [Description("Repositorio; padrao: o do card")] string? repository = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var (c, repo) = await TargetAsync(card, repository, ct);
        var github = services.GetRequiredService<IGitHubService>();
        var pr = await github.GetPullRequestReviewsAsync(repo, number, ct);
        if (pr.Error is { } error) throw new McpException(error);
        CheckCard(c, pr.Head, number);
        var who = (reviewers is { Length: > 0 } ? reviewers.ToList() : pr.ChangesRequestedBy)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (who.Count == 0) throw new McpException("Ninguem pediu mudancas neste PR — informe reviewers.");
        return await specialist.RunAsync(c, $"Pedir nova revisao no PR #{number}", string.Join(", ", who), async () =>
        {
            await github.RequestReviewAsync(repo, number, who, ct);
            return (JsonSerializer.Serialize(new { pr = number, repository = repo, reviewers = who }, Json),
                $"nova revisao pedida no PR #{number} a {string.Join(", ", who)}");
        }, ct);
    });

    /// <summary>Markdown curto do review: quem pediu mudanças, revisões, conversas nas linhas (raiz + respostas) e a conversa do PR.</summary>
    public static string ReviewsMarkdown(PullRequestReviewsResponse r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# PR #{r.Number} {r.Repository} ({r.Head} -> {r.Base}) — {r.State}");
        if (r.Title is { Length: > 0 }) sb.AppendLine(r.Title);
        if (r.Url is { Length: > 0 }) sb.AppendLine(r.Url);
        sb.AppendLine(r.ChangesRequestedBy.Count > 0
            ? $"**Mudancas pedidas por:** {string.Join(", ", r.ChangesRequestedBy)}"
            : "Nenhum pedido de mudancas valendo.");
        var reviews = r.Reviews.Where(x => x.State != "commented" || !string.IsNullOrWhiteSpace(x.Body)).ToList();
        if (reviews.Count > 0)
        {
            sb.AppendLine().AppendLine("## Revisoes");
            foreach (var x in reviews)
                sb.AppendLine($"- {x.User} · {x.State} · {x.SubmittedAt:dd/MM HH:mm}{(string.IsNullOrWhiteSpace(x.Body) ? string.Empty : $": {OneLine(x.Body!)}")}");
        }
        var roots = r.Comments.Where(x => x.InReplyTo is null).ToList();
        if (roots.Count > 0)
        {
            sb.AppendLine().AppendLine("## Comentarios nas linhas (id · arquivo:linha · codigo)");
            foreach (var root in roots)
            {
                var where = root.OnNewCode ? "codigo NOVO do PR" : "codigo que o PR nao mudou (contexto)";
                if (root.Outdated) where += ", desatualizado (o codigo mudou depois)";
                sb.AppendLine($"- **{root.Id}** · `{root.Path}:{root.Line}` · {where} · {root.User}: {OneLine(root.Body)}");
                if (root.Snippet is { Length: > 0 } snippet)
                    sb.AppendLine("  ```diff").AppendLine("  " + snippet.Replace("\n", "\n  ")).AppendLine("  ```");
                foreach (var reply in r.Comments.Where(x => x.InReplyTo == root.Id))
                    sb.AppendLine($"  - resposta {reply.Id} · {reply.User}: {OneLine(reply.Body)}");
            }
        }
        if (r.Conversation.Count > 0)
        {
            sb.AppendLine().AppendLine("## Conversa do PR");
            foreach (var x in r.Conversation.TakeLast(15))
                sb.AppendLine($"- {x.Id} · {x.User} · {x.CreatedAt:dd/MM HH:mm}: {OneLine(x.Body)}");
        }
        return sb.ToString();
    }

    private static string OneLine(string text)
    {
        var t = text.Replace("\r", string.Empty).Replace('\n', ' ').Trim();
        return t.Length <= 600 ? t : t[..600] + "…";
    }

    /// <summary>Card e repositório: o informado ou o do card (PRs/plano; senão o padrão das regras).</summary>
    private async Task<(string Card, string Repository)> TargetAsync(string card, string? repository, CancellationToken ct)
    {
        var c = SkillsRulesService.NormalizeCard(card);
        if (c.Length == 0) throw new McpException("Informe o card.");
        if (!string.IsNullOrWhiteSpace(repository)) return (c, repository.Trim());
        var repos = services.GetService<WorkItemToolsService>() is { } tools ? await tools.CardRepositoriesAsync(c, ct) : [];
        var repo = repos.FirstOrDefault() ?? (await rules.GetAsync(c, ct)).DefaultRepository;
        if (string.IsNullOrWhiteSpace(repo)) throw new McpException("Informe o repositorio (repository).");
        return (c, repo);
    }

    /// <summary>Só PR do card: a branch de origem leva o número do card (hotfix/79047-qa, feature/77988-rv).</summary>
    private static void CheckCard(string card, string? head, int number)
    {
        if (string.IsNullOrWhiteSpace(head) || !System.Text.RegularExpressions.Regex.IsMatch(head, $@"(^|[/_-]){card}($|[/_-])"))
            throw new McpException($"O PR #{number} ({head ?? "sem branch"}) nao e do card {card} — so PRs das branches do card.");
    }

    /// <summary><c>PrTitlePattern</c> com <c>{card}</c>, <c>{TARGET}</c> (maiúsculas) e <c>{target}</c> — o mesmo do <c>prmake-plan.sh</c>.</summary>
    public static string DefaultTitle(string? pattern, string card, string target)
    {
        var p = string.IsNullOrWhiteSpace(pattern) ? "AB#{card} {TARGET}" : pattern.Trim();
        var text = p.Replace("{card}", card, StringComparison.OrdinalIgnoreCase)
            .Replace("{TARGET}", target.ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{target}", target, StringComparison.Ordinal)
            .Trim();
        return text.Length > 0 ? text : $"{card} {target.ToUpperInvariant()}";
    }

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    private Guid? UserId() => Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) && id != Guid.Empty ? id : null;

    private async Task<string> ActorNameAsync(CancellationToken ct)
    {
        string? name = UserId() is { } id ? await users.GetFullNameAsync(id, ct) : null;
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim();
    }

    private static async Task<string> Safe(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or solvace.executionplans.domain.Entities.DomainException
                                       or PersonalIntegrationRequiredException or InvalidOperationException or HttpRequestException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
