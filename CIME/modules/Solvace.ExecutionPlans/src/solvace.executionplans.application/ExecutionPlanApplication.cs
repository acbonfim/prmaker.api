using System.IO.Compression;
using System.Security.Cryptography;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

public partial class ExecutionPlanApplication : IExecutionPlanApplication
{
    /// <summary>Máximo de pedaços por lote (a fila local da skill reenvia em lotes).</summary>
    public const int MaxLogsPerRequest = 200;

    private const int MaxLogsPerPage = 1000;
    private const int ConcurrencyRetries = 8;

    private readonly IExecutionPlanRepository _repository;
    private readonly IRealTimeNotifier _realTimeNotifier;
    private readonly IExecutionPullRequestSource _pullRequests;
    private readonly IExecutionTimelineWriter _timeline;
    private readonly IExecutionCardRegistrar? _cards;
    private readonly IExecutionResumeTrigger? _resumeTrigger;

    public ExecutionPlanApplication(IExecutionPlanRepository repository, IRealTimeNotifier realTimeNotifier,
        IExecutionPullRequestSource pullRequests, IExecutionTimelineWriter timeline, IExecutionCardRegistrar? cards = null,
        IExecutionResumeTrigger? resumeTrigger = null)
    {
        _cards = cards;
        _resumeTrigger = resumeTrigger;
        _repository = repository;
        _realTimeNotifier = realTimeNotifier;
        _pullRequests = pullRequests;
        _timeline = timeline;
    }

    public async Task<ExecutionPlanResponse> CreateAsync(CreateExecutionPlanRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        ExecutionSession? parentSession = null;
        if (request.ParentPlanId is { } parentId)
        {
            // Plano de correção: nasce de um plano de análise do mesmo card.
            var parent = await _repository.GetPlanWithStepsAsync(parentId, cancellationToken)
                         ?? throw new DomainException("Plano de análise de origem não encontrado.");
            if (!string.Equals(parent.CardNumber, request.CardNumber?.Trim(), StringComparison.Ordinal))
                throw new DomainException("O plano de origem é de outro card.");
            if (parent.Phase != ExecutionPhase.Analysis)
                throw new DomainException("O plano de origem precisa ser um plano de análise.");
            parentSession = parent.CurrentSession;
            _repository.ClearTracking();
        }
        var plan = new ExecutionPlan(request.CardNumber!, request.Kind, request.Title, request.Summary, actor.UserId, actor.Name, now,
            request.Phase, request.ParentPlanId);
        // 0041: a correção é a mesma conversa do Claude que fez a análise — herda a sessão (o executor retoma por ela;
        // a skill pelo script registraria a mesma, o MCP não sabe o id da sessão). 0049: no executor a correção roda numa
        // sessão nova — o plano fica com a sessão que está rodando agora, não com a da análise.
        if (parentSession is not null)
        {
            var running = await RunningSessionAsync(request.CardNumber!, cancellationToken);
            var session = running ?? parentSession;
            plan.RegisterSession(session.SessionId, session.Host, running is null ? parentSession.Cwd : null, now);
        }
        if (request.Steps.Count > 0)
            plan.UpsertSteps(request.Steps, now);

        _repository.AddPlan(plan);
        await _repository.SaveChangesAsync(cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Created, null, cancellationToken);
        await EnsureCardRegisteredAsync(plan, actor, cancellationToken);
        if (plan.Phase == ExecutionPhase.Correction)
            await WriteTimelineAsync(plan, CorrectionPlanCreatedText(plan), actor, cancellationToken);
        return plan.ToResponse([], 0, now);
    }

    /// <summary>0037: o card já fica salvo no PRMake (registro + "últimos cards") ao nascer o plano. Best-effort.</summary>
    private async Task EnsureCardRegisteredAsync(ExecutionPlan plan, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (_cards is null || actor.UserId is not { } userId) return;
        try
        {
            await _cards.EnsureRegisteredAsync(plan.CardNumber, userId, cancellationToken);
        }
        catch
        {
            // O plano não depende disso: a gerar-prmake/save-pr-text salvam o card depois.
        }
    }

    public Task<List<ExecutionPlanSummaryResponse>> GetByCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _repository.GetSummariesByCardAsync(cardNumber.Trim(), cancellationToken);

