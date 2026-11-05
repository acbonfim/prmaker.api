using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>
/// 0085 (ajustes): <c>GET WorkItems/{number}/live</c> — a sessão de execução do item ao vivo: o pedido ativo (do próprio
/// item ou, no item de plano, o da pista principal do plano), o que o Claude está fazendo agora, as últimas atividades,
/// o histórico recente do item e se o executor está com sinal. Projeção leve: nada de texto grande.
/// </summary>
public class WorkItemLiveResponse
{
    public long Number { get; set; }
    public string Status { get; set; } = WorkItemStatus.Pending;
    /// <summary>Pedido ativo (queued/claimed/running) — null quando não há.</summary>
    public WorkItemLiveRequest? Request { get; set; }
    /// <summary>O que o Claude está fazendo agora (null sem pedido rodando).</summary>
    public ExecutionActivityResponse? Activity { get; set; }
    /// <summary>Últimas atividades do pedido, mais novas primeiro (como o executor manda — 0050).</summary>
    public List<ExecutionActivityResponse> Recent { get; set; } = [];
    /// <summary>Últimos <see cref="WorkItemLive.MaxLogs"/> registros do item, o mais novo por último.</summary>
    public List<WorkItemLogResponse> Logs { get; set; } = [];
    /// <summary>
    /// Executor com sinal: a máquina do pedido está online; sem pedido rodando, o dono tem executor ativo e online; no item
    /// de plano ativo, a skill deu sinal no plano nos últimos 5 min (o mesmo critério do "sem sinal do Claude" da tela).
    /// </summary>
    public bool ExecutorOnline { get; set; }
}

public class WorkItemLiveRequest
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? WorkerName { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    /// <summary>Só na fila: no-worker | offline | paused | busy | budget | retry | gathering | throttled | updating | null.</summary>
    public string? WaitCode { get; set; }
}

/// <summary>Cabeçalho do item para o "ao vivo" (com o status e o último sinal do plano, quando é item de plano).</summary>
public sealed record WorkItemLiveHead(Guid Id, long Number, string CardNumber, string Status, Guid? PlanId, Guid? CreatedBy,
    Guid? PlanOwnerUserId, string? PlanStatus, DateTimeOffset? PlanLastActivityAt);

/// <summary>Pedido ativo projetado (sem nota, stderr nem prompt).</summary>
public sealed record WorkItemLiveRequestRow(Guid Id, string Kind, string Status, Guid OwnerUserId, Guid? WorkerId, string? WorkerName,
    DateTimeOffset? ClaimedAt, DateTimeOffset? StartedAt, DateTimeOffset? LastHeartbeatAt, string? CurrentActivity,
    string? CurrentActivityTool, DateTimeOffset? CurrentActivityAt, string? RecentActivities);

/// <summary>Máquina do dono (só o que decide o sinal).</summary>
public sealed record WorkItemLiveWorker(Guid Id, string Status, DateTimeOffset? LastSeenAt)
{
    public bool IsOnline(DateTimeOffset now) =>
        Status != ExecutionWorkerStatus.Revoked && LastSeenAt is { } seen && now - seen <= ExecutionWorker.OnlineWindow;

    public bool AcceptsWork(DateTimeOffset now) => Status == ExecutionWorkerStatus.Active && IsOnline(now);
}

public static class WorkItemLive
{
    public const int MaxLogs = 50;

    /// <summary>Sem sinal da skill no plano há mais que isto = "sem sinal do Claude" (igual à tela do plano).</summary>
    public static readonly TimeSpan PlanStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Dono das máquinas que contam para o sinal: o do pedido, senão o do plano, senão quem criou o item, senão quem vê.</summary>
    public static Guid? OwnerOf(WorkItemLiveHead head, WorkItemLiveRequestRow? request, Guid? viewer) =>
        request?.OwnerUserId ?? head.PlanOwnerUserId ?? head.CreatedBy ?? viewer;

    public static bool IsExecutorOnline(WorkItemLiveHead head, WorkItemLiveRequestRow? request, IReadOnlyCollection<WorkItemLiveWorker> workers,
        DateTimeOffset now)
    {
        // Skill rodando no terminal (sem fila): o plano ativo recebeu sinal há pouco.
        if (head.PlanStatus is ExecutionStatus.Pending or ExecutionStatus.Running or ExecutionStatus.Paused
            && head.PlanLastActivityAt is { } last && now - last <= PlanStaleAfter)
            return true;
        if (request is { WorkerId: { } workerId } && request.Status is ExecutionRequestStatus.Claimed or ExecutionRequestStatus.Running)
            return workers.Any(w => w.Id == workerId && w.IsOnline(now))
                   || request.LastHeartbeatAt is { } beat && now - beat <= ExecutionWorker.OnlineWindow;
        return workers.Any(w => w.AcceptsWork(now));
    }

    public static WorkItemLiveResponse Build(WorkItemLiveHead head, WorkItemLiveRequestRow? request, string? waitCode,
        IReadOnlyCollection<WorkItemLog> logs, IReadOnlyCollection<WorkItemLiveWorker> workers, DateTimeOffset now) => new()
    {
        Number = head.Number,
        Status = head.Status,
        Request = request is null ? null : new WorkItemLiveRequest
        {
            Id = request.Id,
            Kind = request.Kind,
            Status = request.Status,
            WorkerName = request.WorkerName,
            ClaimedAt = request.ClaimedAt,
            StartedAt = request.StartedAt,
            LastHeartbeatAt = request.LastHeartbeatAt,
            WaitCode = request.Status == ExecutionRequestStatus.Queued ? waitCode : null,
        },
        Activity = request is { CurrentActivity: { } label, CurrentActivityAt: { } at }
            ? new ExecutionActivityResponse { Label = label, Tool = request.CurrentActivityTool, At = at }
            : null,
        Recent = request is null
            ? []
            : ExecutionActivity.Parse(request.RecentActivities)
                .Select(a => new ExecutionActivityResponse { Label = a.Label, Tool = a.Tool, At = a.At }).ToList(),
        Logs = logs.OrderBy(l => l.At).ThenBy(l => l.Id).TakeLast(MaxLogs).Select(WorkItemLogResponse.From).ToList(),
        ExecutorOnline = IsExecutorOnline(head, request, workers, now),
    };
}
