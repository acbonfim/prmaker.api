using System.Text.Json;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.github.application.Contract;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0106: um PR do card recebeu "changes requested" (visto na sincronização do plano). O fluxo simples:
/// <list type="number">
/// <item>cria o item <c>atender-review-pr</c> pendente (ler os pontos só no código novo do PR → ajustar na branch do card →
/// validar → levar aos PRs do card → responder no PR e pedir nova revisão) — um por rodada: com item aberto para o mesmo PR,
/// não cria outro;</item>
/// <item>avisa na conversa do caso (mensagem do PRMake) com o número do item;</item>
/// <item>registra o sinal de erro (o próximo agente avalia se foi erro dele e grava a lição).</item>
/// </list>
/// Nada executa sozinho: código muda só quando alguém clica em Executar (ou pede ao especialista). Dependências resolvidas
/// na hora (o plano depende deste handler; o item e a conversa dependem do plano).
/// </summary>
public class PullRequestReviewHandler(IServiceProvider services, ILogger<PullRequestReviewHandler> logger) : IPullRequestReviewHandler
{
    public const string TemplateKey = SeedReviewFlow0106.ReviewTemplateKey;

    private static readonly string[] OpenStatuses =
        [WorkItemStatus.Pending, WorkItemStatus.Queued, WorkItemStatus.Running, WorkItemStatus.Waiting, WorkItemStatus.Failed];

    public async Task ChangesRequestedAsync(PullRequestChangesRequested pr, CancellationToken ct)
    {
        if (pr.Number <= 0 || string.IsNullOrWhiteSpace(pr.Repository)) return;
        var (who, points) = await ReviewersAsync(pr, ct);
        var whoText = who.Count > 0 ? string.Join(", ", who) : "um revisor";
        var pointsText = points > 0 ? $" ({points} comentário(s) nas linhas)" : string.Empty;

        if (services.GetService<ILessonSignalSink>() is { } signals)
            await signals.SignalAsync(pr.Card, LessonSignals.PrChangesRequested, $"{pr.Repository}#{pr.Number}:{pr.HeadSha ?? "?"}",
                $"O PR #{pr.Number} ({pr.Repository} → {pr.Target}) recebeu pedido de mudanças de {whoText}{pointsText}. Veja os pontos " +
                "(prmake_pr_reviews): o que o revisor apontou que o agente deveria ter feito antes de abrir o PR?", $"PR #{pr.Number}", ct);

        var items = services.GetRequiredService<IWorkItemApplication>();
        var existing = await items.ListAsync(pr.Card, ct);
        var same = existing.Where(i => i.TemplateKey == TemplateKey && SamePullRequest(i, pr.Number)).ToList();
        if (same.FirstOrDefault(i => OpenStatuses.Contains(i.Status)) is { } open)
        {
            await PostAsync(pr, $"O PR [#{pr.Number}]({pr.Url}) recebeu de novo **pedido de mudanças** de {whoText}{pointsText}. O item #{open.Number} " +
                                "já está aberto para atender — ele lê os pontos na hora de executar.", ct);
            return;
        }

        WorkItemResponse created;
        try
        {
            var tools = services.GetRequiredService<WorkItemToolsService>();
            var request = await tools.BuildCreateRequestAsync(pr.Card, new CreateFromTemplateRequest
            {
                TemplateKey = TemplateKey,
                Answers = new Dictionary<string, JsonElement>
                {
                    ["repository"] = JsonSerializer.SerializeToElement(pr.Repository),
                    ["pullRequest"] = JsonSerializer.SerializeToElement(pr.Number.ToString()),
                    ["reviewers"] = JsonSerializer.SerializeToElement(string.Join(", ", who)),
                },
                AllowDuplicate = same.Count > 0,
                DuplicateReason = same.Count > 0 ? $"nova rodada do review do PR #{pr.Number}" : null
            }, ct);
            created = await items.CreateAsync(pr.Card, request, WorkItemOrigin.Template, new ExecutionActor(pr.OwnerId, "PRMake", false), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Item do review do PR {Repository}#{Number} não criado no card {Card}", pr.Repository, pr.Number, pr.Card);
            await PostAsync(pr, $"O PR [#{pr.Number}]({pr.Url}) recebeu **pedido de mudanças** de {whoText}{pointsText}. Não consegui criar o item " +
                                $"para atender ({e.Message}) — use a ferramenta **atender-review-pr** ou peça ao especialista.", ct);
            return;
        }

        await PostAsync(pr, $"O PR [#{pr.Number}]({pr.Url}) recebeu **pedido de mudanças** de {whoText}{pointsText}. Criei o item #{created.Number} " +
                            "para atender: ler os pontos (só o código novo do PR), ajustar, levar aos PRs do card e responder no PR pedindo nova " +
                            "revisão. Clique em **Executar** na Bancada ou peça ao especialista.", ct);
    }

    private static bool SamePullRequest(WorkItemResponse item, int number) =>
        item.Tasks.Any(t => t.Params.TryGetValue("pullRequest", out var v) && v?.Trim().TrimStart('#') == number.ToString());

    private async Task<(List<string> Who, int Points)> ReviewersAsync(PullRequestChangesRequested pr, CancellationToken ct)
    {
        try
        {
            var github = services.GetRequiredService<IGitHubService>();
            var r = await github.GetPullRequestReviewsAsync(pr.Repository, pr.Number, ct);
            if (r.Error is not null) return ([], 0);
            return (r.ChangesRequestedBy, r.Comments.Count(c => c.InReplyTo is null && !c.Outdated));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogInformation("Review do PR {Repository}#{Number} indisponível: {Error}", pr.Repository, pr.Number, e.Message);
            return ([], 0);
        }
    }

    private async Task PostAsync(PullRequestChangesRequested pr, string text, CancellationToken ct)
    {
        try
        {
            await services.GetRequiredService<CaseChatApplication>().PostSystemAsync(pr.Card, text, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Aviso do review não postado na conversa do card {Card}", pr.Card);
        }
    }
}
