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
    /// <summary>Conferência do Dev Test in QA no máximo a cada 30 s por plano (a leitura do plano é frequente).</summary>
    private static readonly TimeSpan DevTestInQaCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> LastDevTestInQaCheck = new();
    /// <summary>Último erro ao publicar a situação na discussion, por plano (o aviso no plano sai uma vez por erro).</summary>
    private static readonly ConcurrentDictionary<Guid, string> LastDiscussionError = new();

    public async Task<CardSituationResponse> GetSituationAsync(string cardNumber, bool withDevOps, CancellationToken cancellationToken)
    {
        var card = cardNumber.Trim();
        var planId = await _repository.GetCurrentPlanIdAsync(card, cancellationToken);
        var situation = planId is { } id ? await BuildSituationAsync(id, true, cancellationToken) : CardSituation.Build(null, [], [], [], _ => false, card);
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
        // O contexto da passagem mudou o resumo: o comentário da discussion acompanha.
        await PostSituationToDevOpsAsync(plan.Id, plan.CardNumber, cancellationToken);
        return await GetSituationAsync(plan.CardNumber, false, cancellationToken);
    }

    /// <summary>Publicação pela tela (botão Publicar). Sem <paramref name="html"/>, o PRMake monta o mesmo texto da automática.</summary>
    public async Task<int> PublishSituationAsync(string cardNumber, string? html, ExecutionActor actor, CancellationToken cancellationToken, string? markdown = null)
    {
        if (_devOps is null)
            throw new DomainException("Integração com o DevOps indisponível.");
        var card = cardNumber.Trim();
        var planId = await _repository.GetCurrentPlanIdAsync(card, cancellationToken)
                     ?? throw new ExecutionPlanNotFoundException("O card não tem plano.");
        var situation = await GetSituationAsync(card, true, cancellationToken);
        // 0078: texto editado pela pessoa vale como está (o PRMake só converte para o HTML da discussion).
        var text = string.IsNullOrWhiteSpace(markdown) ? situation.Markdown : markdown.Trim();
        var body = string.IsNullOrWhiteSpace(html) ? SituationHtml.Build(text, DateTimeOffset.UtcNow, CardSituation.Brasilia) : html;
        var existing = await _repository.GetSituationCommentIdAsync(planId, cancellationToken);
        int commentId;
        try
        {
            commentId = await _devOps.UpsertCommentAsync(card, body, existing, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not DomainException)
        {
            // Sem integração do DevOps, DevOps fora: mensagem clara para a tela (400), não 500.
            throw new DomainException($"Não deu para publicar na discussion do DevOps: {e.Message}");
        }
        await _repository.SetSituationPostedAsync(planId, commentId, text, actor.Name, DateTimeOffset.UtcNow, cancellationToken);
        var plan = await LoadAsync(planId, cancellationToken);
        await WriteTimelineAsync(plan, $"📣 **Situação publicada na discussion do DevOps** por {actor.Name}", actor, cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Situation, null, cancellationToken);
        return commentId;
    }

    public async Task<SituationPostSettingsResponse> GetSituationPostSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        new() { AutoPost = await _repository.GetSituationAutoPostAsync(userId, cancellationToken), GlobalDefault = (await QaSettingsAsync(cancellationToken)).PostSituationToDevOps };

    public async Task<SituationPostSettingsResponse> SetSituationPostSettingsAsync(Guid userId, bool? autoPost, CancellationToken cancellationToken)
    {
        await _repository.SetSituationAutoPostAsync(userId, autoPost, cancellationToken);
        return await GetSituationPostSettingsAsync(userId, cancellationToken);
    }

    /// <param name="withFiles">Arquivos das etapas de chamado (só para o texto; o tipo não depende deles).</param>
    private async Task<CardSituationResponse> BuildSituationAsync(Guid planId, bool withFiles, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var links = await _repository.GetLinksAsync(planId, cancellationToken);
        var questions = await _repository.GetQuestionsAsync(planId, cancellationToken);
        var artifacts = withFiles ? await _repository.GetArtifactsAsync(planId, cancellationToken) : [];
        var settings = await QaSettingsAsync(cancellationToken);
        var situation = CardSituation.Build(plan, links, questions, artifacts, settings.IsQaBranch, plan.CardNumber);
        if (withFiles)
        {
            // 0076/0078: automático = escolha feita para este card (plano atual ou a análise), senão a preferência do dono do
            // plano (quem trata o card), senão o padrão global.
            var card = plan.SituationAutoPost;
            if (card is null && plan.ParentPlanId is { } parentId)
                card = (await _repository.GetPlanWithStepsAsync(parentId, cancellationToken))?.SituationAutoPost;
            var own = card is null && plan.CreatedByUserId is { } owner ? await _repository.GetSituationAutoPostAsync(owner, cancellationToken) : null;
            situation.AutoPost = card ?? own ?? settings.PostSituationToDevOps;
            situation.AutoPostSource = card is not null ? "card" : own is not null ? "user" : "global";
            situation.PublishPending = situation.Markdown != situation.PublishedMarkdown
                                       && CardSituationKind.PostToDevOps(situation, plan.SituationCommentId is not null);
        }
        return situation;
    }

    /// <summary>
    /// Publica sozinho (cria ou atualiza o mesmo comentário) o resumo da situação na discussion do card no DevOps —
    /// em chamado, PR ou QA, e para atualizar o comentário quando a espera muda ou acaba. Config
    /// <c>SituationPostToDevOps</c>. Best-effort: falha vira aviso no plano (uma vez por erro), nunca quebra a operação.
    /// </summary>
    private async Task PostSituationToDevOpsAsync(Guid planId, string cardNumber, CancellationToken cancellationToken)
    {
        if (_devOps is null) return;
        try
        {
            var commentId = await _repository.GetSituationCommentIdAsync(planId, cancellationToken);
            var situation = await BuildSituationAsync(planId, true, cancellationToken);
            // Manual (preferência do dono ou padrão): fica pendente na tela, com o botão Publicar.
            if (!situation.AutoPost || !CardSituationKind.PostToDevOps(situation, commentId is not null)) return;
            if (situation.Markdown == situation.PublishedMarkdown) return;
            try
            {
                (situation.DevOpsState, situation.DevOpsColumn) = await _devOps.GetStateAsync(cardNumber, cancellationToken);
                CardSituation.Finish(situation);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Estado do DevOps é enfeite do resumo.
            }
            var html = SituationHtml.Build(situation, DateTimeOffset.UtcNow, CardSituation.Brasilia);
            var id = await _devOps.UpsertCommentAsync(cardNumber, html, commentId, cancellationToken);
            await _repository.SetSituationPostedAsync(planId, id, situation.Markdown, "PRMake (automático)", DateTimeOffset.UtcNow, cancellationToken);
            if (id != commentId)
                // Primeira vez (ou o comentário foi apagado no DevOps): avisa uma vez na Timeline; depois só atualiza.
                await WriteTimelineAsync(await LoadAsync(planId, cancellationToken),
                    $"📣 **Situação publicada na discussion do DevOps** ({situation.Title}) — o PRMake atualiza o mesmo comentário a cada mudança.",
                    SystemActor, cancellationToken);
            LastDiscussionError.TryRemove(planId, out _);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var error = e.Message.Length <= 300 ? e.Message : e.Message[..300];
            if (LastDiscussionError.TryGetValue(planId, out var last) && last == error) return;
            LastDiscussionError[planId] = error;
            try
            {
                await AppendLogsAsync(planId, new AppendExecutionLogsRequest
                {
                    Logs = [new ExecutionLogItem { Kind = ExecutionLogKind.Warning,
                        Message = $"Não deu para publicar a situação na discussion do DevOps: {error}. Publique pelo cartão *Situação atual* (Publicar no DevOps)." }]
                }, cancellationToken);
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                // Aviso é best-effort.
            }
        }
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
            var situation = await BuildSituationAsync(plan.Id, false, cancellationToken);
            var stored = await _repository.GetSituationKindAsync(plan.Id, cancellationToken);
            var before = stored ?? CardSituationKind.None;
            if (situation.Kind == before
                || !await _repository.SetSituationKindAsync(plan.Id, stored, situation.Kind, DateTimeOffset.UtcNow, cancellationToken))
                return;
            if (CardSituationKind.Milestone(situation))
                situation = await BuildSituationAsync(plan.Id, true, cancellationToken);

            var finished = situation.PlanStatus is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed;
            if (CardSituationKind.Milestone(situation))
                await WriteTimelineAsync(plan, situation.Markdown, SystemActor, cancellationToken);
            else if ((CardSituationKind.Milestone(before) || before == CardSituationKind.User) && !finished)
                await WriteTimelineAsync(plan, $"▶️ **Situação:** não está mais em *{CardSituationKind.Title(before)}*" +
                                               (situation.Kind == CardSituationKind.None ? " — o plano segue." : $" — agora: *{situation.Title}*."), SystemActor, cancellationToken);

            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(plan.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = plan.CardNumber, planId = plan.Id, action = ExecutionPlanRealTimeEvents.Actions.Situation, stepKey = (string?)null, status = plan.Status },
                cancellationToken);
            await PostSituationToDevOpsAsync(plan.Id, plan.CardNumber, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Best-effort: a situação é recalculada na próxima mudança.
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
        if (LastDevTestInQaCheck.TryGetValue(planId, out var last) && now - last < DevTestInQaCheckInterval)
            return;
        LastDevTestInQaCheck[planId] = now;
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
            // Movido (ou não) para Dev Test in QA: o resumo da discussion mostra o estado novo.
            await PostSituationToDevOpsAsync(planId, peek.CardNumber, cancellationToken);
        }
        finally
        {
            DevTestInQaRunning.TryRemove(planId, out _);
        }
    }
}
