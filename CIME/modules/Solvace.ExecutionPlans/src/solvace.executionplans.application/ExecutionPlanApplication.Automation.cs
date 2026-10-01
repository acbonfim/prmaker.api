using System.Collections.Concurrent;
using System.Text;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.application;

/// <summary>
/// Automação do plano (0024): PRs mesclados concluem as etapas de PR (e o plano de correção), e os marcos
/// do plano vão para a Timeline do card — para ações feitas pela skill e pela tela.
/// </summary>
public partial class ExecutionPlanApplication
{
    /// <summary>Autor das mudanças automáticas (sincronização do GitHub).</summary>
    private static readonly ExecutionActor SystemActor = new(null, "PRMake", false);

    /// <summary>
    /// Só contra rajadas (tela + skill + evento de tempo real no mesmo instante). O GitHub não é consultado a
    /// cada sincronização: o módulo de PRs tem cache de 1 min por PR e lê do banco os já mesclados/fechados —
    /// então um status gravado por outra tela (ou pelo vigia) aparece no plano na próxima leitura (0025).
    /// </summary>
    private static readonly TimeSpan PullRequestSyncInterval = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> LastPullRequestSync = new();

    private record StepSnapshot(string Status, string? Reason, string? WaitingOn);
    private record PlanSnapshot(string Status, IReadOnlyDictionary<string, StepSnapshot> Steps);

    private static PlanSnapshot Snapshot(ExecutionPlan plan) =>
        new(plan.Status, plan.Steps.ToDictionary(s => s.Key, s => new StepSnapshot(s.Status, s.StatusReason, s.WaitingOn)));

