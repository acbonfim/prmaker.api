using System.Collections.Concurrent;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// Fila de execução (0039): a tela (ou uma resposta, ou a regra automática) pede, o executor da máquina do dono pega
/// com trava e roda o Claude Code sem terminal. Manutenção preguiçosa — tudo o que "vence" (trava, fila de 24 h, regra
/// automática) é avaliado quando o executor do dono pergunta ou alguém olha o card: no Cloud Run só há CPU durante o
/// request, então nada depende de serviço em segundo plano.
/// </summary>
public class ExecutionQueueApplication : IExecutionQueueApplication, IExecutionResumeTrigger
{
    /// <summary>Sinal da skill mais novo que isso (e depois do último pedido) = tem uma sessão viva no card.</summary>
    public static readonly TimeSpan AliveWindow = TimeSpan.FromSeconds(150);

    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RuleInterval = TimeSpan.FromMinutes(5);
    /// <summary>Card que já teve pedido nesse período não é pedido de novo pela regra automática.</summary>
    private static readonly TimeSpan RuleCooldown = TimeSpan.FromDays(30);

    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> LastMaintenance = new();
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource> Signals = new();

    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private readonly IExecutionQueueRepository _queue;
    private readonly IExecutionPlanRepository _plans;
    private readonly IRealTimeNotifier _realTime;
    private readonly IExecutionWorkItemSource? _workItems;
    private readonly IExecutionAgentInfo? _agentInfo;
    private readonly IExecutionQueueSettings? _settings;
    private ExecutionQueueOptions? _options;

    public ExecutionQueueApplication(IExecutionQueueRepository queue, IExecutionPlanRepository plans, IRealTimeNotifier realTime,
        IExecutionWorkItemSource? workItems = null, IExecutionAgentInfo? agentInfo = null, IExecutionQueueSettings? settings = null)
    {
        _queue = queue;
        _plans = plans;
        _realTime = realTime;
        _workItems = workItems;
        _agentInfo = agentInfo;
        _settings = settings;
    }

    /// <summary>0049: configurações da fila (uma leitura por request; falha → padrão).</summary>
    private async Task<ExecutionQueueOptions> OptionsAsync(CancellationToken cancellationToken)
    {
        if (_options is not null) return _options;
        try
        {
            _options = _settings is null ? ExecutionQueueOptions.Default : await _settings.GetAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _options = ExecutionQueueOptions.Default;
        }
        return _options;
    }

    // ── Tela ─────────────────────────────────────────────────────────────────────────────────────

    public async Task<ExecutionRequestResponse> CreateAsync(CreateExecutionRequestRequest request, ExecutionActor actor, string source, CancellationToken cancellationToken)
    {
        var owner = actor.UserId ?? throw new DomainException("Seu usuário não tem identificação no PRMake — entre de novo.");
        var card = (request.CardNumber ?? string.Empty).Trim();
        if (card.Length == 0)
            throw new DomainException("Informe o número do card.");
        var now = DateTimeOffset.UtcNow;

        if (await _queue.GetActiveRequestForCardAsync(card, cancellationToken) is { } existing)
        {
            var changed = false;
            if (request.Force && existing.Status == ExecutionRequestStatus.Queued && existing.OwnerUserId == owner && !existing.Force)
            {
                existing.AllowOverBudget(now);
                changed = true;
            }
            // 0049: "Continuar com Claude" com o pedido esperando mais comentários → começa já.
            if (existing.OwnerUserId == owner && existing.Source == ExecutionRequestSource.Note && existing.RunNow(now))
                changed = true;
            if (changed)
            {
                await _queue.SaveChangesAsync(cancellationToken);
                await NotifyAsync(existing, cancellationToken);
                Pulse(owner);
            }
            return await RespondAsync(existing, cancellationToken);
        }

        if (request.TargetWorkerId is { } target)
        {
            var worker = await _queue.GetWorkerAsync(target, cancellationToken) ?? throw new DomainException("Máquina não encontrada.");
            if (worker.OwnerUserId != owner)
                throw new ExecutionForbiddenException("Essa máquina é de outra pessoa.");
            if (worker.IsRevoked)
                throw new DomainException("Essa máquina foi revogada — escolha outra.");
        }

        ExecutionPlan? plan = null;
        if (await _plans.GetCurrentPlanIdAsync(card, cancellationToken) is { } planId)
            plan = await _plans.GetPlanWithStepsAsync(planId, cancellationToken);
        var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind.Length == 0)
            kind = plan is { IsFinished: false } ? ExecutionRequestKind.Resume : ExecutionRequestKind.Analyze;
        var session = kind == ExecutionRequestKind.Resume ? plan?.CurrentSession : null;

