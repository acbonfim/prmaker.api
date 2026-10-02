namespace solvace.executionplans.domain.Entities;

/// <summary>Status do pedido de execução (0039). Compartilhados com o frontend e com o executor — mantenha em sincronia.</summary>
public static class ExecutionRequestStatus
{
    public const string Queued = "queued";
    public const string Claimed = "claimed";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    /// <summary>Pedido que ainda vai rodar ou está rodando — no máximo um por card.</summary>
    public static readonly IReadOnlySet<string> Active = new HashSet<string> { Queued, Claimed, Running };

    public static bool IsActive(string status) => Active.Contains(status);
}

public static class ExecutionRequestKind
{
    /// <summary>Começar a análise do card (<c>/analisar-bug &lt;card&gt;</c>).</summary>
    public const string Analyze = "analyze";

    /// <summary>Continuar a sessão do card (<c>claude --resume</c>; sem a sessão na máquina, começa uma nova que retoma o plano).</summary>
    public const string Resume = "resume";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Analyze, Resume };
}

/// <summary>De onde veio o pedido.</summary>
public static class ExecutionRequestSource
{
    public const string Button = "button";
    public const string Answers = "answers";
    public const string Resume = "resume";
    public const string UserAction = "user-action";
    public const string Note = "note";
    public const string PullRequest = "pr";
    public const string Rule = "rule";
    public const string Api = "api";
}

/// <summary>
/// Pedido de execução (0039): "rode a skill para este card" — na fila até um executor do dono pegar. O executor
/// pega com trava (<see cref="LeaseUntil"/>) e renova com heartbeat; trava vencida devolve o pedido para a fila
/// (tentativa + 1) e, passado o máximo, o pedido falha com o motivo visível no card.
/// </summary>
public class ExecutionRequest
{
    public const int MaxNoteLength = 4_000;
    public const int MaxErrorLength = 4_000;
    public const int MaxStderrLength = 8_000;
    public const int DefaultMaxAttempts = 3;

    /// <summary>Sem heartbeat por esse tempo, o executor é dado como morto e o pedido volta para a fila.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    /// <summary>Espera antes de cada nova tentativa (1ª, 2ª, 3ª...).</summary>
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    /// <summary>Na fila sem nenhum executor pegar por esse tempo → expira (avisa no card).</summary>
    public static readonly TimeSpan QueueExpiration = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Kind { get; private set; } = ExecutionRequestKind.Analyze;
    public string Source { get; private set; } = ExecutionRequestSource.Button;
    public string Status { get; private set; } = ExecutionRequestStatus.Queued;

    public Guid? PlanId { get; private set; }
    public string? SessionId { get; private set; }
    /// <summary>Máquina onde a sessão a retomar vive (vem do plano).</summary>
    public string? SessionHost { get; private set; }
    public string? SessionCwd { get; private set; }

