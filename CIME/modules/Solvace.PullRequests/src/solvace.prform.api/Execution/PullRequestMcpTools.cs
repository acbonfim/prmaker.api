using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.github.application.Contract;
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
        specialist.EnsureCard(c);

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
