using System.Collections.Concurrent;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0075: "Situação atual" do card — o que está sendo aguardado (chamado, aprovação/merge, validação em QA, resposta,
/// usuário), com os fatos do plano e o contexto que a skill deixou (handoff). Entrar/sair de chamado, PR ou QA vira uma
/// entrada na Timeline. PR da branch de QA mesclado move o card para Dev Test in QA sem depender da sessão do Claude.
/// </summary>
public partial class ExecutionPlanApplication
{
    private static readonly ConcurrentDictionary<Guid, byte> DevTestInQaRunning = new();

    public async Task<CardSituationResponse> GetSituationAsync(string cardNumber, bool withDevOps, CancellationToken cancellationToken)
    {
        var card = cardNumber.Trim();
        var planId = await _repository.GetCurrentPlanIdAsync(card, cancellationToken);
        var situation = planId is { } id ? (await BuildSituationAsync(id, cancellationToken)).Situation : CardSituation.Build(null, [], [], [], _ => false, card);
        if (withDevOps && _devOps is not null)
        {
            try
            {
                (situation.DevOpsState, situation.DevOpsColumn) = await _devOps.GetStateAsync(card, cancellationToken);
                CardSituation.Finish(situation);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Estado do DevOps é enfeite do resumo.
            }
        }
        return situation;
    }

    public async Task<CardSituationResponse> SetHandoffAsync(Guid planId, string? text, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p => p.SetHandoff(text, actor.Name, DateTimeOffset.UtcNow), cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Situation, null, cancellationToken);
        return await GetSituationAsync(plan.CardNumber, false, cancellationToken);
    }

    public async Task<int> PublishSituationAsync(string cardNumber, string html, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (_devOps is null)
            throw new DomainException("Integração com o DevOps indisponível.");
        if (string.IsNullOrWhiteSpace(html))
            throw new DomainException("Resumo vazio.");
        var card = cardNumber.Trim();
        var planId = await _repository.GetCurrentPlanIdAsync(card, cancellationToken)
                     ?? throw new ExecutionPlanNotFoundException("O card não tem plano.");
        var current = await LoadAsync(planId, cancellationToken);
        var commentId = await _devOps.UpsertCommentAsync(card, html, current.SituationCommentId, cancellationToken);
        _repository.ClearTracking();
        var plan = await MutateAsync(planId, p => p.SetSituationComment(commentId, DateTimeOffset.UtcNow), cancellationToken, actor);
        await WriteTimelineAsync(plan, $"📣 **Situação publicada no DevOps** por {actor.Name}", actor, cancellationToken);
        return commentId;
    }

    /// <returns>A situação e o tipo gravado no plano (para saber se mudou).</returns>
    private async Task<(CardSituationResponse Situation, string? Stored)> BuildSituationAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var links = await _repository.GetLinksAsync(planId, cancellationToken);
        var questions = await _repository.GetQuestionsAsync(planId, cancellationToken);
        var artifacts = await _repository.GetArtifactsAsync(planId, cancellationToken);
        var settings = await QaSettingsAsync(cancellationToken);
        _repository.ClearTracking();
        return (CardSituation.Build(plan, links, questions, artifacts, settings.IsQaBranch, plan.CardNumber), plan.SituationKind);
    }

    private async Task<ExecutionQaSettings> QaSettingsAsync(CancellationToken cancellationToken)
    {
        if (_devOps is null) return ExecutionQaSettings.Default;
        try
        {
            return await _devOps.GetQaSettingsAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ExecutionQaSettings.Default;
        }
    }

    /// <summary>
    /// Depois de cada mudança do plano: se o tipo da situação mudou, grava e — entrando/saindo de chamado, PR ou QA — escreve
    /// o resumo na Timeline (substitui o antigo "⏳ Etapa aguardando"). Best-effort.
    /// </summary>
    private async Task TrackSituationAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            var (situation, stored) = await BuildSituationAsync(plan.Id, cancellationToken);
            var before = stored ?? CardSituationKind.None;
            if (situation.Kind == before
                || !await _repository.SetSituationKindAsync(plan.Id, stored, situation.Kind, DateTimeOffset.UtcNow, cancellationToken))
                return;

            var finished = situation.PlanStatus is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed;
            if (CardSituationKind.Milestone(situation.Kind))
                await WriteTimelineAsync(plan, situation.Markdown, SystemActor, cancellationToken);
            else if (CardSituationKind.Milestone(before) && !finished)
                await WriteTimelineAsync(plan, $"▶️ **Situação:** não está mais em *{CardSituationKind.Title(before)}*" +
                                               (situation.Kind == CardSituationKind.None ? " — o plano segue." : $" — agora: *{situation.Title}*."), SystemActor, cancellationToken);

            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(plan.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = plan.CardNumber, planId = plan.Id, action = ExecutionPlanRealTimeEvents.Actions.Situation, stepKey = (string?)null, status = plan.Status },
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _repository.ClearTracking();
        }
    }

    /// <summary>
    /// PR da branch de QA mesclado + <c>validar-qa</c> pendente → move o card para Dev Test in QA (uma vez; falhou = nova
    /// tentativa em 10 min). Roda na leitura do plano e no heartbeat da skill — sem depender da sessão do Claude.
    /// </summary>
    private async Task EnsureDevTestInQaAsync(Guid planId, CancellationToken cancellationToken)
    {
        if (_devOps is null) return;
        var now = DateTimeOffset.UtcNow;
        var peek = await _repository.GetPlanWithStepsAsync(planId, cancellationToken);
        _repository.ClearTracking();
        if (peek is null || peek.Phase != ExecutionPhase.Correction || peek.IsFinished || !peek.DevTestInQaDue(now)
            || peek.QaValidationStep is not { } qa || ExecutionStatus.IsStepFinished(qa.Status))
            return;

        var settings = await QaSettingsAsync(cancellationToken);
        if (!settings.AutoDevTestInQa)
            return;
        var links = await _repository.GetLinksAsync(planId, cancellationToken);
        _repository.ClearTracking();
        var merged = links.FirstOrDefault(l => l.Kind == ExecutionLinkKind.PullRequest && l.Status == ExecutionLinkStatus.Merged && settings.IsQaBranch(l.TargetBranch));
        if (merged is null || !DevTestInQaRunning.TryAdd(planId, 0))
            return;
        try
        {
            string? error;
            try
            {
                error = await _devOps.MoveToDevTestInQaAsync(peek.CardNumber, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error = e.Message;
            }
            var firstFailure = error is not null && peek.DevTestInQaError is null;
            var plan = await MutateAsync(planId, p => p.RecordDevTestInQa(error, DateTimeOffset.UtcNow), cancellationToken);
            if (firstFailure)
                await AppendLogsAsync(planId, new AppendExecutionLogsRequest
                {
                    Logs = [new ExecutionLogItem { StepKey = qa.Key, Kind = ExecutionLogKind.Warning,
                        Message = $"PR de QA [{merged.DisplayName}]({merged.Url}) mesclado, mas não deu para mover o card para Dev Test in QA: {error}. Nova tentativa em 10 min (ou mova pelo menu do DevOps)." }]
                }, cancellationToken);
            await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Situation, qa.Key, cancellationToken);
        }
        finally
        {
            DevTestInQaRunning.TryRemove(planId, out _);
        }
    }
}