    /// <summary>Dono: só os executores dele pegam o pedido.</summary>
    public Guid OwnerUserId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public Guid? RequestedByUserId { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;

    public Guid? TargetWorkerId { get; private set; }
    public Guid? WorkerId { get; private set; }
    public string? WorkerName { get; private set; }

    public string? Note { get; private set; }
    /// <summary>Roda mesmo com o orçamento do dia estourado (o dono liberou).</summary>
    public bool Force { get; private set; }

    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; } = DefaultMaxAttempts;
    public DateTimeOffset? NotBefore { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public int? Pid { get; private set; }
    /// <summary>Por que ainda não começou (sem executor online, orçamento, executor pausado...). Atualizado pela fila.</summary>
    public string? WaitReason { get; private set; }

    public string? LastError { get; private set; }
    public string? StderrTail { get; private set; }
    public int? ExitCode { get; private set; }
    public string? FinishedReason { get; private set; }
    public string? FinishedBy { get; private set; }

    /// <summary>Custo DESTE pedido (0044: o Claude Code informa o acumulado da sessão — aqui fica a diferença).</summary>
    public decimal? CostUsd { get; private set; }
    /// <summary>0044: acumulado da sessão informado pelo Claude Code no fim deste pedido (base do próximo pedido).</summary>
    public decimal? SessionCostUsd { get; private set; }
    /// <summary>Entrada total (nova + cache lido + cache escrito).</summary>
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    /// <summary>0044: as partes da entrada (executor 1.0.3+).</summary>
    public long? FreshInputTokens { get; private set; }
    public long? CacheReadTokens { get; private set; }
    public long? CacheWriteTokens { get; private set; }
    public string? Model { get; private set; }
    public int? Turns { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    /// <summary>xmin: claim, heartbeat e cancelamento simultâneos não se atropelam.</summary>
    public uint Version { get; private set; }

    protected ExecutionRequest() { }

    public ExecutionRequest(string cardNumber, string kind, string source, Guid ownerUserId, string ownerName,
        Guid? requestedByUserId, string requestedBy, Guid? planId, ExecutionSession? session, Guid? targetWorkerId,
        string? note, bool force, DateTimeOffset now)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        if (card.Length == 0 || card.Length > 100)
            throw new DomainException("Informe o número do card.");
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (!ExecutionRequestKind.All.Contains(k))
            throw new DomainException($"Tipo de pedido inválido: '{kind}'.");

        Id = Guid.NewGuid();
        CardNumber = card;
        Kind = k;
        Source = string.IsNullOrWhiteSpace(source) ? ExecutionRequestSource.Api : source.Trim();
        OwnerUserId = ownerUserId;
        OwnerName = Clean(ownerName, 200) ?? "Usuário";
        RequestedByUserId = requestedByUserId;
        RequestedBy = Clean(requestedBy, 200) ?? "Usuário";
        PlanId = planId;
        SessionId = session?.SessionId;
        SessionHost = session?.Host;
        SessionCwd = session?.Cwd;
        TargetWorkerId = targetWorkerId;
        Note = Clean(note, MaxNoteLength);
        Force = force;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public bool IsActive => ExecutionRequestStatus.IsActive(Status);
    public bool IsFinished => !IsActive;

    public bool CanBeClaimedBy(ExecutionWorker worker, DateTimeOffset now) =>
        Status == ExecutionRequestStatus.Queued
        && worker.OwnerUserId == OwnerUserId
        && (TargetWorkerId is null || TargetWorkerId == worker.Id)
        && (NotBefore is null || NotBefore <= now);

    public void Claim(ExecutionWorker worker, DateTimeOffset now)
    {
        if (!CanBeClaimedBy(worker, now))
            throw new DomainException("Este pedido não está disponível para este executor.");
        Status = ExecutionRequestStatus.Claimed;
        WorkerId = worker.Id;
        WorkerName = worker.Name;
        ClaimedAt = now;
        LeaseUntil = now + LeaseDuration;
        Attempts++;
        WaitReason = null;
        LastHeartbeatAt = now;
        UpdatedAt = now;
    }

    /// <summary>O executor subiu o processo do Claude.</summary>
    public void Start(Guid workerId, int? pid, string? sessionId, DateTimeOffset now)
    {
        EnsureWorker(workerId);
        if (Status is not (ExecutionRequestStatus.Claimed or ExecutionRequestStatus.Running))
            throw new DomainException("O pedido não está mais com este executor.");
        Status = ExecutionRequestStatus.Running;
        StartedAt ??= now;
        Pid = pid;
        if (!string.IsNullOrWhiteSpace(sessionId))
            SessionId = Clean(sessionId, ExecutionSession.MaxSessionIdLength);
        LeaseUntil = now + LeaseDuration;
        LastHeartbeatAt = now;
        UpdatedAt = now;
    }

    public void Heartbeat(Guid workerId, int? pid, string? stderrTail, DateTimeOffset now)
    {
        EnsureWorker(workerId);
        if (!IsActive || Status == ExecutionRequestStatus.Queued)
            return;
        if (pid is not null) Pid = pid;
        if (stderrTail is not null) StderrTail = Tail(stderrTail, MaxStderrLength);
        LeaseUntil = now + LeaseDuration;
        LastHeartbeatAt = now;
        UpdatedAt = now;
    }

    public const int MaxModelLength = 100;

    /// <summary>
    /// Consumo da tentativa. <paramref name="sessionCostUsd"/> é o acumulado da sessão (o Claude Code soma as
    /// retomadas); <paramref name="previousSessionCostUsd"/> é o acumulado no fim do pedido anterior da mesma sessão —
    /// o custo deste pedido é a diferença (0044). Acumulado menor que o anterior = sessão recomeçou: vale inteiro.
    /// </summary>
    public void RecordUsage(decimal? sessionCostUsd, decimal? previousSessionCostUsd, long? inputTokens, long? outputTokens, int? turns,
        long? freshInputTokens = null, long? cacheReadTokens = null, long? cacheWriteTokens = null, string? model = null)
    {
        if (sessionCostUsd is { } total)
        {
            total = Math.Max(0, total);
            var previous = SessionCostUsd ?? previousSessionCostUsd;
            var delta = previous is { } p && total >= p ? total - p : total;
            CostUsd = (CostUsd ?? 0) + delta;
            SessionCostUsd = total;
        }
        if (inputTokens is not null) InputTokens = (InputTokens ?? 0) + Math.Max(0, inputTokens.Value);
        if (outputTokens is not null) OutputTokens = (OutputTokens ?? 0) + Math.Max(0, outputTokens.Value);
        if (turns is not null) Turns = (Turns ?? 0) + Math.Max(0, turns.Value);
        if (freshInputTokens is not null) FreshInputTokens = (FreshInputTokens ?? 0) + Math.Max(0, freshInputTokens.Value);
        if (cacheReadTokens is not null) CacheReadTokens = (CacheReadTokens ?? 0) + Math.Max(0, cacheReadTokens.Value);
        if (cacheWriteTokens is not null) CacheWriteTokens = (CacheWriteTokens ?? 0) + Math.Max(0, cacheWriteTokens.Value);
        if (!string.IsNullOrWhiteSpace(model)) Model = Clean(model, MaxModelLength);
    }

    /// <summary>O processo terminou bem (a skill encerrou a vez: pergunta, PR aberto, plano concluído...).</summary>
    public void Complete(Guid workerId, int? exitCode, string? reason, DateTimeOffset now)
    {
        EnsureWorker(workerId);
        if (!IsActive) return;
        Finish(ExecutionRequestStatus.Done, Clean(reason, MaxErrorLength) ?? "Concluído", null, now);
        ExitCode = exitCode;
    }

    /// <summary>Falhou. <paramref name="retryable"/>: volta para a fila com espera (até o máximo de tentativas).</summary>
    public void Fail(Guid? workerId, string error, int? exitCode, string? stderrTail, bool retryable, DateTimeOffset now)
    {
        if (workerId is not null) EnsureWorker(workerId.Value);
        if (!IsActive) return;
        LastError = Clean(error, MaxErrorLength) ?? "Falha sem detalhe";
        ExitCode = exitCode;
        if (stderrTail is not null) StderrTail = Tail(stderrTail, MaxStderrLength);

        if (retryable && Attempts < MaxAttempts)
        {
            Status = ExecutionRequestStatus.Queued;
            NotBefore = now + Backoff[Math.Clamp(Attempts - 1, 0, Backoff.Length - 1)];
            LeaseUntil = null;
            Pid = null;
            WaitReason = $"Nova tentativa ({Attempts + 1}/{MaxAttempts}) depois de: {LastError}";
            UpdatedAt = now;
            return;
        }
        Finish(ExecutionRequestStatus.Failed, LastError, null, now);
    }

    /// <summary>
    /// Limite de uso da conta do Claude (0041): volta para a fila até o reset, sem gastar tentativa — tentar antes só
    /// queimaria as tentativas.
    /// </summary>
    public void Throttle(Guid workerId, DateTimeOffset retryAt, string reason, string? stderrTail, DateTimeOffset now)
    {
        EnsureWorker(workerId);
        if (!IsActive) return;
        Status = ExecutionRequestStatus.Queued;
        Attempts = Math.Max(0, Attempts - 1);
        NotBefore = retryAt;
        LeaseUntil = null;
        Pid = null;
        LastError = Clean(reason, MaxErrorLength);
        WaitReason = Clean(reason, 500);
        if (stderrTail is not null) StderrTail = Tail(stderrTail, MaxStderrLength);
        UpdatedAt = now;
    }

    /// <summary>Trava vencida: o executor sumiu (máquina desligou, processo morreu).</summary>
    public bool ExpireLease(DateTimeOffset now)
    {
        if (Status is not (ExecutionRequestStatus.Claimed or ExecutionRequestStatus.Running) || LeaseUntil is null || LeaseUntil > now)
            return false;
        Fail(null, WorkerName is null ? "O executor parou de dar sinal" : $"O executor {WorkerName} parou de dar sinal",
            null, null, retryable: true, now);
        return true;
    }

    /// <summary>Ninguém pegou o pedido a tempo.</summary>
    public bool ExpireQueued(DateTimeOffset now)
    {
        if (Status != ExecutionRequestStatus.Queued || now - CreatedAt < QueueExpiration)
            return false;
        Finish(ExecutionRequestStatus.Expired, WaitReason is null
            ? "Nenhum executor pegou o pedido em 24 h"
            : $"Nenhum executor pegou o pedido em 24 h ({WaitReason})", null, now);
        return true;
    }

    public void Cancel(string actor, string? reason, DateTimeOffset now)
    {
        if (!IsActive)
            throw new DomainException("Este pedido já terminou.");
        Finish(ExecutionRequestStatus.Cancelled, Clean(reason, MaxErrorLength) ?? $"Cancelado por {actor}", actor, now);
    }

    /// <summary>O dono libera o pedido mesmo com o orçamento do dia estourado.</summary>
    public void AllowOverBudget(DateTimeOffset now)
    {
        if (Status != ExecutionRequestStatus.Queued)
            throw new DomainException("Só dá para liberar um pedido que está na fila.");
        Force = true;
        UpdatedAt = now;
    }

    public void SetWaitReason(string? reason, DateTimeOffset now)
    {
        var r = Clean(reason, 500);
        if (Status != ExecutionRequestStatus.Queued || r == WaitReason) return;
        WaitReason = r;
        UpdatedAt = now;
    }

    private void Finish(string status, string? reason, string? actor, DateTimeOffset now)
    {
        Status = status;
        FinishedReason = reason;
        FinishedBy = actor;
        FinishedAt = now;
        LeaseUntil = null;
        NotBefore = null;
        WaitReason = null;
        UpdatedAt = now;
    }

    private void EnsureWorker(Guid workerId)
    {
        if (WorkerId != workerId)
            throw new DomainException("O pedido não está com este executor.");
    }

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }

    private static string Tail(string value, int max) => value.Length <= max ? value : value[^max..];
}
