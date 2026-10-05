using Microsoft.EntityFrameworkCore;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;
using solvace.knowledge.infra.Contexts;
using solvace.prform.Infra.Contexts;
using solvace.timeline.infra.Contexts;

namespace solvace.prform.Admin;

/// <summary>Quanto existe (ou foi apagado) de um card em cada parte do PRMake.</summary>
public sealed record CardResetCounts(int Plans, int Steps, int Logs, int Artifacts, int Questions, int Notes, int Links, int Requests,
    int Timeline, int PullRequests, int GithubPullRequests, int ReverseCardContexts);

/// <param name="Blocked">Motivo de não apagar (pedido rodando, PR no GitHub) — null = apagou (ou apagaria, no ensaio).</param>
public sealed record CardResetResult(string Card, bool DryRun, CardResetCounts Counts, string? Blocked, List<string> Kept);

/// <summary>
/// 0061: recomeça um card no PRMake — apaga planos (etapas, logs, anexos, perguntas, links, notas), a fila, a Timeline, o
/// registro da tabela de PR e o registro do card na engenharia reversa, para refazer a análise do zero. Mantém o que não é
/// do card em si: lacunas/sugestões e armadilhas da Base Solvace (conhecimento) e tudo no DevOps/GitHub. Recusa com pedido
/// na fila/rodando (a sessão recriaria o plano) e mantém a linha de PR quando já há PR no GitHub ligado a ela.
/// Só admin; os contextos são do mesmo banco, apagados em sequência (planos primeiro).
/// </summary>
public sealed class CardResetService(ExecutionPlanContext plans, TimelineContext timeline, DefaultContext prform, KnowledgeContext knowledge,
    ILogger<CardResetService> logger)
{
    public async Task<CardResetResult> ResetAsync(string card, bool dryRun, string actor, CancellationToken ct)
    {
        card = (card ?? string.Empty).Trim();
        if (card.Length == 0) throw new ArgumentException("Informe o card.");

        var planIds = await plans.Plans.Where(p => p.CardNumber == card).Select(p => p.Id).ToListAsync(ct);
        var artifactIds = await plans.Artifacts.Where(a => planIds.Contains(a.PlanId)).Select(a => a.Id).ToListAsync(ct);
        var github = await prform.PullRequestsGithub.CountAsync(g => g.CardNumber == card, ct);
        var counts = new CardResetCounts(
            planIds.Count,
            await plans.Steps.CountAsync(s => planIds.Contains(s.PlanId), ct),
            await plans.Logs.CountAsync(l => planIds.Contains(l.PlanId), ct),
            artifactIds.Count,
            await plans.Questions.CountAsync(q => planIds.Contains(q.PlanId), ct),
            await plans.Notes.CountAsync(n => n.CardNumber == card, ct),
            await plans.Links.CountAsync(l => planIds.Contains(l.PlanId), ct),
            await plans.Requests.CountAsync(r => r.CardNumber == card, ct),
            await timeline.TimelineEntries.CountAsync(t => t.CardNumber == card, ct),
            await prform.PullRequests.CountAsync(p => p.CardNumber == card, ct),
            github,
            await knowledge.ReverseCardContexts.CountAsync(c => c.CardNumber == card, ct));

        var kept = new List<string> { "lacunas, sugestões e armadilhas da Base Solvace", "campos e comentários do card no DevOps" };
        if (github > 0) kept.Add($"tabela de PR ({github} PR(s) no GitHub ligados ao card)");

        var active = ExecutionRequestStatus.Active.ToList();
        if (await plans.Requests.AnyAsync(r => r.CardNumber == card && active.Contains(r.Status), ct))
            return new CardResetResult(card, dryRun, counts, "Há um pedido na fila ou rodando para o card — cancele-o na tela do card antes de recomeçar.", kept);
        if (dryRun) return new CardResetResult(card, true, counts, null, kept);

        await plans.ArtifactContents.Where(c => artifactIds.Contains(c.ArtifactId)).ExecuteDeleteAsync(ct);
        await plans.Plans.Where(p => p.CardNumber == card && p.ParentPlanId != null).ExecuteDeleteAsync(ct);
        await plans.Plans.Where(p => p.CardNumber == card).ExecuteDeleteAsync(ct);   // etapas, logs, anexos, perguntas, links: cascata
        await plans.Notes.Where(n => n.CardNumber == card).ExecuteDeleteAsync(ct);
        await plans.Requests.Where(r => r.CardNumber == card).ExecuteDeleteAsync(ct);
        await timeline.TimelineEntries.Where(t => t.CardNumber == card).ExecuteDeleteAsync(ct);
        if (github == 0) await prform.PullRequests.Where(p => p.CardNumber == card).ExecuteDeleteAsync(ct);
        await knowledge.ReverseCardContexts.Where(c => c.CardNumber == card).ExecuteDeleteAsync(ct);

        logger.LogWarning("Card {Card} recomeçado por {Actor}: {Counts}", card, actor, counts);
        return new CardResetResult(card, false, counts, null, kept);
    }
}