    public async Task<ExecutionPlanResponse?> GetCurrentAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var id = await _repository.GetCurrentPlanIdAsync(cardNumber.Trim(), cancellationToken);
        return id is null ? null : await GetAsync(id.Value, cancellationToken);
    }

    public async Task<ExecutionPlanResponse> GetAsync(Guid planId, CancellationToken cancellationToken)
    {
        await SyncPullRequestsAsync(planId, cancellationToken);
        var plan = await LoadAsync(planId, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionPlanResponse> UpsertStepsAsync(Guid planId, UpsertExecutionStepsRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (request.Steps.Count == 0)
            throw new DomainException("Informe ao menos uma etapa.");

        var plan = await MutateAsync(planId, p => p.UpsertSteps(request.Steps, DateTimeOffset.UtcNow), cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Steps, null, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionStepResponse> UpdateStepAsync(Guid planId, string stepKey, UpdateExecutionStepRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p =>
        {
            step = p.UpdateStep(stepKey, request.Status, request.Reason, request.Activity, request.Checkpoint,
                request.Title, request.Description, actor.Name, DateTimeOffset.UtcNow, request.WaitingOn);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionStepResponse> CancelStepAsync(Guid planId, string stepKey, string reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p =>
        {
            var why = string.IsNullOrWhiteSpace(reason) ? $"Cancelada por {actor.Name}" : $"{reason.Trim()} — {actor.Name}";
            step = p.CancelStep(stepKey, why, actor.Name, DateTimeOffset.UtcNow);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionPlanResponse> ChangeStatusAsync(Guid planId, ChangeExecutionPlanStatusRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p =>
            p.ChangeStatus(request.Status, request.Reason, request.Summary, actor.Name, actor.IsExecutor, DateTimeOffset.UtcNow),
            cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        if (plan.Status == ExecutionStatus.Running)
            await TriggerResumeAsync(plan, actor, ExecutionRequestSource.Resume, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionControlResponse> ControlAsync(Guid planId, CancellationToken cancellationToken)
    {
        await SyncPullRequestsAsync(planId, cancellationToken);
        var plan = await MutateAsync(planId, p => p.Touch(DateTimeOffset.UtcNow, fromExecutor: true), cancellationToken);

        var action = plan.Status switch
        {
            ExecutionStatus.Paused => "wait",
            ExecutionStatus.Cancelled or ExecutionStatus.Completed => "stop",
            _ => "continue"
        };

        var questions = await _repository.GetQuestionsAsync(plan.Id, cancellationToken);
        var openQuestions = questions.Count(q => q.Status == ExecutionQuestionStatus.Open);
        var userActions = plan.UserActions(questions);
        var (lastUserNote, userNotesChangedAt) = await _repository.GetUserNotesStateAsync(plan.CardNumber, cancellationToken);

        // Sem evento de tempo real: o heartbeat é frequente e a tela calcula "sem sinal" sozinha.
        return new ExecutionControlResponse
        {
            ReadySteps = plan.ReadySteps().Select(s => s.Key).ToList(),
            WaitingSteps = plan.Steps.Where(s => s.Status == ExecutionStatus.Waiting).OrderBy(s => s.Order).Select(s => s.Key).ToList(),
            Steps = plan.Steps.OrderBy(s => s.Order)
                .Select(s => new ExecutionControlStep(s.Key, s.Status, s.Executor, s.WaitingOn, s.StatusReason, s.StatusChangedBy)).ToList(),
            OpenQuestions = openQuestions,
            UserPending = userActions.Count,
            UserActions = userActions,
            LastUserNoteNumber = lastUserNote,
            UserNotesChangedAt = userNotesChangedAt,
            PlanId = plan.Id,
            Status = plan.Status,
            StatusReason = plan.StatusReason,
            StatusChangedBy = plan.StatusChangedBy,
            Action = action,
            CancelledSteps = plan.Steps
                .Where(s => s.Status == ExecutionStatus.Cancelled)
                .OrderBy(s => s.Order)
                .Select(s => s.Key)
                .ToList()
        };
    }

    // ── 0024: ações do usuário ────────────────────────────────────────────────────────────────────

    public async Task<ExecutionStepResponse> StartStepAsync(Guid planId, string stepKey, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p => { step = p.StartStepByUser(stepKey, actor.Name, DateTimeOffset.UtcNow); }, cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionStepResponse> CompleteStepAsync(Guid planId, string stepKey, string? reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p =>
        {
            var why = string.IsNullOrWhiteSpace(reason) ? $"Concluída por {actor.Name}" : $"{reason.Trim()} — {actor.Name}";
            step = p.CompleteStepByUser(stepKey, why, actor.Name, DateTimeOffset.UtcNow);
        }, cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return step.ToResponse();
    }

    /// <summary>"Já resolvi" (0037): o usuário fez o que a etapa travada esperava; a skill tenta de novo.</summary>
    public async Task<ExecutionStepResponse> ResolveStepAsync(Guid planId, string stepKey, string? note, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p => { step = p.ResolveStepByUser(stepKey, note, actor.Name, DateTimeOffset.UtcNow); }, cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return step.ToResponse();
    }

    /// <summary>Planos ativos do usuário com alguma pendência dele (0037).</summary>
    public async Task<List<ExecutionUserPendingResponse>> GetUserPendingAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plans = await _repository.GetActivePlansByUserAsync(userId, cancellationToken);
        if (plans.Count == 0)
            return [];
        var questions = await _repository.GetOpenQuestionsAsync(plans.Select(p => p.Id).ToList(), cancellationToken);
        return plans
            .Select(p =>
            {
                var actions = p.UserActions(questions.Where(q => q.PlanId == p.Id));
                return new ExecutionUserPendingResponse
                {
                    PlanId = p.Id,
                    CardNumber = p.CardNumber,
                    Title = p.Title,
                    Phase = p.Phase,
                    Status = p.Status,
                    Count = actions.Count,
                    Actions = actions
                };
            })
            .Where(r => r.Count > 0)
            // Um card com análise e correção ativas aparece uma vez só (o plano mais recente vem primeiro).
            .GroupBy(r => r.CardNumber).Select(g => g.First())
            .ToList();
    }

    // ── 0024: perguntas ───────────────────────────────────────────────────────────────────────────

    public async Task<List<ExecutionQuestionResponse>> AskAsync(Guid planId, AskExecutionQuestionsRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (request.Questions.Count == 0)
            throw new DomainException("Informe ao menos uma pergunta.");
        if (request.Questions.Count > 20)
            throw new DomainException("No máximo 20 perguntas por vez.");

        List<ExecutionQuestion> created = [];
        var plan = await MutateAsync(planId, async p =>
        {
            EnsureAcceptsChanges(p);
            var now = DateTimeOffset.UtcNow;
            var existing = await _repository.GetQuestionsAsync(p.Id, cancellationToken);
            var order = existing.Count;
            created = request.Questions
                .Select(q => new ExecutionQuestion(p.Id, q.StepKey, ++order, q.Text, q.Options, q.AllowFreeText, actor.Name, now))
                .ToList();
            foreach (var key in created.Where(q => q.StepKey is not null).Select(q => q.StepKey!).Distinct())
            {
                if (!p.Steps.Any(s => s.Key == key))
                    throw new DomainException($"Etapa não encontrada: '{key}'.");
                var open = existing.Count(q => q.StepKey == key && q.Status == ExecutionQuestionStatus.Open) + created.Count(q => q.StepKey == key);
                p.SetStepWaiting(key, open == 1 ? "Aguardando a resposta do usuário" : $"Aguardando {open} respostas do usuário",
                    ExecutionWaitingOn.Answer, actor.Name, now, actor.IsExecutor);
            }
            _repository.AddQuestions(created);
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Question, created.FirstOrDefault()?.StepKey, cancellationToken);
        await WriteTimelineAsync(plan, QuestionsAskedText(plan, created), actor, cancellationToken);
        return created.Select(q => q.ToResponse()).ToList();
    }

    public async Task<ExecutionQuestionResponse> AnswerAsync(Guid planId, Guid questionId, string answer, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionQuestion? question = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            question = await _repository.GetQuestionAsync(p.Id, questionId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Pergunta não encontrada.");
            question.AnswerWith(answer, actor.Name, actor.IsExecutor, now);
            await ResumeIfNoOpenQuestionsAsync(p, question.StepKey, questionId, actor, now, cancellationToken);
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        // 0036: tempo real (relay externo) e Timeline em paralelo — independentes e sem o DbContext do plano; a
        // resposta à tela sai assim que os dois terminam (antes eram duas viagens ao relay em sequência).
        await Task.WhenAll(
            NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Question, question!.StepKey, cancellationToken),
            WriteTimelineAsync(plan, AnswerText(question), actor, cancellationToken));
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.Answers, cancellationToken);
        return question.ToResponse();
    }

    public async Task<ExecutionQuestionResponse> CancelQuestionAsync(Guid planId, Guid questionId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionQuestion? question = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            question = await _repository.GetQuestionAsync(p.Id, questionId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Pergunta não encontrada.");
            question.Cancel();
            await ResumeIfNoOpenQuestionsAsync(p, question.StepKey, questionId, actor, now, cancellationToken);
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Question, question!.StepKey, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return question.ToResponse();
    }

    /// <summary>Última pergunta da etapa respondida/cancelada: a etapa sai de "aguardando".</summary>
    private async Task ResumeIfNoOpenQuestionsAsync(ExecutionPlan plan, string? stepKey, Guid justChanged, ExecutionActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (stepKey is null) return;
        var questions = await _repository.GetQuestionsAsync(plan.Id, cancellationToken);
        var stillOpen = questions.Count(q => q.StepKey == stepKey && q.Id != justChanged && q.Status == ExecutionQuestionStatus.Open);
        if (stillOpen == 0)
            plan.ResumeStepFromWait(stepKey, actor.Name, now, actor.IsExecutor);
        else if (plan.Steps.Any(s => s.Key == stepKey && s.Status == ExecutionStatus.Waiting))
            plan.SetStepWaiting(stepKey, stillOpen == 1 ? "Aguardando a resposta do usuário" : $"Aguardando {stillOpen} respostas do usuário",
                ExecutionWaitingOn.Answer, actor.Name, now, actor.IsExecutor);
    }

    // ── 0024: links (chamados, PRs, documentos) ───────────────────────────────────────────────────

    public async Task<ExecutionLinkResponse> AddLinkAsync(Guid planId, string stepKey, AddExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionLink? link = null;
        var plan = await MutateAsync(planId, async p =>
        {
            EnsureAcceptsChanges(p);
            var now = DateTimeOffset.UtcNow;
            var key = ExecutionStep.NormalizeKey(stepKey);
            if (!p.Steps.Any(s => s.Key == key))
                throw new DomainException($"Etapa não encontrada: '{key}'.");

            // PR já anexado à etapa (ex.: a sincronização do PRMake chegou antes de quem abriu o PR): não duplica.
            if (request.PullRequestNumber is { } prNumber && !string.IsNullOrWhiteSpace(request.Repository))
            {
                var existing = (await _repository.GetLinksAsync(p.Id, cancellationToken)).FirstOrDefault(l =>
                    l.StepKey == key && l.Kind == ExecutionLinkKind.PullRequest && l.PullRequestNumber == prNumber
                    && SameRepository(l.Repository ?? "", request.Repository!));
                if (existing is not null)
                {
                    if (!string.IsNullOrWhiteSpace(request.Title)) existing.SetTitle(request.Title);
                    link = existing;
                    return;
                }
            }

            link = new ExecutionLink(p.Id, key, request.Url, request.Title, request.Kind, request.BlocksStep,
                request.PullRequestNumber, request.Repository, request.TargetBranch, actor.Name, now);
            _repository.AddLink(link);

            if (link.BlocksStep)
            {
                var blocking = (await _repository.GetLinksAsync(p.Id, cancellationToken))
                    .Where(l => l.StepKey == key && l.BlocksStep).Append(link).ToList();
                p.ApplyTicketState(key, blocking, actor.Name, now, actor.IsExecutor);
            }
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor, () => link!.Kind == ExecutionLinkKind.Ticket
            ? $"🎫 **Chamado anexado** à etapa *{link.StepKey}*: [{link.DisplayName}]({link.Url})"
            : null);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Link, link!.StepKey, cancellationToken);
        return link.ToResponse();
    }

    public async Task<ExecutionLinkResponse> UpdateLinkAsync(Guid planId, Guid linkId, UpdateExecutionLinkRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionLink? link = null;
        var statusChanged = false;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            link = await _repository.GetLinkAsync(p.Id, linkId, cancellationToken)
                   ?? throw new ExecutionPlanNotFoundException("Link não encontrado.");
            if (request.Title is not null)
                link.SetTitle(request.Title);
            statusChanged = !string.IsNullOrWhiteSpace(request.Status) && link.ChangeStatus(request.Status, actor.Name, now);
            if (statusChanged && link.BlocksStep)
            {
                var blocking = (await _repository.GetLinksAsync(p.Id, cancellationToken))
                    .Where(l => l.StepKey == link.StepKey && l.BlocksStep).ToList();
                p.ApplyTicketState(link.StepKey, blocking, actor.Name, now, actor.IsExecutor);
            }
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor, () => statusChanged && link!.Kind == ExecutionLinkKind.Ticket
            ? $"🎫 Chamado [{link.DisplayName}]({link.Url}) marcado como **{TicketStatusLabel(link.Status)}** por {actor.Name}"
            : null);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Link, link!.StepKey, cancellationToken);
        if (statusChanged)
            await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return link.ToResponse();
    }

    public async Task DeleteLinkAsync(Guid planId, Guid linkId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        string? key = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var link = await _repository.GetLinkAsync(p.Id, linkId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Link não encontrado.");
            key = link.StepKey;
            _repository.RemoveLink(link);
            if (link.BlocksStep)
            {
                var remaining = (await _repository.GetLinksAsync(p.Id, cancellationToken))
                    .Where(l => l.StepKey == key && l.BlocksStep && l.Id != linkId).ToList();
                // Sem chamado nenhum: a etapa deixa de esperar (volta a andar).
                if (remaining.Count == 0) p.ResumeStepFromWait(key, actor.Name, now, actor.IsExecutor);
                else p.ApplyTicketState(key, remaining, actor.Name, now, actor.IsExecutor);
            }
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Link, key, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
    }

    private static void EnsureAcceptsChanges(ExecutionPlan plan)
    {
        if (plan.Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado.");
    }

    public async Task<int> AppendLogsAsync(Guid planId, AppendExecutionLogsRequest request, CancellationToken cancellationToken)
    {
        if (request.Logs.Count == 0)
            return 0;
        if (request.Logs.Count > MaxLogsPerRequest)
            throw new DomainException($"Envie no máximo {MaxLogsPerRequest} registros por vez.");

        var appended = 0;
        string? lastStepKey = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var logs = request.Logs.Select(l => new ExecutionLog(p.Id, l.StepKey, l.Kind, l.Message, l.ClientId, now)).ToList();

            // Reenvio da fila local da skill: o mesmo clientId não entra duas vezes (nem no mesmo lote).
            var clientIds = logs.Where(l => l.ClientId is not null).Select(l => l.ClientId!).Distinct().ToList();
            var existing = clientIds.Count == 0
                ? []
                : await _repository.GetExistingClientIdsAsync(p.Id, clientIds, cancellationToken);
            var fresh = logs.Where(l => l.ClientId is null || existing.Add(l.ClientId)).ToList();

            foreach (var log in fresh.Where(l => l.StepKey is not null && l.Kind is ExecutionLogKind.Info or ExecutionLogKind.Progress))
                p.TrackActivity(log.StepKey!, log.Message, now);

            _repository.AddLogs(fresh);
            p.Touch(now, fromExecutor: true);
            appended = fresh.Count;
            lastStepKey = fresh.LastOrDefault(l => l.StepKey is not null)?.StepKey;
        }, cancellationToken);

        if (appended > 0)
            await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Log, lastStepKey, cancellationToken);
        return appended;
    }

    public async Task<List<ExecutionLogResponse>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken)
    {
        await LoadAsync(planId, cancellationToken);
        var key = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        var logs = await _repository.GetLogsAsync(planId, Math.Max(0, afterId), key, Math.Clamp(limit, 1, MaxLogsPerPage), cancellationToken);
        return logs.Select(l => l.ToResponse()).ToList();
    }

    public async Task<ExecutionArtifactResponse> UploadArtifactAsync(Guid planId, ExecutionArtifactUpload upload, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (upload.Data.LongLength == 0)
            throw new DomainException("O arquivo está vazio.");
        if (upload.Data.LongLength > ExecutionArtifact.MaxFileBytes)
            throw new DomainException($"O arquivo passa do limite de {ExecutionArtifact.MaxFileBytes / (1024 * 1024)} MB.");

        var name = ExecutionArtifact.NormalizeName(upload.FileName);
        var kind = ExecutionArtifact.NormalizeKind(upload.Kind, name);

        // 0032: a skill baixa os anexos dos comentários e podia reenviá-los como arquivos dela — o mesmo conteúdo
        // aparecia duas vezes (ou três, com o plano de correção). Conteúdo idêntico a um anexo do card = o próprio anexo.
        if (actor.IsExecutor)
        {
            var target = await LoadAsync(planId, cancellationToken);
            // 0050: script que altera dados e texto do chamado são da correção — na análise, só as consultas.
            if (ExecutionPhaseFiles.Reject(target.Phase, kind, name, upload.Data) is { } reason)
                throw new DomainException(reason);
            var sha = Convert.ToHexStringLower(SHA256.HashData(upload.Data));
            if (await _repository.FindNoteAttachmentByShaAsync(target.CardNumber, sha, cancellationToken) is { } attachment)
                return attachment.ToResponse();
        }

        ExecutionArtifact? saved = null;

        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var artifact = await _repository.FindArtifactAsync(p.Id, kind, name, cancellationToken);
            var isNew = artifact is null;

            var otherFiles = await _repository.GetArtifactsSizeAsync(p.Id, artifact?.Id, cancellationToken);
            if (otherFiles + upload.Data.LongLength > ExecutionArtifact.MaxPlanBytes)
                throw new DomainException($"Os arquivos deste plano passariam do limite de {ExecutionArtifact.MaxPlanBytes / (1024 * 1024)} MB.");

            artifact ??= new ExecutionArtifact(p.Id, name, kind, actor.Name, now,
                await _repository.GetMaxArtifactNumberAsync(p.CardNumber, cancellationToken) + 1);
            artifact.SetContent(upload.Data, upload.ContentType, upload.StepKey, upload.Description, now, isNew);
            if (isNew)
                _repository.AddArtifact(artifact);

            p.Touch(now, fromExecutor: actor.IsExecutor);
            saved = artifact;
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Artifact, saved!.StepKey, cancellationToken);
        return saved.ToResponse();
    }

    public async Task<ExecutionArtifactFile> GetArtifactFileAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await _repository.GetArtifactAsync(planId, artifactId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
        var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
        return new ExecutionArtifactFile(artifact.ToResponse(), data);
    }

    public async Task DeleteArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, async p =>
        {
            _ = await _repository.GetArtifactAsync(p.Id, artifactId, cancellationToken)
                ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
            await _repository.RemoveArtifactAsync(artifactId, cancellationToken);
            p.Touch(DateTimeOffset.UtcNow, fromExecutor: false);
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Artifact, null, cancellationToken);
    }

    public async Task<(string FileName, byte[] Data)> BuildZipAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var artifacts = await _repository.GetArtifactsAsync(planId, cancellationToken);
        if (artifacts.Count == 0)
            throw new DomainException("O plano ainda não tem arquivos.");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in artifacts.OrderBy(a => a.Kind).ThenBy(a => a.Name))
            {
                var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
                var entry = zip.CreateEntry($"{FolderOf(artifact.Kind)}/{artifact.Name}", CompressionLevel.Optimal);
                entry.LastWriteTime = artifact.UpdatedAt ?? artifact.CreatedAt;
                await using var stream = await entry.OpenAsync(cancellationToken);
                await stream.WriteAsync(data, cancellationToken);
            }
        }

        return ($"card-{plan.CardNumber}-{plan.Kind}-{plan.CreatedAt:yyyyMMdd-HHmm}.zip", buffer.ToArray());
    }

    private static string FolderOf(string kind) => kind switch
    {
        ExecutionArtifactKind.Script => "scripts",
        ExecutionArtifactKind.Analysis => "analises",
        ExecutionArtifactKind.Data => "dados",
        ExecutionArtifactKind.Image => "imagens",
        _ => "anexos"
    };

    private async Task<ExecutionPlan> LoadAsync(Guid planId, CancellationToken cancellationToken) =>
        await _repository.GetPlanWithStepsAsync(planId, cancellationToken)
        ?? throw new ExecutionPlanNotFoundException("Plano de execução não encontrado.");

    private Task<ExecutionPlan> MutateAsync(Guid planId, Action<ExecutionPlan> mutate, CancellationToken cancellationToken, ExecutionActor? actor = null) =>
        MutateAsync(planId, p => { mutate(p); return Task.CompletedTask; }, cancellationToken, actor);

    /// <param name="headline">Linha que abre o registro de marcos na Timeline (ex.: "Chamado anexado"), antes das mudanças que ela causou.</param>

    /// <summary>
    /// Carrega, altera e salva o plano. Se outra requisição gravou no meio (a skill mandando andamento
    /// enquanto o usuário pausa, por exemplo), relê e reaplica — ninguém perde a própria alteração.
    /// Depois de salvar, registra na Timeline os marcos que a alteração causou (0024).
    /// </summary>
    private async Task<ExecutionPlan> MutateAsync(Guid planId, Func<ExecutionPlan, Task> mutate, CancellationToken cancellationToken, ExecutionActor? actor = null,
        Func<string?>? headline = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var plan = await LoadAsync(planId, cancellationToken);
            var before = Snapshot(plan);
            await mutate(plan);
            plan.TryAutoComplete(actor?.Name ?? SystemActor.Name, DateTimeOffset.UtcNow);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
                await WriteMilestonesAsync(before, plan, actor ?? SystemActor, headline?.Invoke(), cancellationToken);
                return plan;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _repository.ClearTracking();
                // Espera curta e aleatória: rajadas simultâneas não voltam a colidir no mesmo instante.
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    private async Task<ExecutionPlanResponse> BuildResponseAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        var artifacts = await _repository.GetArtifactsAsync(plan.Id, cancellationToken);
        var lastLogId = await _repository.GetLastLogIdAsync(plan.Id, cancellationToken);
        var questions = await _repository.GetQuestionsAsync(plan.Id, cancellationToken);
        var links = await _repository.GetLinksAsync(plan.Id, cancellationToken);
        var notes = await GetNotesByCardAsync(plan.CardNumber, cancellationToken);
        return plan.ToResponse(artifacts, lastLogId, DateTimeOffset.UtcNow, questions, links, notes);
    }

    /// <summary>0049: sessão que o executor está rodando no card agora (best-effort).</summary>
    private async Task<ExecutionSession?> RunningSessionAsync(string cardNumber, CancellationToken cancellationToken)
    {
        if (_resumeTrigger is null) return null;
        try
        {
            return await _resumeTrigger.RunningSessionAsync(cardNumber, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 0039: uma ação de fora do Claude pode retomar o card no executor do dono (sem terminal). Best-effort: nunca
    /// quebra a operação da tela.
    /// </summary>
    private async Task<ExecutionRequest?> TriggerResumeAsync(ExecutionPlan plan, ExecutionActor actor, string source,
        CancellationToken cancellationToken, bool explicitRequest = false)
    {
        if (_resumeTrigger is null || actor.IsExecutor)
            return null;
        try
        {
            return await _resumeTrigger.PlanChangedAsync(plan, source, actor, explicitRequest, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _repository.ClearTracking();
            return null;
        }
    }

    /// <summary>
    /// Avisa quem está no card (sinal + refetch, como a timeline). Best-effort: nunca quebra a operação.
    /// </summary>
    private async Task NotifyAsync(ExecutionPlan plan, string action, string? stepKey, CancellationToken cancellationToken)
    {
        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(plan.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = plan.CardNumber, planId = plan.Id, action, stepKey, status = plan.Status },
                cancellationToken);
            // 0037: pendências do usuário fora do card (recentes, título da aba). Andamento não muda pendência.
            if (action != ExecutionPlanRealTimeEvents.Actions.Log)
                await _realTimeNotifier.NotifyGroupAsync(
                    ExecutionPlanRealTimeEvents.PendingGroup,
                    ExecutionPlanRealTimeEvents.EventPendingChanged,
                    new { cardNumber = plan.CardNumber, planId = plan.Id, userId = plan.CreatedByUserId },
                    cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort; a tela também consulta periodicamente enquanto o plano está ativo.
        }
    }
}
