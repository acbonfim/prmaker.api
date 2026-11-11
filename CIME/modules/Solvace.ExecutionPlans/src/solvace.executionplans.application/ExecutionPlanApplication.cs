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
    private readonly IExecutionCardDevOps? _devOps;
    private readonly IExecutionCardEvidenceSource? _evidence;
    /// <summary>0085: itens de trabalho (o plano de correção gera o item; as etapas sincronizam as sub-tarefas).</summary>
    private readonly IWorkItemApplication? _workItems;

    public ExecutionPlanApplication(IExecutionPlanRepository repository, IRealTimeNotifier realTimeNotifier,
        IExecutionPullRequestSource pullRequests, IExecutionTimelineWriter timeline, IExecutionCardRegistrar? cards = null,
        IExecutionResumeTrigger? resumeTrigger = null, IExecutionCardDevOps? devOps = null, IExecutionCardEvidenceSource? evidence = null,
        IWorkItemApplication? workItems = null)
    {
        _workItems = workItems;
        _devOps = devOps;
        _evidence = evidence;
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
        var parentPlanId = request.ParentPlanId;
        ExecutionPlanSummaryResponse? emptyAnalysis = null;
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
            (parentPlanId, emptyAnalysis) = await ResolveCorrectionParentAsync(parent, cancellationToken);
            _repository.ClearTracking();
        }
        // r5 (1.5): o plano de entrega da User Story é "Implementação <card>: ..." (nunca "correção").
        var title = request.Phase == ExecutionPhase.Correction
            ? PlanTerms.CorrectionTitle(request.Kind, request.CardNumber ?? string.Empty, request.Title)
            : request.Title;
        var plan = new ExecutionPlan(request.CardNumber!, request.Kind, title, request.Summary, actor.UserId, actor.Name, now,
            request.Phase, parentPlanId);
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
        // 0085: o plano vira um item de trabalho (uma sub-tarefa por etapa), salvo se a skill já criou itens dele — a correção
        // (a entrega) e, nos ajustes, a análise ("Análise inicial do bug/da US #card"; cada versão da análise = item novo).
        await CreateWorkItemFromPlanAsync(plan, actor, cancellationToken);
        // 0078: o assistente pergunta (sem travar) a estimativa inicial e a publicação dos resumos de espera.
        await SuggestOnCreateAsync(plan, cancellationToken);
        if (plan.Phase == ExecutionPhase.Correction)
            await WriteTimelineAsync(plan, CorrectionPlanCreatedText(plan), actor, cancellationToken);
        if (emptyAnalysis is not null && request.ParentPlanId is { } shellId)
            await ChangeStatusAsync(shellId, new ChangeExecutionPlanStatusRequest
            {
                Status = ExecutionStatus.Cancelled,
                Reason = $"Versão sem análise: a correção partiu da análise v{emptyAnalysis.VersionNumber}"
            }, actor, cancellationToken);
        if (plan.ParentPlanId is { } analysisId)
            await CloseAnalysisAsync(analysisId, plan.Kind, actor, cancellationToken);
        return plan.ToResponse([], 0, now);
    }

    /// <summary>
    /// Card 78262: com o plano de entrega criado a solução está decidida — a análise de origem fecha ("propor-solucoes"
    /// concluída e plano concluído). A skill deveria concluir antes de criar a correção, mas quando não conclui o item da
    /// análise ficava "em andamento" para sempre na Bancada. Best-effort: nunca quebra a criação do plano.
    /// </summary>
    private async Task CloseAnalysisAsync(Guid analysisId, string? kind, ExecutionActor actor, CancellationToken cancellationToken)
    {
        try
        {
            var reason = $"Solução decidida: {PlanTerms.DeliveryPlan(kind)} criado";
            var analysis = await MutateAsync(analysisId, p =>
            {
                if (p.Phase == ExecutionPhase.Analysis && !p.IsFinished)
                    p.CompleteStep(AnalysisDelivery.ProposeSolutionsStep, reason, actor.Name, DateTimeOffset.UtcNow, actor.IsExecutor);
            }, cancellationToken, actor);
            if (analysis.Phase != ExecutionPhase.Analysis || analysis.IsFinished)
                return;
            await ChangeStatusAsync(analysisId, new ChangeExecutionPlanStatusRequest { Status = ExecutionStatus.Completed, Reason = reason },
                actor, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _repository.ClearTracking();
        }
    }

    /// <summary>A análise já tem plano de entrega (não cancelado) nascido dela.</summary>
    private async Task<bool> HasDeliveryPlanAsync(ExecutionPlan analysis, CancellationToken cancellationToken) =>
        analysis.Phase == ExecutionPhase.Analysis
        && (await _repository.GetSummariesByCardAsync(analysis.CardNumber, cancellationToken))
            .Any(s => s.Phase == ExecutionPhase.Correction && s.ParentPlanId == analysis.Id && s.Status != ExecutionStatus.Cancelled);

    /// <summary>
    /// 0082: correção pedida sobre uma análise sem etapa concluída nem arquivo (a proposta aceita era de correção e a análise anterior
    /// estava cancelada — o início da skill abriu uma versão nova vazia; card 77900). A correção parte da última análise
    /// feita e a vazia é cancelada; o retorno traz essa análise feita (null = a análise pedida vale).
    /// </summary>
    private async Task<(Guid ParentId, ExecutionPlanSummaryResponse? Done)> ResolveCorrectionParentAsync(ExecutionPlan parent,
        CancellationToken cancellationToken)
    {
        if (parent.IsFinished || parent.Steps.Any(s => s.Status == ExecutionStatus.Completed)
            || (await _repository.GetArtifactsAsync(parent.Id, cancellationToken)).Count > 0)
            return (parent.Id, null);
        var done = (await _repository.GetSummariesByCardAsync(parent.CardNumber, cancellationToken))
            .Where(s => s.Phase == ExecutionPhase.Analysis && s.Id != parent.Id && s.StepsCompleted > 0 && s.CreatedAt < parent.CreatedAt)
            .MaxBy(s => s.CreatedAt);
        return done is null ? (parent.Id, null) : (done.Id, done);
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
        await EnsureDevTestInQaAsync(planId, cancellationToken);
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

    public async Task<ExecutionStepResponse> AddStepAsync(Guid planId, ExecutionStepDefinition definition, string? beforeKey, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p => step = p.AddStep(definition, beforeKey, DateTimeOffset.UtcNow), cancellationToken, actor);
        // Steps: o item espelho ganha a sub-tarefa na sincronização (SyncWorkItemsAsync).
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Steps, step!.Key, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionStepResponse> UpdateStepAsync(Guid planId, string stepKey, UpdateExecutionStepRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        // 0050: o Claude não deixa uma etapa de chamado nas mãos do usuário sem o script e o texto do chamado anexados.
        var artifacts = actor.IsExecutor ? await _repository.GetArtifactsAsync(planId, cancellationToken) : null;
        var plan = await MutateAsync(planId, p =>
        {
            var missingBefore = artifacts is null ? [] : TicketStepsMissingFiles(p, artifacts).ToHashSet();
            step = p.UpdateStep(stepKey, request.Status, request.Reason, request.Activity, request.Checkpoint,
                request.Title, request.Description, actor.Name, DateTimeOffset.UtcNow, request.WaitingOn);
            if (artifacts is not null && TicketStepsMissingFiles(p, artifacts).FirstOrDefault(k => !missingBefore.Contains(k)) is { } missing)
                throw new DomainException(
                    $"A etapa de chamado '{missing}' ficaria com o usuário sem os arquivos: anexe antes o script (.sql com rollback) e o texto do chamado " +
                    $"com prmake_file(card, \"01_<card>_<nome>.sql\", conteudo, \"script\", \"{missing}\", phase: \"correction\") e " +
                    $"prmake_file(card, \"chamado-<card>-<nome>.md\", texto, \"ticket\", \"{missing}\", phase: \"correction\").");
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
        List<ExecutionQuestion> cancelled = [];
        var plan = await MutateAsync(planId, async p =>
        {
            p.ChangeStatus(request.Status, request.Reason, request.Summary, actor.Name, actor.IsExecutor, DateTimeOffset.UtcNow);
            // Plano cancelado/concluído: as perguntas abertas não decidem mais nada — saem da espera do usuário (card 77900).
            cancelled = [];
            if (p.IsFinished)
            {
                var reason = p.Status == ExecutionStatus.Cancelled ? "Plano cancelado" : "Plano concluído";
                cancelled = (await _repository.GetQuestionsAsync(p.Id, cancellationToken))
                    .Where(q => q.Status == ExecutionQuestionStatus.Open).ToList();
                foreach (var q in cancelled)
                    q.Cancel(reason);
            }
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        if (cancelled.Count > 0)
            await WriteTimelineAsync(plan, QuestionsCancelledText(cancelled, actor), actor, cancellationToken);
        if (plan.Status == ExecutionStatus.Running)
            await TriggerResumeAsync(plan, actor, ExecutionRequestSource.Resume, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionControlResponse> ControlAsync(Guid planId, CancellationToken cancellationToken)
    {
        await SyncPullRequestsAsync(planId, cancellationToken);
        await EnsureDevTestInQaAsync(planId, cancellationToken);
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
            TicketStepsMissingFiles = TicketStepsMissingFiles(plan, await _repository.GetArtifactsAsync(plan.Id, cancellationToken)),
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
        // Card 78262: análise que já virou plano de entrega não tem mais o que esperar — "Já resolvi" fecha a análise.
        var current = await LoadAsync(planId, cancellationToken);
        if (!current.IsFinished && await HasDeliveryPlanAsync(current, cancellationToken))
        {
            await CloseAnalysisAsync(planId, current.Kind, actor, cancellationToken);
            var closed = await LoadAsync(planId, cancellationToken);
            return (closed.Steps.FirstOrDefault(s => s.Key == ExecutionStep.NormalizeKey(stepKey))
                    ?? throw new DomainException("Etapa não encontrada.")).ToResponse();
        }
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
        List<ExecutionQuestion> replaced = [];
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
                if (!p.Steps.Any(s => s.Key == key))
                    throw new DomainException($"Etapa não encontrada: '{key}'.");

            // Nova rodada (comentário/anexo novo): a pergunta revisada substitui as anteriores — as abertas são
            // canceladas para o usuário não responder o que já não vale (card 75648).
            var byOrder = existing.GroupBy(q => q.Order).ToDictionary(g => g.Key, g => g.Last());
            for (var i = 0; i < created.Count; i++)
                foreach (var n in (request.Questions[i].Replaces ?? []).Distinct())
                {
                    if (!byOrder.TryGetValue(n, out var old))
                        throw new DomainException($"Pergunta {n} não existe (a substituída precisa ser uma pergunta anterior).");
                    old.ReplaceWith(created[i].Order);
                    if (!replaced.Contains(old)) replaced.Add(old);
                }

            var all = existing.Concat(created).ToList();
            foreach (var key in created.Concat(replaced).Where(q => q.StepKey is not null).Select(q => q.StepKey!).Distinct())
                RefreshStepWait(p, key, all, actor, now);
            _repository.AddQuestions(created);
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Question, created.FirstOrDefault()?.StepKey, cancellationToken);
        await WriteTimelineAsync(plan, QuestionsAskedText(plan, created, replaced), actor, cancellationToken);
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

    public async Task<ExecutionQuestionResponse> CancelQuestionAsync(Guid planId, Guid questionId, string? reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var cancelled = await CancelQuestionsAsync(planId, new CancelExecutionQuestionsRequest { Ids = [questionId], Reason = reason }, actor, cancellationToken);
        return cancelled.Single();
    }

    public async Task<List<ExecutionQuestionResponse>> CancelQuestionsAsync(Guid planId, CancelExecutionQuestionsRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var numbers = request.Numbers ?? [];
        var ids = request.Ids ?? [];
        if (numbers.Count == 0 && ids.Count == 0)
            throw new DomainException("Informe as perguntas a cancelar.");

        List<ExecutionQuestion> targets = [];
        List<ExecutionQuestion> cancelled = [];
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var questions = await _repository.GetQuestionsAsync(p.Id, cancellationToken);
            foreach (var n in numbers.Distinct())
                targets.Add(questions.FirstOrDefault(q => q.Order == n) ?? throw new ExecutionPlanNotFoundException($"Pergunta {n} não encontrada."));
            foreach (var id in ids.Distinct())
                targets.Add(questions.FirstOrDefault(q => q.Id == id) ?? throw new ExecutionPlanNotFoundException("Pergunta não encontrada."));
            targets = targets.Distinct().ToList();

            cancelled = targets.Where(q => q.Status == ExecutionQuestionStatus.Open).ToList();
            foreach (var q in cancelled)
                q.Cancel(request.Reason);
            foreach (var key in cancelled.Where(q => q.StepKey is not null).Select(q => q.StepKey!).Distinct())
                RefreshStepWait(p, key, questions, actor, now);
            p.Touch(now, actor.IsExecutor);
        }, cancellationToken, actor);

        if (cancelled.Count > 0)
        {
            await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Question, cancelled[0].StepKey, cancellationToken);
            await WriteTimelineAsync(plan, QuestionsCancelledText(cancelled, actor), actor, cancellationToken);
            await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        }
        return targets.Select(q => q.ToResponse()).ToList();
    }

    /// <summary>Etapa espera enquanto houver pergunta aberta dela; a última respondida/cancelada a tira de "aguardando".</summary>
    private static void RefreshStepWait(ExecutionPlan plan, string stepKey, IEnumerable<ExecutionQuestion> questions, ExecutionActor actor, DateTimeOffset now)
    {
        var open = questions.Count(q => q.StepKey == stepKey && q.Status == ExecutionQuestionStatus.Open);
        if (open == 0)
            plan.ResumeStepFromWait(stepKey, actor.Name, now, actor.IsExecutor);
        else
            plan.SetStepWaiting(stepKey, open == 1 ? "Aguardando a resposta do usuário" : $"Aguardando {open} respostas do usuário",
                ExecutionWaitingOn.Answer, actor.Name, now, actor.IsExecutor);
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
            else if (link.Kind == ExecutionLinkKind.PullRequest)
            {
                var prs = (await _repository.GetLinksAsync(p.Id, cancellationToken))
                    .Where(l => l.StepKey == key && l.Kind == ExecutionLinkKind.PullRequest && l.Id != link.Id).Append(link).ToList();
                p.WaitForPullRequests(key, prs, actor.Name, now, actor.IsExecutor, includeRunning: true);
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
            // 0080: divergência entre a análise e a base real fica pendente no plano (alerta + portão do PR).
            foreach (var log in fresh.Where(l => l.Kind == ExecutionLogKind.Divergence))
                p.RaiseDivergence(log.Message, log.StepKey, now);

            _repository.AddLogs(fresh);
            p.Touch(now, fromExecutor: true);
            appended = fresh.Count;
            lastStepKey = fresh.LastOrDefault(l => l.StepKey is not null)?.StepKey;
        }, cancellationToken);

        if (appended > 0)
            await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Log, lastStepKey, cancellationToken);
        return appended;
    }

    /// <summary>0080: a pessoa confirmou a divergência — a etapa de PR pode começar e o Claude segue.</summary>
    public async Task<ExecutionPlanResponse> ConfirmDivergenceAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p =>
        {
            var now = DateTimeOffset.UtcNow;
            p.ConfirmDivergence(actor.Name, now);
            p.Touch(now, fromExecutor: false);
            return Task.CompletedTask;
        }, cancellationToken, actor, () => "⚠️ **Divergência confirmada** por " + actor.Name);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, plan.DivergenceStepKey, cancellationToken);
        await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return await GetAsync(planId, cancellationToken);
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

    public async Task<ExecutionArtifactFile> GetArtifactFileAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken, bool markRead = false)
    {
        var artifact = await _repository.GetArtifactAsync(planId, artifactId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
        // 0076: a skill abriu a evidência (a análise só conclui depois de abrir todas).
        if (markRead && artifact.ExternalKey is not null && artifact.ReadAt is null)
            await _repository.MarkArtifactReadAsync(artifact.Id, DateTimeOffset.UtcNow, cancellationToken);
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

    /// <summary>
    /// 0050: etapas de chamado já com o usuário (prontas ou aguardando) sem o script e o texto do chamado
    /// (<c>ticket</c>) anexados com a <c>key</c> delas.
    /// </summary>
    private static List<string> TicketStepsMissingFiles(ExecutionPlan plan, IEnumerable<ExecutionArtifact> artifacts)
    {
        var files = artifacts.Where(a => a.NoteId is null && a.StepKey is not null).ToLookup(a => a.StepKey!);
        return plan.StepsPendingForUser()
            .Where(s => s.Kind == ExecutionStepKind.Ticket)
            .Where(s => !(files[s.Key].Any(a => a.Kind == ExecutionArtifactKind.Ticket) && files[s.Key].Any(a => a.Kind == ExecutionArtifactKind.Script)))
            .Select(s => s.Key)
            .ToList();
    }

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
                // 0078: etapa concluída pode render uma sugestão de lançar horas.
                await SuggestAfterStepsAsync(before, plan, cancellationToken);
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

    /// <summary>0085: item de trabalho do plano (análise ou correção; idempotente). Best-effort: nunca quebra a criação do plano.</summary>
    private async Task CreateWorkItemFromPlanAsync(ExecutionPlan plan, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (_workItems is null) return;
        try
        {
            await _workItems.FromPlanAsync(plan.CardNumber, plan.Id, actor, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O item pode ser gerado depois pela tela (POST WorkItems/card/{c}/from-plan/{planId}).
        }
    }

    /// <summary>0085: etapas/status do plano → sub-tarefas/status dos itens ligados a ele. Best-effort.</summary>
    private async Task SyncWorkItemsAsync(ExecutionPlan plan, string action, CancellationToken cancellationToken)
    {
        if (_workItems is null || action is not (ExecutionPlanRealTimeEvents.Actions.Steps or ExecutionPlanRealTimeEvents.Actions.Step
                or ExecutionPlanRealTimeEvents.Actions.Status or ExecutionPlanRealTimeEvents.Actions.Link or ExecutionPlanRealTimeEvents.Actions.Question))
            return;
        try
        {
            await _workItems.SyncFromPlanAsync(plan.Id, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O item volta a espelhar o plano na próxima mudança de etapa.
        }
    }

    /// <summary>
    /// Avisa quem está no card (sinal + refetch, como a timeline). Best-effort: nunca quebra a operação.
    /// </summary>
    private async Task NotifyAsync(ExecutionPlan plan, string action, string? stepKey, CancellationToken cancellationToken)
    {
        await SyncWorkItemsAsync(plan, action, cancellationToken);
        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(plan.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = plan.CardNumber, planId = plan.Id, action, stepKey, status = plan.Status },
                cancellationToken);
            // 0075: a situação do card (o que está sendo aguardado) pode ter mudado.
            // Só as ações que mudam o que é aguardado (andamento, arquivos, comentários e fotografias não mudam).
            if (action is ExecutionPlanRealTimeEvents.Actions.Created or ExecutionPlanRealTimeEvents.Actions.Steps or ExecutionPlanRealTimeEvents.Actions.Step
                or ExecutionPlanRealTimeEvents.Actions.Status or ExecutionPlanRealTimeEvents.Actions.Question or ExecutionPlanRealTimeEvents.Actions.Link
                or ExecutionPlanRealTimeEvents.Actions.Situation)
                await TrackSituationAsync(plan, cancellationToken);
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