        var created = new ExecutionRequest(card, kind, source, owner, actor.Name, actor.UserId, actor.Name,
            plan is { IsFinished: false } ? plan.Id : null, session, request.TargetWorkerId, request.Note, request.Force, now);
        var saved = await AddRequestAsync(created, cancellationToken) ?? created;
        await UpdateWaitReasonsAsync(owner, now, cancellationToken);
        return await RespondAsync(saved, cancellationToken);
    }

    public async Task<ExecutionCardQueueResponse> GetCardAsync(string cardNumber, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var card = cardNumber.Trim();
        var now = DateTimeOffset.UtcNow;
        var active = await _queue.GetActiveRequestForCardAsync(card, cancellationToken);
        if (active is not null && await MaintainAsync(active.OwnerUserId, now, cancellationToken, force: false))
            active = await _queue.GetActiveRequestForCardAsync(card, cancellationToken);

        var response = new ExecutionCardQueueResponse
        {
            Active = active is null ? null : await RespondAsync(active, cancellationToken),
            Recent = (await _queue.GetRecentRequestsForCardAsync(card, 6, cancellationToken))
                .Where(r => r.Id != active?.Id).Take(5).Select(r => r.ToResponse()).ToList()
        };
        if (viewerUserId is { } viewer)
            response.MyWorkers = await GetWorkersAsync(viewer, cancellationToken);
        if (active is not null && active.OwnerUserId != viewerUserId)
            response.OwnerWorkers = await GetWorkersAsync(active.OwnerUserId, cancellationToken);
        return response;
    }

    public async Task<ExecutionRequestResponse> CancelAsync(Guid requestId, string? reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var request = await RequireRequestAsync(requestId, cancellationToken);
        request.Cancel(actor.Name, string.IsNullOrWhiteSpace(reason) ? null : $"{reason.Trim()} — {actor.Name}", DateTimeOffset.UtcNow);
        await _queue.SaveChangesAsync(cancellationToken);
        await NotifyAsync(request, cancellationToken);
        Pulse(request.OwnerUserId);
        return request.ToResponse();
    }

    public async Task<ExecutionRequestResponse> RetryAsync(Guid requestId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var previous = await RequireRequestAsync(requestId, cancellationToken);
        if (previous.IsActive)
            return await RespondAsync(previous, cancellationToken);
        return await CreateAsync(new CreateExecutionRequestRequest
        {
            CardNumber = previous.CardNumber,
            Kind = previous.Kind,
            TargetWorkerId = previous.TargetWorkerId,
            Note = previous.Note,
            Force = previous.Force
        }, actor, ExecutionRequestSource.Button, cancellationToken);
    }

    public async Task<ExecutionRequestResponse> AllowOverBudgetAsync(Guid requestId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var request = await RequireRequestAsync(requestId, cancellationToken);
        if (request.OwnerUserId != actor.UserId)
            throw new ExecutionForbiddenException("Só o dono do pedido libera o orçamento.");
        request.AllowOverBudget(DateTimeOffset.UtcNow);
        await _queue.SaveChangesAsync(cancellationToken);
        await NotifyAsync(request, cancellationToken);
        Pulse(request.OwnerUserId);
        return request.ToResponse();
    }

    public async Task<List<ExecutionRequestResponse>> GetHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await _queue.GetHistoryForOwnerAsync(ownerUserId, 50, cancellationToken)).Select(r => r.ToResponse()).ToList();

    // ── Executor ─────────────────────────────────────────────────────────────────────────────────

    public async Task<ExecutionClaimResponse?> NextAsync(Guid workerId, TimeSpan wait, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var worker = await MutateWorkerAsync(workerId, (w, at) => w.Seen(at), cancellationToken);
        var owner = worker.OwnerUserId;

        await MaintainAsync(owner, now, cancellationToken, force: true);
        await EvaluateRuleAsync(owner, now, cancellationToken);

        var deadline = now + (wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxWait ? MaxWait : wait);
        while (true)
        {
            _queue.ClearTracking();
            now = DateTimeOffset.UtcNow;
            worker = await RequireWorkerAsync(workerId, cancellationToken);
            if (await TryClaimAsync(worker, now, cancellationToken) is { } claimed)
                return claimed;
            if (now >= deadline || cancellationToken.IsCancellationRequested)
                return null;
            await MaintainAsync(owner, now, cancellationToken, force: false);
            var remaining = deadline - now;
            await WaitSignalAsync(owner, remaining < PollInterval ? remaining : PollInterval, cancellationToken);
        }
    }

    public async Task<ExecutionRequestResponse> StartAsync(Guid requestId, Guid workerId, StartExecutionRequestRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var r = await MutateRequestAsync(requestId, x => x.Start(workerId, request.Pid, request.SessionId, now), cancellationToken);
        await TouchWorkerAsync(workerId, now, cancellationToken);
        await NotifyAsync(r, cancellationToken);
        return r.ToResponse();
    }

    public async Task<ExecutionHeartbeatResponse> HeartbeatAsync(Guid requestId, Guid workerId, ExecutionRequestHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var r = await RequireRequestAsync(requestId, cancellationToken);
        if (r.WorkerId != workerId)
            return new ExecutionHeartbeatResponse { Action = "cancel", RequestStatus = r.Status, Reason = "O pedido passou para outro executor." };
        if (!r.IsActive)
            return new ExecutionHeartbeatResponse { Action = "cancel", RequestStatus = r.Status, Reason = r.FinishedReason };
        if (r.Status == ExecutionRequestStatus.Queued)
            return new ExecutionHeartbeatResponse { Action = "cancel", RequestStatus = r.Status, Reason = "A trava venceu e o pedido voltou para a fila." };

        var activityChanged = false;
        r = await MutateRequestAsync(requestId, x =>
        {
            x.Heartbeat(workerId, request.Pid, request.StderrTail, now);
            activityChanged = x.RecordActivity(workerId, ToActivity(request.Activity, now), request.Recent?.Select(a => ToActivity(a, now)).OfType<ExecutionActivity>());
        }, cancellationToken);
        await TouchWorkerAsync(workerId, now, cancellationToken);
        if (activityChanged) await NotifyActivityAsync(r, cancellationToken);

        var plan = await FindPlanAsync(r, cancellationToken);
        if (plan is not null && plan.Status == ExecutionStatus.Cancelled && plan.UpdatedAt >= (r.StartedAt ?? r.CreatedAt))
        {
            r = await MutateRequestAsync(requestId, x => x.Cancel(plan.StatusChangedBy ?? "PRMake", "Plano cancelado pela tela", now), cancellationToken);
            await NotifyAsync(r, cancellationToken);
            return new ExecutionHeartbeatResponse { Action = "cancel", RequestStatus = r.Status, PlanStatus = plan.Status, Reason = "Plano cancelado pela tela." };
        }
        return new ExecutionHeartbeatResponse { Action = "continue", RequestStatus = r.Status, PlanStatus = plan?.Status, Reason = plan?.StatusReason };
    }

    public async Task<ExecutionRequestResponse> FinishAsync(Guid requestId, Guid workerId, FinishExecutionRequestRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? throttledUntil = null;
        var outcome = (request.Outcome ?? string.Empty).Trim().ToLowerInvariant();
        if (outcome is not ("done" or "failed"))
            throw new DomainException("outcome deve ser done ou failed.");

        // 0044: o Claude Code informa o custo ACUMULADO da sessão retomada — a base é o acumulado do pedido anterior dela.
        var current = await RequireRequestAsync(requestId, cancellationToken);
        var sessionId = current.SessionId ?? request.SessionId;
        var previousCost = request.CostUsd is not null && !string.IsNullOrEmpty(sessionId)
            ? await _queue.GetLastSessionCostAsync(sessionId, requestId, cancellationToken)
            : null;
        _queue.ClearTracking();

        var r = await MutateRequestAsync(requestId, x =>
        {
            if (!x.IsActive || x.WorkerId != workerId) return;
            x.RecordUsage(request.CostUsd, previousCost, request.InputTokens, request.OutputTokens, request.Turns,
                request.FreshInputTokens, request.CacheReadTokens, request.CacheWriteTokens, request.Model);
            if (outcome == "failed" && request.RetryAt is { } retryAt)
            {
                // 0041: limite da conta do Claude — espera o reset (no máximo 24 h) sem gastar tentativa.
                var until = retryAt < now ? now.AddMinutes(5) : retryAt > now.AddHours(24) ? now.AddHours(24) : retryAt;
                x.Throttle(workerId, until, $"Limite de uso da conta do Claude atingido — continua às {LocalTime(until)}", request.StderrTail, now);
                throttledUntil = until;
            }
            else if (outcome == "done")
                x.Complete(workerId, request.ExitCode, request.Reason, now);
            else
                x.Fail(workerId, request.Error ?? request.Reason ?? "O processo do Claude terminou com erro", request.ExitCode, request.StderrTail, request.Retryable, now);
        }, cancellationToken);
        if (throttledUntil is { } t)
            await MutateWorkerAsync(workerId, (w, at) => { w.Seen(at); w.Throttle(t, at); }, cancellationToken);
        else
            await TouchWorkerAsync(workerId, now, cancellationToken);
        await NotifyAsync(r, cancellationToken);
        if (r.Status == ExecutionRequestStatus.Queued) Pulse(r.OwnerUserId);
        return r.ToResponse();
    }

    public async Task<Guid?> ResolvePlanIdAsync(Guid requestId, string sessionId, CancellationToken cancellationToken)
    {
        var r = await RequireRequestAsync(requestId, cancellationToken);
        if (await _plans.GetCurrentPlanIdAsync(r.CardNumber, cancellationToken) is { } currentId && currentId != r.PlanId
            && (await _plans.GetSessionsAsync([currentId], cancellationToken)).TryGetValue(currentId, out var sessions)
            && sessions.Any(s => s.SessionId == sessionId))
            return currentId;
        return (await FindPlanAsync(r, cancellationToken))?.Id;
    }

    // ── Executores ───────────────────────────────────────────────────────────────────────────────

    public async Task<(ExecutionWorker Worker, ExecutionWorkerResponse Response)> RegisterWorkerAsync(RegisterExecutionWorkerRequest request,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        var owner = actor.UserId ?? throw new DomainException("Seu usuário não tem identificação no PRMake — gere a api-key de novo.");
        var host = request.Host?.Trim();
        if (string.IsNullOrEmpty(host))
            throw new DomainException("Informe o nome da máquina.");
        var now = DateTimeOffset.UtcNow;

        var worker = await _queue.FindWorkerByHostAsync(owner, host, cancellationToken);
        if (worker is null)
        {
            worker = new ExecutionWorker(owner, actor.Name, request.Name, host, request.Os, now);
            _queue.AddWorker(worker);
        }
        else
        {
            worker.Reactivate(request.Os, now);
            if (!string.IsNullOrWhiteSpace(request.Name)) worker.Configure(request.Name, null, now);
        }
        worker.Report(request.AgentVersion, null, null, null, null, now);
        await _queue.SaveChangesAsync(cancellationToken);
        await NotifyWorkersAsync(owner, cancellationToken);
        return (worker, worker.ToResponse(now, 0, _agentInfo?.LatestVersion));
    }

    public async Task<ExecutionWorkerStateResponse> ReportWorkerAsync(Guid workerId, ExecutionWorkerReportRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var wasOnline = false;
        var capabilities = request.Capabilities is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } c ? c.GetRawText() : null;
        var worker = await MutateWorkerAsync(workerId, (w, at) =>
        {
            wasOnline = w.IsOnline(at);
            w.Report(request.AgentVersion, request.ClaudeVersion, request.SkillsVersion, request.Workspace, capabilities, at);
        }, cancellationToken);
        if (!wasOnline) await NotifyWorkersAsync(worker.OwnerUserId, cancellationToken);

        var active = await _queue.GetActiveForWorkerAsync(workerId, cancellationToken);
        return new ExecutionWorkerStateResponse
        {
            Status = worker.Status,
            MaxConcurrency = worker.MaxConcurrency,
            DoctorRequested = worker.DoctorPending,
            LatestAgentVersion = _agentInfo?.LatestVersion,
            ActiveRequestIds = active.Select(r => r.Id).ToList()
        };
    }

    public async Task<ExecutionWorkerResponse> ReportDoctorAsync(Guid workerId, ExecutionWorkerDoctorRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var checks = request.Checks.Take(50).Select(c => new ExecutionDoctorCheck
        {
            Name = Trim(c.Name, 100) ?? "?",
            Ok = c.Ok,
            Message = Trim(c.Message, 500),
            Severity = c.Severity is "warning" ? "warning" : "error"
        }).ToList();
        var json = JsonSerializer.Serialize(checks, CamelCase);
        var worker = await MutateWorkerAsync(workerId, (w, at) => w.SetDoctor(json, at), cancellationToken);
        await NotifyWorkersAsync(worker.OwnerUserId, cancellationToken);
        return await WorkerResponseAsync(worker, now, cancellationToken);
    }

    public async Task<ExecutionWorkerResponse> GetWorkerAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var worker = await RequireWorkerAsync(workerId, cancellationToken);
        return await WorkerResponseAsync(worker, DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<List<ExecutionWorkerResponse>> GetWorkersAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var workers = await _queue.GetWorkersByOwnerAsync(ownerUserId, cancellationToken);
        var counts = await _queue.CountActiveByWorkerAsync(ownerUserId, cancellationToken);
        return workers.Where(w => !w.IsRevoked)
            .Select(w => w.ToResponse(now, counts.GetValueOrDefault(w.Id), _agentInfo?.LatestVersion))
            .ToList();
    }

    public Task<ExecutionWorkerResponse> ConfigureWorkerAsync(Guid workerId, ConfigureExecutionWorkerRequest request, ExecutionActor actor, CancellationToken cancellationToken) =>
        MutateOwnWorkerAsync(workerId, actor, (w, now) => w.Configure(request.Name, request.MaxConcurrency, now), cancellationToken);

    /// <summary>"Rodar diagnóstico agora": o executor vê no próximo sinal de vida (até 1 min) e roda o doctor.</summary>
    public Task<ExecutionWorkerResponse> RequestDoctorAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken) =>
        MutateOwnWorkerAsync(workerId, actor, (w, now) => w.RequestDoctor(now), cancellationToken);

    public Task<ExecutionWorkerResponse> PauseWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken) =>
        MutateOwnWorkerAsync(workerId, actor, (w, now) => w.Pause(now), cancellationToken);

    public Task<ExecutionWorkerResponse> ResumeWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken) =>
        MutateOwnWorkerAsync(workerId, actor, (w, now) => w.Resume(now), cancellationToken);

    public async Task<ExecutionWorkerResponse> RevokeWorkerAsync(Guid workerId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var response = await MutateOwnWorkerAsync(workerId, actor, (w, now) => w.Revoke(actor.Name, now), cancellationToken);
        // O que estava com ele volta para a fila (outra máquina do dono pode pegar).
        var now = DateTimeOffset.UtcNow;
        foreach (var r in await _queue.GetActiveForWorkerAsync(workerId, cancellationToken))
        {
            r.Fail(null, $"A máquina {response.Name} foi revogada por {actor.Name}", null, null, retryable: true, now);
            await _queue.SaveChangesAsync(cancellationToken);
            await NotifyAsync(r, cancellationToken);
        }
        return response;
    }

    public async Task<bool> IsCredentialValidAsync(Guid workerId, Guid credentialId, CancellationToken cancellationToken) =>
        await _queue.GetWorkerCredentialAsync(workerId, cancellationToken) is { } c
        && c.Status != ExecutionWorkerStatus.Revoked && c.CredentialId == credentialId;

    // ── Configurações ────────────────────────────────────────────────────────────────────────────

    public async Task<ExecutionUserSettingsResponse> GetSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await _queue.GetSettingsAsync(userId, cancellationToken) ?? new ExecutionUserSettings(userId, DateTimeOffset.UtcNow);
        return await SettingsResponseAsync(settings, cancellationToken);
    }

    public async Task<ExecutionUserSettingsResponse> UpdateSettingsAsync(Guid userId, UpdateExecutionUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = await _queue.GetSettingsAsync(userId, cancellationToken);
        if (settings is null)
        {
            settings = new ExecutionUserSettings(userId, now);
            _queue.AddSettings(settings);
        }
        settings.SetBudget(request.DailyBudgetUsd, now);
        settings.SetAutoRule(request.AutoAnalyzeEnabled, request.AutoWorkItemTypes, request.AutoStates, request.AutoAreaPaths,
            request.AutoAssignedTo, request.AutoMaxPerDay, now);
        await _queue.SaveChangesAsync(cancellationToken);
        Pulse(userId);
        return await SettingsResponseAsync(settings, cancellationToken);
    }

    // ── Gatilho de retomada (chamado pelo plano) ─────────────────────────────────────────────────

    public async Task<ExecutionRequest?> PlanChangedAsync(ExecutionPlan plan, string source, ExecutionActor actor, bool explicitRequest, CancellationToken cancellationToken)
    {
        if (actor.IsExecutor || plan.IsFinished || plan.Status == ExecutionStatus.Paused)
            return null;
        if (plan.Status == ExecutionStatus.Failed && !explicitRequest)
            return null;
        if (plan.CreatedByUserId is not { } owner)
            return null;

        var workers = (await _queue.GetWorkersByOwnerAsync(owner, cancellationToken)).Where(w => !w.IsRevoked).ToList();
        if (workers.Count == 0)
            return null;
        var now = DateTimeOffset.UtcNow;
        if (await _queue.GetActiveRequestForCardAsync(plan.CardNumber, cancellationToken) is { } active)
            return await GatherIntoAsync(active, source, explicitRequest, now, cancellationToken);
        if (!explicitRequest && !HasWorkForClaude(plan, source))
            return null;

        var lastFinished = await _queue.GetLastFinishedAtAsync(plan.CardNumber, cancellationToken);
        var alive = plan.LastActivityAt is { } last && now - last < AliveWindow && (lastFinished is null || lastFinished < last);
        if (alive)
            return null;

        var session = plan.CurrentSession;
        var target = session?.Host is { } host
            ? workers.FirstOrDefault(w => string.Equals(w.Host, host, StringComparison.OrdinalIgnoreCase))?.Id
            : null;
        var request = new ExecutionRequest(plan.CardNumber, ExecutionRequestKind.Resume, source, owner, plan.CreatedBy,
            actor.UserId, actor.Name, plan.Id, session, target, null, false, now);
        // 0049: comentário espera um pouco — quem comenta costuma mandar vários em sequência; cada retomada relê o contexto.
        if (source == ExecutionRequestSource.Note && !explicitRequest && (await OptionsAsync(cancellationToken)).NoteDelay is { } delay && delay > TimeSpan.Zero)
            request.Delay(now + delay, GatheringText(now + delay), now);
        var saved = await AddRequestAsync(request, cancellationToken);
        if (saved is not null && saved.Id == request.Id)
            await UpdateWaitReasonsAsync(owner, now, cancellationToken);
        return saved;
    }

    public Task<HashSet<Guid>> OwnersWithWorkersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0 ? Task.FromResult(new HashSet<Guid>()) : _queue.GetOwnersWithWorkersAsync(userIds, cancellationToken);

    /// <summary>
    /// 0049: já há pedido no card. Na fila esperando comentários: um comentário novo empurra a espera (até 3× o
    /// intervalo desde o pedido) e o "Continuar" pela tela começa já. Retorna o pedido só no pedido explícito com ele
    /// ainda na fila (rodando → null: "o Claude já está trabalhando").
    /// </summary>
    private async Task<ExecutionRequest?> GatherIntoAsync(ExecutionRequest active, string source, bool explicitRequest, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (active.Status != ExecutionRequestStatus.Queued)
            return null;
        var changed = false;
        if (explicitRequest || (source != ExecutionRequestSource.Note && active.Source == ExecutionRequestSource.Note))
            // "Continuar" ou outra ação do usuário (respondeu, "Já resolvi"...) não espera mais comentários.
            changed = active.RunNow(now);
        else if (source == ExecutionRequestSource.Note && active.Attempts == 0 && active.NotBefore is { } nb && nb > now)
        {
            var delay = (await OptionsAsync(cancellationToken)).NoteDelay;
            var until = new[] { now + delay, active.CreatedAt + 3 * delay }.Min();
            changed = active.Delay(until, GatheringText(until), now);
        }
        if (changed)
        {
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
                await NotifyAsync(active, cancellationToken);
                Pulse(active.OwnerUserId);
            }
            catch (ExecutionPlanConcurrencyException)
            {
                // O executor pegou o pedido ao mesmo tempo — segue com o que ele já tem.
                _queue.ClearTracking();
            }
        }
        return explicitRequest ? active : null;
    }

    private static string GatheringText(DateTimeOffset until) =>
        $"Esperando mais comentários para retomar uma vez só — o Claude começa às {LocalTime(until)} (\"Continuar\" começa já)";

    public async Task<ExecutionSession?> RunningSessionAsync(string cardNumber, CancellationToken cancellationToken)
    {
        if (await _queue.GetActiveRequestForCardAsync(cardNumber.Trim(), cancellationToken) is not { Status: ExecutionRequestStatus.Running } r
            || string.IsNullOrEmpty(r.SessionId))
            return null;
        var host = r.WorkerId is { } workerId ? (await _queue.GetWorkerAsync(workerId, cancellationToken))?.Host : null;
        return new ExecutionSession { SessionId = r.SessionId, Host = host };
    }

    /// <summary>Tem algo para o Claude fazer agora: etapa dele em andamento ou pronta (ou um comentário novo do usuário).</summary>
    private static bool HasWorkForClaude(ExecutionPlan plan, string source)
    {
        if (source == ExecutionRequestSource.Note)
            return true;
        if (plan.Steps.Count == 0)
            return plan.Status is ExecutionStatus.Pending or ExecutionStatus.Running;
        var ready = plan.ReadySteps().Select(s => s.Key).ToHashSet();
        return plan.Steps.Any(s => s.Executor == ExecutionExecutor.Claude
                                   && (s.Status == ExecutionStatus.Running || ready.Contains(s.Key)));
    }

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private async Task<ExecutionClaimResponse?> TryClaimAsync(ExecutionWorker worker, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (worker.Status != ExecutionWorkerStatus.Active || worker.IsThrottled(now))
            return null;
        var counts = await _queue.CountActiveByWorkerAsync(worker.OwnerUserId, cancellationToken);
        if (counts.GetValueOrDefault(worker.Id) >= worker.MaxConcurrency)
            return null;

        var queued = await _queue.GetQueuedForOwnerAsync(worker.OwnerUserId, cancellationToken);
        if (queued.Count == 0)
            return null;
        var (budget, spent) = await BudgetAsync(worker.OwnerUserId, now, cancellationToken);
        var overBudget = budget is { } b && spent >= b;

        foreach (var request in queued)
        {
            if (!request.CanBeClaimedBy(worker, now) || (overBudget && !request.Force))
                continue;
            var (phase, newSession) = await PhaseOfAsync(request, cancellationToken);
            request.Claim(worker, now, phase);
            if (newSession)
                request.StartInNewSession(now);
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
            }
            catch (ExecutionPlanConcurrencyException)
            {
                // Outro executor (ou um cancelamento) mexeu no pedido ao mesmo tempo: tenta na próxima volta.
                return null;
            }
            await NotifyAsync(request, cancellationToken);
            return new ExecutionClaimResponse
            {
                Request = request.ToResponse(),
                ResumePrompt = ResumePrompt(request),
                FreshPrompt = FreshPrompt(request, phase),
                RemainingBudgetUsd = budget is { } limit && !request.Force ? Math.Max(0, limit - spent) : null,
                Phase = phase
            };
        }
        return null;
    }

    /// <summary>
    /// Trava vencida → volta para a fila (ou falha); fila de 24 h → expira; motivos de espera atualizados.
    /// No máximo a cada 15 s por dono (a não ser forçado). True quando mudou algo.
    /// </summary>
    private async Task<bool> MaintainAsync(Guid owner, DateTimeOffset now, CancellationToken cancellationToken, bool force)
    {
        if (!force && LastMaintenance.TryGetValue(owner, out var last) && now - last < MaintenanceInterval)
            return false;
        LastMaintenance[owner] = now;

        var changed = new List<ExecutionRequest>();
        foreach (var r in await _queue.GetStaleForOwnerAsync(owner, now, now - ExecutionRequest.QueueExpiration, cancellationToken))
        {
            if (r.ExpireLease(now) || r.ExpireQueued(now))
                changed.Add(r);
        }
        if (changed.Count > 0)
        {
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
            }
            catch (ExecutionPlanConcurrencyException)
            {
                _queue.ClearTracking();
                return false;
            }
            foreach (var r in changed) await NotifyAsync(r, cancellationToken);
        }
        await UpdateWaitReasonsAsync(owner, now, cancellationToken);
        return changed.Count > 0;
    }

    /// <summary>Por que cada pedido na fila do dono ainda não começou — gravado no pedido e mostrado no card.</summary>
    private async Task UpdateWaitReasonsAsync(Guid owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var queued = await _queue.GetQueuedForOwnerAsync(owner, cancellationToken);
        if (queued.Count == 0) return;
        var context = await WaitContextAsync(owner, now, cancellationToken);
        var changed = new List<ExecutionRequest>();
        foreach (var r in queued)
        {
            var before = r.WaitReason;
            r.SetWaitReason(Diagnose(r, context, now).Text, now);
            if (r.WaitReason != before) changed.Add(r);
        }
        if (changed.Count == 0) return;
        try
        {
            await _queue.SaveChangesAsync(cancellationToken);
            foreach (var r in changed) await NotifyAsync(r, cancellationToken);
        }
        catch (ExecutionPlanConcurrencyException)
        {
            _queue.ClearTracking();
        }
    }

    private record WaitContext(List<ExecutionWorker> Workers, Dictionary<Guid, int> Busy, decimal? Budget, decimal Spent, string? LatestAgentVersion = null);

    private async Task<WaitContext> WaitContextAsync(Guid owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var workers = (await _queue.GetWorkersByOwnerAsync(owner, cancellationToken)).ToList();
        var busy = await _queue.CountActiveByWorkerAsync(owner, cancellationToken);
        var (budget, spent) = await BudgetAsync(owner, now, cancellationToken);
        return new WaitContext(workers, busy, budget, spent, _agentInfo?.LatestVersion);
    }

    /// <summary>(código, texto) do motivo de espera; (null, null) = vai começar assim que um executor perguntar.</summary>
    private static (string? Code, string? Text) Diagnose(ExecutionRequest r, WaitContext ctx, DateTimeOffset now)
    {
        if (r.Status != ExecutionRequestStatus.Queued)
            return (null, null);
        var live = ctx.Workers.Where(w => !w.IsRevoked).ToList();
        if (live.Count == 0)
            return ("no-worker", "Nenhuma máquina com o executor do PRMake — instale em \"Meus executores\"");
        var candidates = r.TargetWorkerId is { } target ? live.Where(w => w.Id == target).ToList() : live;
        if (candidates.Count == 0)
            return ("no-worker", "A máquina escolhida foi revogada — cancele e peça de novo");
        var throttled = candidates.Where(w => w.IsThrottled(now)).ToList();
        if (throttled.Count == candidates.Count)
            return ("throttled", $"Limite de uso da conta do Claude atingido — continua às {LocalTime(throttled.Min(w => w.ThrottledUntil!.Value))}");
        if (r.NotBefore is { } nb && nb > now)
            return r.Attempts == 0 && r.Source == ExecutionRequestSource.Note
                ? ("gathering", r.WaitReason ?? GatheringText(nb))
                : ("retry", r.WaitReason ?? "Aguardando a próxima tentativa");
        var online = candidates.Where(w => w.IsOnline(now)).ToList();
        if (online.Count == 0)
            return ("offline", candidates.Count == 1
                ? $"A máquina {candidates[0].Name} está offline — o pedido começa quando ela voltar"
                : "Nenhuma das suas máquinas está online — o pedido começa quando uma voltar");
        var active = online.Where(w => w.Status == ExecutionWorkerStatus.Active).ToList();
        if (active.Count == 0)
            return ("paused", online.Count == 1 ? $"O executor de {online[0].Name} está pausado" : "Seus executores estão pausados");
        if (ctx.Budget is { } budget && ctx.Spent >= budget && !r.Force)
            return ("budget", $"Orçamento do dia atingido (US$ {ctx.Spent:0.00} de US$ {budget:0.00}) — libere o pedido ou aumente o orçamento");
        if (active.All(w => ctx.Busy.GetValueOrDefault(w.Id) >= w.MaxConcurrency))
            return ("busy", active.Count == 1 ? $"Aguardando {active[0].Name} terminar outro pedido" : "Suas máquinas estão ocupadas com outros pedidos");
        // 0050: executor livre e desatualizado se atualiza no próximo sinal de vida (até 1 min) antes de pegar pedidos.
        var free = active.Where(w => ctx.Busy.GetValueOrDefault(w.Id) < w.MaxConcurrency).ToList();
        if (ctx.LatestAgentVersion is { Length: > 0 } latest && free.Count > 0 && free.All(w => IsOlder(w.AgentVersion, latest)))
            return ("updating", free.Count == 1
                ? $"O executor de {free[0].Name} está se atualizando para a {latest} — o pedido começa logo em seguida"
                : $"Os executores estão se atualizando para a {latest} — o pedido começa logo em seguida");
        return (null, null);
    }

    private static bool IsOlder(string? version, string latest) =>
        Version.TryParse(version?.Split('-', '+')[0], out var v) && Version.TryParse(latest.Split('-', '+')[0], out var l) && v < l;

    private async Task EvaluateRuleAsync(Guid owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_workItems is null) return;
        var settings = await _queue.GetSettingsAsync(owner, cancellationToken);
        if (settings is null || !settings.AutoRuleDue(now, RuleInterval)) return;

        string? error = null;
        try
        {
            var since = StartOfDay(now);
            var remaining = settings.AutoMaxPerDay - await _queue.CountBySourceSinceAsync(owner, ExecutionRequestSource.Rule, since, cancellationToken);
            if (remaining > 0)
            {
                var cards = await _workItems.FindCardsAsync(settings, Math.Min(50, remaining * 4), cancellationToken);
                var ownerName = (await _queue.GetWorkersByOwnerAsync(owner, cancellationToken)).FirstOrDefault()?.OwnerName ?? "Usuário";
                foreach (var card in cards)
                {
                    if (remaining <= 0) break;
                    if (await _plans.GetCurrentPlanIdAsync(card, cancellationToken) is not null) continue;
                    if (await _queue.HasRequestForCardSinceAsync(card, now - RuleCooldown, cancellationToken)) continue;
                    var request = new ExecutionRequest(card, ExecutionRequestKind.Analyze, ExecutionRequestSource.Rule, owner, ownerName,
                        null, "Regra automática (Azure DevOps)", null, null, null, null, false, now);
                    if (await AddRequestAsync(request, cancellationToken) is { } saved && saved.Id == request.Id)
                        remaining--;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            error = e.Message;
        }

        _queue.ClearTracking();
        settings = await _queue.GetSettingsAsync(owner, cancellationToken);
        if (settings is null) return;
        settings.MarkAutoChecked(error, now);
        try
        {
            await _queue.SaveChangesAsync(cancellationToken);
        }
        catch (ExecutionPlanConcurrencyException)
        {
            _queue.ClearTracking();
        }
    }

    /// <summary>Grava o pedido; se já havia um ativo no card (corrida), devolve o existente.</summary>
    private async Task<ExecutionRequest?> AddRequestAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        _queue.AddRequest(request);
        try
        {
            await _queue.SaveChangesAsync(cancellationToken);
        }
        catch (ExecutionRequestDuplicateException)
        {
            _queue.ClearTracking();
            return await _queue.GetActiveRequestForCardAsync(request.CardNumber, cancellationToken);
        }
        await NotifyAsync(request, cancellationToken);
        Pulse(request.OwnerUserId);
        return request;
    }

    private async Task<(decimal? Budget, decimal Spent)> BudgetAsync(Guid owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = await _queue.GetSettingsAsync(owner, cancellationToken);
        if (settings?.DailyBudgetUsd is not { } budget)
            return (null, 0);
        return (budget, await _queue.GetCostSinceAsync(owner, StartOfDay(now), cancellationToken));
    }

    private static readonly TimeZoneInfo Brazil = ResolveBrazil();

    private static TimeZoneInfo ResolveBrazil()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
        }
    }

    /// <summary>Hora no horário de Brasília (HH:mm; com a data quando não é hoje).</summary>
    private static string LocalTime(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, Brazil);
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brazil).Date;
        return local.Date == today ? local.ToString("HH:mm") : local.ToString("dd/MM HH:mm");
    }

    /// <summary>Meia-noite de hoje no horário de Brasília (o "dia" do orçamento e da regra automática).</summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Brazil);
        // timestamptz no Npgsql só aceita offset 0.
        return new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
    }

    private async Task<ExecutionRequestResponse> RespondAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        var response = request.ToResponse();
        if (request.Status == ExecutionRequestStatus.Queued)
        {
            var now = DateTimeOffset.UtcNow;
            var (code, text) = Diagnose(request, await WaitContextAsync(request.OwnerUserId, now, cancellationToken), now);
            response.WaitCode = code;
            response.WaitReason = text ?? response.WaitReason;
        }
        return response;
    }

    private async Task<ExecutionSettingsBundle> SettingsBundleAsync(ExecutionUserSettings settings, CancellationToken cancellationToken) =>
        new(settings, await _queue.GetCostSinceAsync(settings.UserId, StartOfDay(DateTimeOffset.UtcNow), cancellationToken));

    private record ExecutionSettingsBundle(ExecutionUserSettings Settings, decimal Spent);

    private async Task<ExecutionUserSettingsResponse> SettingsResponseAsync(ExecutionUserSettings settings, CancellationToken cancellationToken)
    {
        var bundle = await SettingsBundleAsync(settings, cancellationToken);
        return new ExecutionUserSettingsResponse
        {
            DailyBudgetUsd = settings.DailyBudgetUsd,
            SpentTodayUsd = bundle.Spent,
            AutoAnalyzeEnabled = settings.AutoAnalyzeEnabled,
            AutoWorkItemTypes = settings.AutoWorkItemTypes.ToList(),
            AutoStates = settings.AutoStates.ToList(),
            AutoAreaPaths = settings.AutoAreaPaths.ToList(),
            AutoAssignedTo = settings.AutoAssignedTo ?? "@Me",
            AutoMaxPerDay = settings.AutoMaxPerDay,
            AutoLastCheckAt = settings.AutoLastCheckAt,
            AutoLastError = settings.AutoLastError
        };
    }

    private async Task<ExecutionPlan?> FindPlanAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        var planId = request.PlanId ?? await _plans.GetCurrentPlanIdAsync(request.CardNumber, cancellationToken);
        if (planId is null) return null;
        var plan = await _plans.GetPlanWithStepsAsync(planId.Value, cancellationToken);
        // Plano de antes do pedido e já terminado não é deste pedido (a análise ainda vai criar o dela).
        if (request.PlanId is null && plan is not null && plan.IsFinished && plan.UpdatedAt < request.CreatedAt)
            return null;
        return plan;
    }

    private async Task<ExecutionRequest> MutateRequestAsync(Guid requestId, Action<ExecutionRequest> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var request = await RequireRequestAsync(requestId, cancellationToken);
            mutate(request);
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
                return request;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < 5)
            {
                _queue.ClearTracking();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    private async Task<ExecutionWorkerResponse> MutateOwnWorkerAsync(Guid workerId, ExecutionActor actor, Action<ExecutionWorker, DateTimeOffset> mutate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var worker = await _queue.GetWorkerAsync(workerId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Máquina não encontrada.");
            if (worker.OwnerUserId != actor.UserId)
                throw new ExecutionForbiddenException("Só o dono mexe nesta máquina.");
            mutate(worker, now);
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
                Pulse(worker.OwnerUserId);
                await NotifyWorkersAsync(worker.OwnerUserId, cancellationToken);
                return await WorkerResponseAsync(worker, now, cancellationToken);
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < 5)
            {
                // O long-poll do executor grava o "último sinal" o tempo todo: relê e reaplica.
                _queue.ClearTracking();
            }
        }
    }

    /// <summary>
    /// Grava algo do próprio executor (sinal de vida, report, doctor). O long-poll grava o "último sinal" o tempo todo:
    /// conflito de concorrência relê e reaplica, em vez de devolver 409 (que perdia o resultado do doctor).
    /// </summary>
    private async Task<ExecutionWorker> MutateWorkerAsync(Guid workerId, Action<ExecutionWorker, DateTimeOffset> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var worker = await RequireWorkerAsync(workerId, cancellationToken);
            mutate(worker, now);
            try
            {
                await _queue.SaveChangesAsync(cancellationToken);
                return worker;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < 5)
            {
                _queue.ClearTracking();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    private async Task TouchWorkerAsync(Guid workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            _queue.ClearTracking();
            if (await _queue.GetWorkerAsync(workerId, cancellationToken) is not { } worker) return;
            worker.Seen(now);
            await _queue.SaveChangesAsync(cancellationToken);
        }
        catch (ExecutionPlanConcurrencyException)
        {
            _queue.ClearTracking();
        }
    }

    private async Task<ExecutionWorkerResponse> WorkerResponseAsync(ExecutionWorker worker, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var counts = await _queue.CountActiveByWorkerAsync(worker.OwnerUserId, cancellationToken);
        return worker.ToResponse(now, counts.GetValueOrDefault(worker.Id), _agentInfo?.LatestVersion);
    }

    private async Task<ExecutionRequest> RequireRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        await _queue.GetRequestAsync(requestId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Pedido não encontrado.");

    private async Task<ExecutionWorker> RequireWorkerAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var worker = await _queue.GetWorkerAsync(workerId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Executor não encontrado.");
        if (worker.IsRevoked)
            throw new ExecutionForbiddenException("Este executor foi revogado — registre a máquina de novo (prmake-agent register).");
        return worker;
    }

    /// <summary>
    /// 0047: fase do card para escolher o modelo — correção quando o plano aberto é o de correção ou quando as perguntas
    /// de <c>propor-solucoes</c> da análise já foram respondidas (a próxima execução já é a correção); senão, análise.
    /// </summary>
    private async Task<(string Phase, bool NewSession)> PhaseOfAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        if (await _plans.GetCurrentPlanIdAsync(request.CardNumber, cancellationToken) is not { } planId
            || await _plans.GetPlanWithStepsAsync(planId, cancellationToken) is not { } plan)
            return (ExecutionPhase.Analysis, false);
        var correction = plan.Phase == ExecutionPhase.Correction
                         || plan.Steps.Any(s => s.Key == ProposeSolutionsStep && s.Status == ExecutionStatus.Completed);
        if (!correction)
        {
            var proposal = (await _plans.GetQuestionsAsync(plan.Id, cancellationToken))
                .Where(q => q.StepKey == ProposeSolutionsStep && q.Status != ExecutionQuestionStatus.Cancelled)
                .ToList();
            correction = proposal.Count > 0 && proposal.All(q => q.Status == ExecutionQuestionStatus.Answered);
        }
        if (!correction)
            return (ExecutionPhase.Analysis, false);
        var analysisPlanId = plan.Phase == ExecutionPhase.Correction ? plan.ParentPlanId : plan.Id;
        return (ExecutionPhase.Correction, await IsAnalysisSessionAsync(request.SessionId, analysisPlanId, cancellationToken)
                                           && (await OptionsAsync(cancellationToken)).CorrectionInNewSession);
    }

    /// <summary>
    /// 0049: a sessão a retomar é a da análise? Quem a começou diz (pedido da fase de análise); sessão aberta fora do
    /// executor ou antes da 0049 → está entre as sessões do plano de análise.
    /// </summary>
    private async Task<bool> IsAnalysisSessionAsync(string? sessionId, Guid? analysisPlanId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
            return false;
        if (await _queue.GetSessionStarterAsync(sessionId, cancellationToken) is { Phase: { } startedIn })
            return startedIn == ExecutionPhase.Analysis;
        return analysisPlanId is { } id
               && (await _plans.GetSessionsAsync([id], cancellationToken)).TryGetValue(id, out var sessions)
               && sessions.Any(s => s.SessionId == sessionId);
    }

    /// <summary>Etapa da skill em que as soluções são propostas ao usuário (fim da análise).</summary>
    private const string ProposeSolutionsStep = "propor-solucoes";

    private static string FreshPrompt(ExecutionRequest r, string phase)
    {
        if (phase == ExecutionPhase.Correction)
            // 0049: a correção não carrega a conversa da análise — só o resumo que a análise deixou no plano. Sem invocar
            // a skill: o "model: opus" do cabeçalho dela valeria para a execução inteira do claude -p, por cima do --model
            // da correção (card 75067 rodou a correção toda no Opus).
            return $"Card {r.CardNumber} — skill analisar-bug, FASE DE CORRECAO numa sessao nova. Leia as instrucoes da skill com " +
                   "`cat ~/.claude/skills/analisar-bug/SKILL.md` (Bash) e siga-as — NAO use a ferramenta Skill nem /analisar-bug: a skill " +
                   "fixa o Opus para a execucao inteira e a correcao roda no modelo configurado no PRMake. " +
                   "A analise deste card rodou em outra sessao e o contexto dela nao esta aqui de proposito (cada resposta relia a analise " +
                   "inteira). Em vez do passo 2, comece com " +
                   $"`bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh contexto-correcao {r.CardNumber}`: ele retoma o plano e mostra " +
                   "o resumo para a correcao, as respostas, os comentarios e os arquivos. Nao refaca a investigacao — confie no resumo e " +
                   "na analise publicada; leia codigo/banco so no que a correcao precisar. Siga do passo 7 (sem plano de correcao) ou da " +
                   "primeira etapa pronta do plano de correcao." + ExecutorSuffix(r);
        var prompt = $"/analisar-bug {r.CardNumber}";
        if (r.Kind == ExecutionRequestKind.Resume)
            prompt += "\n\nEste card ja tem plano no PRMake: retome de onde parou (o contexto do plano mostra o que ja foi feito).";
        return prompt + ExecutorSuffix(r);
    }

    private static string ResumePrompt(ExecutionRequest r) =>
        // 0045: MCP primeiro (menos tokens que o script); o script fica de reserva quando o MCP nao esta na sessao.
        $"Retomando o card {r.CardNumber} pelo PRMake ({SourceText(r)}). Antes de continuar, veja o que mudou na tela enquanto voce estava " +
        $"parado (respostas, comentarios, pausa, etapas) com as ferramentas MCP prmake_plan, prmake_notes e prmake_answers (card {r.CardNumber}) " +
        $"— sem o MCP na sessao: prmake-plan.sh resume-info/notes/answers {r.CardNumber} — e siga de onde parou, conforme a skill analisar-bug." +
        ExecutorSuffix(r);

    private static string ExecutorSuffix(ExecutionRequest r)
    {
        var text = "\n\n(Executor do PRMake, sem terminal e sem ninguem olhando esta sessao: nao espere nada em segundo plano — " +
                   "quando depender de alguem, registre no plano e encerre a vez; o PRMake retoma esta sessao sozinho quando a pessoa agir.)";
        if (!string.IsNullOrWhiteSpace(r.Note))
            text += $"\n\nObservacao de {r.RequestedBy} ao pedir pelo PRMake: {r.Note}";
        return text;
    }

    private static string SourceText(ExecutionRequest r) => r.Source switch
    {
        ExecutionRequestSource.Answers => $"{r.RequestedBy} respondeu pela tela",
        ExecutionRequestSource.Resume => $"{r.RequestedBy} pediu para continuar",
        ExecutionRequestSource.UserAction => $"{r.RequestedBy} agiu no plano",
        ExecutionRequestSource.Note => $"{r.RequestedBy} comentou no plano",
        ExecutionRequestSource.PullRequest => "PR mesclado",
        ExecutionRequestSource.Rule => "regra automatica",
        _ => $"pedido de {r.RequestedBy}"
    };

    private async Task NotifyAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _realTime.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(request.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = request.CardNumber, planId = request.PlanId, action = ExecutionPlanRealTimeEvents.Actions.Request, requestId = request.Id, status = request.Status },
                cancellationToken);
            await NotifyWorkersAsync(request.OwnerUserId, cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort; a tela também consulta enquanto há pedido ativo.
        }
    }

    /// <summary>0050: atividade nova — a tela atualiza a linha "agora" com o payload (sem refazer o GET).</summary>
    private async Task NotifyActivityAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = request.ToResponse();
            await _realTime.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(request.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new
                {
                    cardNumber = request.CardNumber, planId = request.PlanId, action = ExecutionPlanRealTimeEvents.Actions.Activity,
                    requestId = request.Id, status = request.Status, activity = response.CurrentActivity, recent = response.RecentActivities
                },
                cancellationToken);
        }
        catch
        {
            // best-effort: a tela pega no próximo GET
        }
    }

    private static ExecutionActivity? ToActivity(ExecutionActivityRequest? a, DateTimeOffset now) =>
        a is null || string.IsNullOrWhiteSpace(a.Label)
            ? null
            // relógio da máquina adiantado não põe a atividade no futuro
            : new ExecutionActivity { Label = a.Label, Tool = a.Tool, At = a.At is { } at && at <= now ? at : now };

    private async Task NotifyWorkersAsync(Guid owner, CancellationToken cancellationToken)
    {
        try
        {
            await _realTime.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.PendingGroup,
                ExecutionPlanRealTimeEvents.EventWorkersChanged,
                new { userId = owner },
                cancellationToken);
        }
        catch
        {
            // best-effort
        }
    }

    private static void Pulse(Guid owner)
    {
        if (Signals.TryRemove(owner, out var signal))
            signal.TrySetResult();
    }

    private static async Task WaitSignalAsync(Guid owner, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) return;
        var signal = Signals.GetOrAdd(owner, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await signal.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}