    /// <summary>
    /// Anexa às etapas de PR (uma por repositório) os PRs do card naquele repositório — abertos pela skill ou
    /// pela tela depois da criação do plano — e acompanha o status. Etapa conclui quando os PRs não fechados
    /// estão todos mesclados.
    /// Best-effort: sem credencial do GitHub ou com o GitHub fora, não faz nada.
    /// </summary>
    private async Task SyncPullRequestsAsync(Guid planId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (LastPullRequestSync.TryGetValue(planId, out var last) && now - last < PullRequestSyncInterval)
            return;

        var peek = await _repository.GetPlanWithStepsAsync(planId, cancellationToken);
        _repository.ClearTracking();
        if (peek is null || peek.Phase != ExecutionPhase.Correction || peek.IsFinished)
            return;
        var prSteps = peek.Steps
            .Where(s => s.Kind == ExecutionStepKind.PullRequest && s.Repository is not null && !ExecutionStatus.IsStepFinished(s.Status))
            .ToList();
        if (prSteps.Count == 0)
            return;

        IReadOnlyList<CardPullRequest> cardPrs;
        try
        {
            cardPrs = await _pullRequests.ListByCardAsync(peek.CardNumber, cancellationToken);
        }
        catch
        {
            LastPullRequestSync[planId] = now;
            return;
        }
        // Card ainda sem PR: consulta barata (só o banco) — não espera o intervalo para ver o primeiro PR.
        if (cardPrs.Count == 0)
            return;
        LastPullRequestSync[planId] = now;

        var merged = new List<ExecutionLink>();
        var changed = false;
        var plan = await MutateAsync(planId, async p =>
        {
            merged.Clear();
            changed = false;
            var links = await _repository.GetLinksAsync(p.Id, cancellationToken);
            foreach (var step in p.Steps.Where(s => s.Kind == ExecutionStepKind.PullRequest && s.Repository is not null && !ExecutionStatus.IsStepFinished(s.Status)).ToList())
            {
                foreach (var pr in cardPrs.Where(pr => pr.Number is not null && SameRepository(pr.Repository, step.Repository!)))
                {
                    var same = links.Where(l => l.Kind == ExecutionLinkKind.PullRequest
                                                && l.PullRequestNumber == pr.Number && SameRepository(l.Repository ?? "", pr.Repository))
                        .OrderBy(l => l.CreatedBy == SystemActor.Name ? 1 : 0).ThenBy(l => l.CreatedAt)
                        .ToList();
                    // Duplicata do mesmo PR (anexado ao mesmo tempo pela sincronização e por quem abriu): fica um só,
                    // de preferência o de quem abriu; a cópia "aberta" não pode segurar a etapa para sempre.
                    foreach (var duplicate in same.Skip(1))
                    {
                        _repository.RemoveLink(duplicate);
                        links.Remove(duplicate);
                        changed = true;
                    }

                    var link = same.FirstOrDefault();
                    if (link is null)
                    {
                        // PR registrado antes do plano é de uma rodada anterior do card (já mesclado ou fechado):
                        // anexá-lo concluiria a etapa antes dos PRs desta correção.
                        if (pr.CreatedAt < p.CreatedAt)
                            continue;
                        link = new ExecutionLink(p.Id, step.Key, pr.Url, $"#{pr.Number} {pr.Repository} → {pr.BaseBranch}", ExecutionLinkKind.PullRequest,
                            false, pr.Number, pr.Repository, pr.BaseBranch, SystemActor.Name, now);
                        _repository.AddLink(link);
                        links.Add(link);
                        changed = true;
                    }
                    if (link.ChangeStatus(pr.Status, "GitHub", now))
                    {
                        changed = true;
                        if (pr.Status == ExecutionLinkStatus.Merged) merged.Add(link);
                    }
                }

                var active = links.Where(l => l.StepKey == step.Key && l.Kind == ExecutionLinkKind.PullRequest && l.Status != ExecutionLinkStatus.Closed).ToList();
                if (active.Count > 0 && active.All(l => l.Status == ExecutionLinkStatus.Merged))
                {
                    p.CompleteStep(step.Key, active.Count == 1 ? "PR mesclado" : $"{active.Count} PRs mesclados", SystemActor.Name, now, false);
                    changed = true;
                }
            }
            if (changed) p.Touch(now, fromExecutor: false);
        }, cancellationToken, SystemActor,
            // PRs mesclados abrem o registro dos marcos (antes da etapa/plano concluídos por causa deles).
            () => merged.Count == 0 ? null : string.Join("\n", merged.Select(l => $"🔀 **PR mesclado**: [{l.DisplayName}]({l.Url})")));

        if (changed)
        {
            await NotifyAsync(plan, domain.RealTime.ExecutionPlanRealTimeEvents.Actions.Link, null, cancellationToken);
            await TriggerResumeAsync(plan, SystemActor, ExecutionRequestSource.PullRequest, cancellationToken);
        }
    }

    /// <summary>"*chave*" no texto vira "*Título da etapa*".</summary>
    private static string ReplaceStepKeys(ExecutionPlan plan, string text)
    {
        foreach (var step in plan.Steps)
            text = text.Replace($"*{step.Key}*", $"*{step.Title}*");
        return text;
    }

