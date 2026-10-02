using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Plano de execução de uma skill (ex.: analisar-bug) sobre um card: as etapas, o status geral e
/// o sinal de vida (heartbeat) de quem executa. Toda regra de transição de status vive aqui.
/// </summary>
public class ExecutionPlan
{
    public const int MaxTitleLength = 300;
    public const int MaxSummaryLength = 20_000;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }

    public string Status { get; private set; } = ExecutionStatus.Pending;
    public string? StatusReason { get; private set; }
    public string? StatusChangedBy { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Última chamada da skill. Sem sinal por muito tempo = a sessão provavelmente caiu.</summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>Token de concorrência (xmin do PostgreSQL): usuário e skill mexendo ao mesmo tempo não se atropelam.</summary>
    public uint Version { get; private set; }

    public List<ExecutionStep> Steps { get; private set; } = [];

    /// <summary>analysis | correction (0024).</summary>
    public string Phase { get; private set; } = ExecutionPhase.Analysis;

    /// <summary>Plano de análise que originou este plano de correção (0024).</summary>
    public Guid? ParentPlanId { get; private set; }

    /// <summary>Sessões do Claude Code que trabalharam no plano (0033) — a última vista é a que se retoma.</summary>
    public List<ExecutionSession> Sessions { get; private set; } = [];

    /// <summary>Pedido de "continuar" feito na tela (0033): o vigia local retoma a sessão; a skill confirma ao pegar.</summary>
    public DateTimeOffset? ResumeRequestedAt { get; private set; }
    public string? ResumeRequestedBy { get; private set; }
    public DateTimeOffset? ResumeHandledAt { get; private set; }

    /// <summary>
    /// Até qual comentário do card a skill já leu (0037) e quando — a tela mostra "o Claude leu e está analisando".
    /// Gravado direto no banco quando a skill busca os comentários (não passa pelo agregado).
    /// </summary>
    public int? NotesReadNumber { get; private set; }
    public DateTimeOffset? NotesReadAt { get; private set; }

    public const int MaxSessions = 50;

    public ExecutionSession? CurrentSession => Sessions.OrderByDescending(s => s.LastSeenAt).FirstOrDefault();

    public bool ResumePending => ResumeRequestedAt is { } requested && (ResumeHandledAt is null || ResumeHandledAt < requested);

    /// <summary>A skill informa em qual sessão/máquina/pasta está trabalhando (no start, ao retomar e no heartbeat).</summary>
    public ExecutionSession RegisterSession(string sessionId, string? host, string? cwd, DateTimeOffset now)
    {
        var id = Clean(sessionId, ExecutionSession.MaxSessionIdLength) ?? throw new DomainException("Informe o id da sessão do Claude Code.");
        var session = Sessions.FirstOrDefault(s => s.SessionId == id);
        if (session is null)
        {
            session = new ExecutionSession { SessionId = id, StartedAt = now };
            Sessions.Add(session);
            if (Sessions.Count > MaxSessions)
                Sessions.Remove(Sessions.OrderBy(s => s.LastSeenAt).First());
        }
        session.Host = Clean(host, ExecutionSession.MaxHostLength) ?? session.Host;
        session.Cwd = Clean(cwd, ExecutionSession.MaxCwdLength) ?? session.Cwd;
        session.LastSeenAt = now;
        return session;
    }

    /// <summary>Custo acumulado da sessão (a skill manda o total lido do transcript; o último valor vence).</summary>
    public void RecordUsage(string sessionId, string? host, int turns, long input, long output, long cacheRead, long cacheWrite, string? model, DateTimeOffset now,
        int? mcpCalls = null, int? scriptCalls = null)
    {
        if (turns < 0 || input < 0 || output < 0 || cacheRead < 0 || cacheWrite < 0)
            throw new DomainException("Valores de uso inválidos.");
        var session = RegisterSession(sessionId, host, null, now);
        session.Turns = turns;
        session.InputTokens = input;
        session.OutputTokens = output;
        session.CacheReadTokens = cacheRead;
        session.CacheWriteTokens = cacheWrite;
        session.Model = Clean(model, 100) ?? session.Model;
        if (mcpCalls is >= 0) session.McpCalls = mcpCalls;
        if (scriptCalls is >= 0) session.ScriptCalls = scriptCalls;
        session.UsageUpdatedAt = now;
    }

    /// <summary>
    /// 0044: linha de base da sessão = o que ela já registrava no plano pai (análise → correção na mesma sessão).
    /// Só a primeira vez; sessão que não veio do pai fica com base zero.
    /// </summary>
    public void SetSessionBaseline(string sessionId, ExecutionSession? fromParent, DateTimeOffset now)
    {
        var session = RegisterSession(sessionId, null, null, now);
        if (session.BaselineSet) return;
        session.BaselineSet = true;
        if (fromParent is null) return;
        session.BaseTurns = fromParent.Turns;
        session.BaseInputTokens = fromParent.InputTokens;
        session.BaseOutputTokens = fromParent.OutputTokens;
        session.BaseCacheReadTokens = fromParent.CacheReadTokens;
        session.BaseCacheWriteTokens = fromParent.CacheWriteTokens;
        session.BaseMcpCalls = fromParent.McpCalls ?? 0;
        session.BaseScriptCalls = fromParent.ScriptCalls ?? 0;
    }

    /// <summary>Modelo da sessão mais recente que informou consumo (para estimar o custo na tela).</summary>
    public string? UsageModel => Sessions.Where(s => s.Model != null).OrderByDescending(s => s.UsageUpdatedAt ?? s.LastSeenAt).FirstOrDefault()?.Model;

    public void RequestResume(string actor, DateTimeOffset now)
    {
        if (IsFinished) throw new DomainException("O plano já terminou — não há o que continuar.");
        if (CurrentSession is null)
            throw new DomainException("Nenhuma sessão do Claude registrada neste plano — retome com prmake-card no terminal.");
        ResumeRequestedAt = now;
        ResumeRequestedBy = actor;
        UpdatedAt = now;
    }

    public void AcknowledgeResume(DateTimeOffset now)
    {
        ResumeHandledAt = now;
        UpdatedAt = now;
    }

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }

    protected ExecutionPlan() { }

    public ExecutionPlan(string cardNumber, string kind, string title, string? summary, Guid? userId, string userName, DateTimeOffset now,
        string? phase = null, Guid? parentPlanId = null)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase) ? ExecutionPhase.Analysis : phase.Trim().ToLowerInvariant();
        if (!ExecutionPhase.All.Contains(normalizedPhase))
            throw new DomainException($"Fase inválida: '{phase}' (use analysis ou correction).");
        if (normalizedPhase == ExecutionPhase.Correction && parentPlanId is null)
            throw new DomainException("O plano de correção precisa do plano de análise de origem (parentPlanId).");
        Phase = normalizedPhase;
        ParentPlanId = normalizedPhase == ExecutionPhase.Correction ? parentPlanId : null;

        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new DomainException("O número do card é obrigatório.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("O nome de quem criou o plano é obrigatório.");

        Id = Guid.NewGuid();
        CardNumber = cardNumber.Trim();
        Kind = string.IsNullOrWhiteSpace(kind) ? "analisar-bug" : kind.Trim().ToLowerInvariant();
        SetTitle(title);
        SetSummary(summary);
        CreatedByUserId = userId;
        CreatedBy = userName.Trim();
        CreatedAt = now;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>Completed/cancelled: o plano terminou. Só "completed" pode ser reaberto (pela skill).</summary>
    public bool IsFinished => Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled;

    public void SetTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new DomainException("O título do plano é obrigatório.");
        Title = trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }

    private void SetSummary(string? summary)
    {
        var trimmed = summary?.Trim();
        if (trimmed is { Length: > MaxSummaryLength })
            throw new DomainException($"O resumo pode ter no máximo {MaxSummaryLength} caracteres.");
        if (!string.IsNullOrEmpty(trimmed))
            Summary = trimmed;
    }

    /// <summary>Sinal de vida da skill (toda chamada dela passa por aqui).</summary>
    public void Touch(DateTimeOffset now, bool fromExecutor)
    {
        UpdatedAt = now;
        if (fromExecutor)
            LastActivityAt = now;
    }

    /// <summary>
    /// Define/refina as etapas (upsert pela chave), na ordem enviada. Etapas que sumiram da lista:
    /// se ainda pendentes, saem do plano; se já começaram, ficam (histórico) depois das enviadas.
    /// </summary>
    public void UpsertSteps(IReadOnlyList<ExecutionStepDefinition> definitions, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var seen = new HashSet<string>();
        var order = 0;
        foreach (var def in definitions)
        {
            var key = ExecutionStep.NormalizeKey(def.Key);
            if (!seen.Add(key))
                throw new DomainException($"Etapa repetida no plano: '{key}'.");

            order += 10;
            var step = FindStep(key);
            if (step is null)
            {
                step = new ExecutionStep(Id, key, order, def.Title, def.Description, now);
                Steps.Add(step);
            }
            else
            {
                step.Order = order;
                step.SetTitle(def.Title);
                if (def.Description is not null)
                    step.SetDescription(def.Description);
                step.Touch(now);
            }
            step.SetShape(def.Executor, def.Kind, def.Repository, def.DependsOn);
        }

        foreach (var orphan in Steps.Where(s => !seen.Contains(s.Key)).OrderBy(s => s.Order).ToList())
        {
            if (orphan.Status == ExecutionStatus.Pending)
            {
                Steps.Remove(orphan);
            }
            else
            {
                order += 10;
                orphan.Order = order;
            }
        }

        Touch(now, fromExecutor: true);
    }

    /// <summary>
    /// Atualização de uma etapa pela skill. Etapa desconhecida é criada no fim (a skill nunca perde
    /// um envio por ter esquecido de declarar a etapa antes).
    /// </summary>
    public ExecutionStep UpdateStep(string key, string? status, string? reason, string? activity, string? checkpoint,
        string? title, string? description, string actor, DateTimeOffset now, string? waitingOn = null)
    {
        EnsureNotCancelled();

        var normalized = ExecutionStep.NormalizeKey(key);
        var step = FindStep(normalized);
        if (step is null)
        {
            step = new ExecutionStep(Id, normalized, NextOrder(), string.IsNullOrWhiteSpace(title) ? normalized : title, description, now);
            Steps.Add(step);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(title)) step.SetTitle(title);
            if (description is not null) step.SetDescription(description);
        }

        if (activity is not null) step.SetActivity(activity);
        if (checkpoint is not null) step.SetCheckpoint(checkpoint);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            step.ChangeStatus(normalizedStatus, reason, actor, now, waitingOn);

            // A skill voltou a trabalhar: o plano (pendente, com falha ou já concluído) volta a
            // "em andamento". Pausado continua pausado — só o usuário (ou a retomada) tira da pausa.
            if (normalizedStatus is ExecutionStatus.Running or ExecutionStatus.Waiting
                && Status is ExecutionStatus.Pending or ExecutionStatus.Failed or ExecutionStatus.Completed)
            {
                SetPlanStatus(ExecutionStatus.Running, null, actor, now);
            }
        }
        else
        {
            step.Touch(now);
        }

        Touch(now, fromExecutor: true);
        return step;
    }

    /// <summary>A atividade atual da etapa acompanha o último pedaço de andamento enviado.</summary>
    public void TrackActivity(string stepKey, string message, DateTimeOffset now)
    {
        var step = FindStep(stepKey.Trim().ToLowerInvariant());
        if (step is null || ExecutionStatus.IsStepFinished(step.Status))
            return;

        var firstLine = message.Split('\n', 2)[0].Trim().TrimStart('#', '*', '-', ' ');
        if (firstLine.Length > 0)
            step.SetActivity(firstLine);
    }

    /// <summary>O usuário começa uma etapa pela tela (típico das etapas executor=user) — 0024.</summary>
    public ExecutionStep StartStepByUser(string key, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (ExecutionStatus.IsStepFinished(step.Status))
            throw new DomainException("Esta etapa já terminou.");
        step.ChangeStatus(ExecutionStatus.Running, null, actor, now);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>O usuário conclui uma etapa pela tela (ex.: abriu o chamado, validou em QA) — 0024.</summary>
    public ExecutionStep CompleteStepByUser(string key, string? reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (step.Status == ExecutionStatus.Completed)
            throw new DomainException("Esta etapa já foi concluída.");
        step.ChangeStatus(ExecutionStatus.Completed, string.IsNullOrWhiteSpace(reason) ? null : reason, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>Etapa passa a aguardar algo externo (resposta, chamado, merge); o plano segue — 0024.</summary>
    public ExecutionStep SetStepWaiting(string key, string reason, string waitingOn, string actor, DateTimeOffset now, bool fromExecutor)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (ExecutionStatus.IsStepFinished(step.Status))
            return step;
        step.ChangeStatus(ExecutionStatus.Waiting, reason, actor, now, waitingOn);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>O que a etapa esperava chegou (ex.: todas as perguntas respondidas): volta a "em andamento".</summary>
    public ExecutionStep? ResumeStepFromWait(string key, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || step.Status != ExecutionStatus.Waiting)
            return null;
        step.ChangeStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>
    /// "Já resolvi" pela tela (0037): o usuário fez o que a etapa esperava dele (liberou a permissão, ligou a VPN...).
    /// A etapa volta a "em andamento" — o vigia da skill vê a mudança e tenta de novo.
    /// </summary>
    public ExecutionStep ResolveStepByUser(string key, string? note, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (step.Status != ExecutionStatus.Waiting || step.WaitingOn != ExecutionWaitingOn.User)
            throw new DomainException("Esta etapa não está aguardando você.");
        var trimmed = note?.Trim();
        step.ChangeStatus(ExecutionStatus.Running, string.IsNullOrEmpty(trimmed) ? $"Resolvido por {actor}" : $"Resolvido por {actor}: {trimmed}", actor, now);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// O que depende do usuário agora (0037): etapas do usuário prontas/em andamento e etapas travadas esperando uma
    /// ação dele. As perguntas abertas entram à parte (não são do agregado). Plano terminado/falho não tem pendência.
    /// </summary>
    public IEnumerable<ExecutionStep> StepsPendingForUser()
    {
        if (IsFinished || Status == ExecutionStatus.Failed)
            return [];
        var ready = ReadySteps().Select(s => s.Key).ToHashSet();
        return Steps.Where(s =>
                (s.Status == ExecutionStatus.Waiting && s.WaitingOn == ExecutionWaitingOn.User)
                || (s.Executor == ExecutionExecutor.User && (s.Status == ExecutionStatus.Running || ready.Contains(s.Key))))
            .OrderBy(s => s.Order);
    }

    /// <summary>
    /// Recalcula a etapa pelos chamados que a bloqueiam (0024): algum aberto → aguardando; o mais recente
    /// resolvido → concluída; só fechados sem resolução → aguardando um novo chamado.
    /// </summary>
    public ExecutionStep? ApplyTicketState(string key, IReadOnlyCollection<ExecutionLink> blockingTickets, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || Status == ExecutionStatus.Cancelled || blockingTickets.Count == 0)
            return null;
        if (step.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled)
            return null;

        var open = blockingTickets.Where(t => t.Status == ExecutionLinkStatus.Open).ToList();
        if (open.Count > 0)
        {
            var label = open.Count == 1 ? $"o chamado {open[0].DisplayName}" : $"{open.Count} chamados";
            return SetStepWaiting(key, $"Aguardando {label}", ExecutionWaitingOn.External, actor, now, fromExecutor);
        }

        var latest = blockingTickets.OrderByDescending(t => t.CreatedAt).First();
        if (latest.Status == ExecutionLinkStatus.Resolved)
        {
            step.ChangeStatus(ExecutionStatus.Completed, $"Chamado {latest.DisplayName} resolvido", actor, now);
            Touch(now, fromExecutor);
            return step;
        }

        return SetStepWaiting(key, $"Chamado {latest.DisplayName} fechado sem resolução — abra outro chamado ou conclua a etapa",
            ExecutionWaitingOn.User, actor, now, fromExecutor);
    }

    /// <summary>Conclui uma etapa por uma regra automática (ex.: PRs mesclados) — 0024.</summary>
    public ExecutionStep? CompleteStep(string key, string reason, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || ExecutionStatus.IsStepFinished(step.Status) || Status == ExecutionStatus.Cancelled)
            return null;
        step.ChangeStatus(ExecutionStatus.Completed, reason, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>
    /// Plano de correção com todas as etapas terminadas (e ao menos uma concluída) → concluído (0024).
    /// Com etapas de PR, elas só concluem com os PRs mesclados — daí "concluído = PRs mesclados".
    /// </summary>
    public bool TryAutoComplete(string actor, DateTimeOffset now)
    {
        if (Phase != ExecutionPhase.Correction || IsFinished || Status == ExecutionStatus.Failed || Steps.Count == 0)
            return false;
        if (!Steps.All(s => s.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled) || !Steps.Any(s => s.Status == ExecutionStatus.Completed))
            return false;
        SetPlanStatus(ExecutionStatus.Completed, "Todas as etapas concluídas", actor, now);
        Touch(now, fromExecutor: false);
        return true;
    }

    /// <summary>Etapas prontas para começar: pendentes com todas as dependências terminadas.</summary>
    public IEnumerable<ExecutionStep> ReadySteps() =>
        Steps.Where(s => s.Status == ExecutionStatus.Pending && s.DependsOn.All(d =>
        {
            var dep = FindStep(d);
            return dep is null || dep.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled;
        })).OrderBy(s => s.Order);

    private ExecutionStep RequireStep(string key) =>
        FindStep(ExecutionStep.NormalizeKey(key)) ?? throw new DomainException("Etapa não encontrada.");

    /// <summary>O usuário cancela (pula) uma etapa que ainda não terminou.</summary>
    public ExecutionStep CancelStep(string key, string reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var step = FindStep(ExecutionStep.NormalizeKey(key))
                   ?? throw new DomainException("Etapa não encontrada.");
        if (ExecutionStatus.IsStepFinished(step.Status))
            throw new DomainException("Esta etapa já terminou e não pode ser cancelada.");

        step.ChangeStatus(ExecutionStatus.Cancelled, reason, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// Troca o status do plano (usuário: pausar/continuar/cancelar; skill: retomar/concluir/falhar).
    /// </summary>
    public void ChangeStatus(string status, string? reason, string? summary, string actor, bool fromExecutor, DateTimeOffset now)
    {
        var target = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ExecutionStatus.PlanStatuses.Contains(target) || target == ExecutionStatus.Pending)
            throw new DomainException($"Status de plano inválido: '{status}'.");

        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado e não pode mudar de status.");

        switch (target)
        {
            case ExecutionStatus.Paused:
                if (Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
                    throw new DomainException("Só um plano pendente ou em andamento pode ser pausado.");
                break;

            case ExecutionStatus.Running:
                // Continuar (usuário) ou retomar (skill). Concluído só volta pela skill (reabrir).
                if (Status == ExecutionStatus.Completed && !fromExecutor)
                    throw new DomainException("O plano já foi concluído.");
                break;

            case ExecutionStatus.Cancelled:
                if (Status == ExecutionStatus.Completed)
                    throw new DomainException("O plano já foi concluído.");
                var cancelReason = string.IsNullOrWhiteSpace(reason) ? $"Plano cancelado por {actor}" : reason.Trim();
                foreach (var step in Steps.Where(s => !ExecutionStatus.IsStepFinished(s.Status)))
                    step.ChangeStatus(ExecutionStatus.Cancelled, cancelReason, actor, now);
                break;

            case ExecutionStatus.Completed:
                foreach (var step in Steps.Where(s => s.Status == ExecutionStatus.Running))
                    step.ChangeStatus(ExecutionStatus.Completed, null, actor, now);
                foreach (var step in Steps.Where(s => s.Status is ExecutionStatus.Pending or ExecutionStatus.Waiting))
                    step.ChangeStatus(ExecutionStatus.Cancelled, "Não executada", actor, now);
                break;

            case ExecutionStatus.Failed:
                foreach (var step in Steps.Where(s => s.Status is ExecutionStatus.Running or ExecutionStatus.Waiting))
                    step.ChangeStatus(ExecutionStatus.Failed, reason, actor, now);
                break;
        }

        SetSummary(summary);
        SetPlanStatus(target, reason, actor, now);
        Touch(now, fromExecutor);
    }

    private void SetPlanStatus(string status, string? reason, string actor, DateTimeOffset now)
    {
        if (status == ExecutionStatus.Running)
        {
            StartedAt ??= now;
            FinishedAt = null;
        }
        else if (status is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed)
        {
            FinishedAt = now;
        }

        Status = status;
        var trimmed = reason?.Trim();
        StatusReason = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length <= ExecutionStep.MaxReasonLength ? trimmed : trimmed[..ExecutionStep.MaxReasonLength];
        StatusChangedBy = actor;
    }

    private void EnsureNotCancelled()
    {
        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado. Crie um novo plano para continuar.");
    }

    private ExecutionStep? FindStep(string key) => Steps.FirstOrDefault(s => s.Key == key);

    private int NextOrder() => (Steps.Count == 0 ? 0 : Steps.Max(s => s.Order)) + 10;
}