    private static bool SameRepository(string a, string b) =>
        string.Equals(a.Trim().Split('/').Last(), b.Trim().Split('/').Last(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Marcos do plano de correção na Timeline: etapa concluída/cancelada/falhou/aguardando, plano pausado/retomado/concluído/cancelado.</summary>
    private async Task WriteMilestonesAsync(PlanSnapshot before, ExecutionPlan plan, ExecutionActor actor, string? headline, CancellationToken cancellationToken)
    {
        if (plan.Phase != ExecutionPhase.Correction)
        {
            // Análise: só a abertura e as etapas que passaram a esperar uma ação do usuário (0037).
            var analysisLines = new List<string>();
            if (headline is not null) analysisLines.Add(ReplaceStepKeys(plan, headline));
            analysisLines.AddRange(UserWaitLines(before, plan));
            if (analysisLines.Count > 0) await WriteTimelineAsync(plan, string.Join("\n", analysisLines), actor, cancellationToken);
            return;
        }

        var lines = new List<string>();
        if (headline is not null) lines.Add(ReplaceStepKeys(plan, headline));
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            before.Steps.TryGetValue(step.Key, out var old);
            if (old is not null && old.Status == step.Status && old.Reason == step.StatusReason)
                continue;
            var reason = string.IsNullOrWhiteSpace(step.StatusReason) ? "" : $" — {step.StatusReason}";
            switch (step.Status)
            {
                case ExecutionStatus.Waiting when step.WaitingOn == ExecutionWaitingOn.User:
                    lines.Add(UserWaitLine(step)); break;
                case ExecutionStatus.Completed: lines.Add($"✅ Etapa concluída: **{step.Title}**{reason}"); break;
                case ExecutionStatus.Cancelled: lines.Add($"⛔ Etapa cancelada: **{step.Title}**{reason}"); break;
                case ExecutionStatus.Failed: lines.Add($"❌ Etapa falhou: **{step.Title}**{reason}"); break;
                // "Aguardando" de chamado/PR (o de pergunta já sai no registro das perguntas).
                case ExecutionStatus.Waiting when step.Kind is ExecutionStepKind.Ticket or ExecutionStepKind.PullRequest:
                    lines.Add($"⏳ Etapa aguardando: **{step.Title}**{reason}"); break;
            }
        }

        if (before.Status != plan.Status)
        {
            switch (plan.Status)
            {
                case ExecutionStatus.Completed:
                    await WriteTimelineAsync(plan, await CorrectionCompletedTextAsync(plan, lines, cancellationToken), actor, cancellationToken);
                    return;
                case ExecutionStatus.Paused: lines.Add($"⏸️ Plano de correção **pausado** por {actor.Name}"); break;
                case ExecutionStatus.Running when before.Status == ExecutionStatus.Paused: lines.Add($"▶️ Plano de correção **retomado** por {actor.Name}"); break;
                case ExecutionStatus.Cancelled: lines.Add($"⛔ Plano de correção **cancelado** por {actor.Name}{(plan.StatusReason is null ? "" : $" — {plan.StatusReason}")}"); break;
                case ExecutionStatus.Failed: lines.Add($"❌ Plano de correção **falhou**{(plan.StatusReason is null ? "" : $" — {plan.StatusReason}")}"); break;
            }
        }

        if (lines.Count == 0)
            return;
        if (lines.Count == 1 && headline is not null)
        {
            await WriteTimelineAsync(plan, lines[0], actor, cancellationToken);
            return;
        }
        lines.Add("");
        lines.Add(RemainingText(plan));
        await WriteTimelineAsync(plan, string.Join("\n", lines), actor, cancellationToken);
    }

    /// <summary>Etapas que entraram em "aguardando o usuário" nesta alteração (0037).</summary>
    private static IEnumerable<string> UserWaitLines(PlanSnapshot before, ExecutionPlan plan) =>
        plan.Steps.OrderBy(s => s.Order)
            .Where(s => s.Status == ExecutionStatus.Waiting && s.WaitingOn == ExecutionWaitingOn.User
                        && !(before.Steps.TryGetValue(s.Key, out var old) && old.Status == s.Status && old.WaitingOn == s.WaitingOn && old.Reason == s.StatusReason))
            .Select(UserWaitLine);

    private static string UserWaitLine(ExecutionStep step) =>
        $"⚠️ **Aguardando você** em *{step.Title}*: {step.StatusReason}";

    private static string CorrectionPlanCreatedText(ExecutionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🛠️ **Plano de correção criado** — {plan.Title}");
        sb.AppendLine();
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
            sb.AppendLine($"- {ExecutorLabel(step)} {step.Title}{(step.Repository is null ? "" : $" · {step.Repository}")}");
        return sb.ToString().TrimEnd();
    }

    private static string QuestionsAskedText(ExecutionPlan plan, IReadOnlyList<ExecutionQuestion> questions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(questions.Count == 1 ? "❓ **Pergunta para o usuário** (responda no PRMake ou no Claude)" : $"❓ **{questions.Count} perguntas para o usuário** (responda no PRMake ou no Claude)");
        sb.AppendLine();
        var i = 0;
        foreach (var q in questions)
        {
            sb.AppendLine($"{++i}. {q.Text}");
            // 0032: rótulos genéricos ("Opção 1") não dizem nada sem a descrição — vai o texto de cada opção.
            foreach (var o in q.Options)
                sb.AppendLine($"   - **{o.Label}**{OptionDetail(o)}{(o.Recommended ? " *(recomendada)*" : "")}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string AnswerText(ExecutionQuestion q)
    {
        var chosen = q.Options.FirstOrDefault(o => string.Equals(o.Label, q.Answer, StringComparison.OrdinalIgnoreCase));
        var answer = chosen is null ? q.Answer : $"**{chosen.Label}**{OptionDetail(chosen)}";
        return $"💬 **Resposta** ({(q.AnsweredVia == "claude" ? "pelo Claude" : "pelo PRMake")}, {q.AnsweredBy})\n\n> {q.Text.Replace("\n", "\n> ")}\n\n{answer}";
    }

    private static string OptionDetail(ExecutionQuestionOption option) =>
        string.IsNullOrWhiteSpace(option.Description) ? "" : $" — {option.Description.Trim().Replace("\n", " ")}";

    private async Task<string> CorrectionCompletedTextAsync(ExecutionPlan plan, List<string> stepLines, CancellationToken cancellationToken)
    {
        var links = await _repository.GetLinksAsync(plan.Id, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine($"🏁 **Plano de correção concluído** — {plan.Title}");
        foreach (var line in stepLines) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("**Etapas**");
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
            sb.AppendLine($"- {(step.Status == ExecutionStatus.Completed ? "✅" : "⛔")} {step.Title}{(step.StatusReason is null ? "" : $" — {step.StatusReason}")}");
        var prs = links.Where(l => l.Kind == ExecutionLinkKind.PullRequest).ToList();
        if (prs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**PRs**");
            foreach (var l in prs) sb.AppendLine($"- [{l.DisplayName}]({l.Url}) — {PullRequestStatusLabel(l.Status)}");
        }
        var tickets = links.Where(l => l.Kind == ExecutionLinkKind.Ticket).ToList();
        if (tickets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Chamados**");
            foreach (var l in tickets) sb.AppendLine($"- [{l.DisplayName}]({l.Url}) — {TicketStatusLabel(l.Status)}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>"Falta: ..." — o que ainda não terminou (com quem executa e se está aguardando).</summary>
    private static string RemainingText(ExecutionPlan plan)
    {
        var pending = plan.Steps.Where(s => !ExecutionStatus.IsStepFinished(s.Status)).OrderBy(s => s.Order).ToList();
        if (pending.Count == 0)
            return "**Falta:** nada — todas as etapas terminaram.";
        return "**Falta:** " + string.Join(" · ", pending.Select(s =>
            $"{ExecutorLabel(s)} {s.Title}{(s.Status == ExecutionStatus.Waiting ? " *(aguardando)*" : s.Status == ExecutionStatus.Running ? " *(em andamento)*" : "")}"));
    }

    private static string ExecutorLabel(ExecutionStep step) => step.Executor == ExecutionExecutor.User ? "👤" : "🤖";


    private static string PullRequestStatusLabel(string? status) => status switch
    {
        ExecutionLinkStatus.Merged => "mesclado",
        ExecutionLinkStatus.Closed => "fechado",
        _ => "aberto"
    };

    private static string TicketStatusLabel(string? status) => status switch
    {
        ExecutionLinkStatus.Resolved => "resolvido",
        ExecutionLinkStatus.Closed => "fechado",
        _ => "aberto"
    };

    /// <summary>Best-effort: a Timeline fora nunca quebra a operação do plano.</summary>
    private async Task WriteTimelineAsync(ExecutionPlan plan, string markdown, ExecutionActor actor, CancellationToken cancellationToken)
    {
        try
        {
            await _timeline.WriteAsync(plan.CardNumber, markdown, actor.UserId, actor.Name, cancellationToken);
        }
        catch
        {
            // ignora
        }
    }
}
