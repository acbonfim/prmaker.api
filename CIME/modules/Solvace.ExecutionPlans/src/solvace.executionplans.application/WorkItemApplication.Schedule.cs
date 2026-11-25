using Microsoft.Extensions.Logging;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0103: agendamento de itens e sub-tarefas. A pessoa (tela) ou o Claude (skills/especialista) agenda; no horário o tique
/// da fila libera — o Cloud Run escala a zero e não tem timer confiável, então o tique roda nos sinais de vida dos
/// executores (só faz sentido com algum executor ligado) e o pedido vai para a máquina de quem agendou.
/// </summary>
public partial class WorkItemApplication
{
    /// <summary>Intervalo mínimo entre dois tiques na mesma instância (os executores dão sinal a cada 60 s; com o relay fora, a cada 3 s).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);
    private const int ReleaseBatch = 20;
    private static long _lastTickTicks;

    public async Task<WorkItemResponse> ScheduleAsync(long number, WorkItemScheduleRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var action = (req.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkScheduleAction.All.Contains(action))
            throw new DomainException($"Ação inválida: '{req.Action}' (use set, park, clear ou run-now).");
        if (action == WorkScheduleAction.RunNow)
            return await ExecuteAsync(number, actor, ct, runNow: true);

        var now = DateTimeOffset.UtcNow;
        var at = action == WorkScheduleAction.Set
            ? ScheduleTime.Parse(req.At, now) ?? throw new DomainException("Informe o horário do agendamento (at).")
            : (DateTimeOffset?)null;
        var changed = true;
        var item = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number, i =>
        {
            switch (action)
            {
                case WorkScheduleAction.Set: i.Schedule(at, false, actor.Name, actor.UserId, DateTimeOffset.UtcNow); break;
                case WorkScheduleAction.Park: i.Schedule(null, true, actor.Name, actor.UserId, DateTimeOffset.UtcNow); break;
                default: changed = i.ClearSchedule(actor.Name, $"Agendamento removido por {actor.Name} — roda quando alguém executar.", DateTimeOffset.UtcNow); break;
            }
        }, ct);
        if (changed)
        {
            await NotifyAsync(item, ExecutionPlanRealTimeEvents.WorkItemActions.Status, null, ct);
            await TimelineAsync(item, action switch
            {
                WorkScheduleAction.Set => $"Item de trabalho **#{item.Number}** agendado por {actor.Name} para {ScheduleTime.Format(item.ScheduledAt!.Value)}.",
                WorkScheduleAction.Park => $"Item de trabalho **#{item.Number}** em espera, sem data ({actor.Name}) — roda quando alguém decidir.",
                _ => $"Item de trabalho **#{item.Number}**: agendamento removido por {actor.Name}.",
            }, actor, ct);
        }
        return (await ToResponsesAsync([item], ct))[0];
    }

    public async Task<int> ReleaseDueSchedulesAsync(CancellationToken ct, bool force = false)
    {
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        var last = Interlocked.Read(ref _lastTickTicks);
        if (!force && nowTicks - last < TickInterval.Ticks) return 0;
        if (!force && Interlocked.CompareExchange(ref _lastTickTicks, nowTicks, last) != last) return 0;

        var now = DateTimeOffset.UtcNow;
        var (items, tasks) = await _items.GetDueSchedulesAsync(now, ReleaseBatch, ct);
        var released = 0;
        foreach (var number in items)
        {
            try
            {
                if (await ReleaseItemAsync(number, now, ct)) released++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log?.LogWarning(e, "0103: agendamento do item #{Number} não liberou", number);
            }
        }
        foreach (var number in tasks)
        {
            try
            {
                if (await ReleaseTaskAsync(number, now, ct)) released++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log?.LogWarning(e, "0103: agendamento da sub-tarefa #{Number} não liberou", number);
            }
        }
        _items.DetachWorkItems();
        return released;
    }

    private ILogger<WorkItemApplication>? Log => (ILogger<WorkItemApplication>?)_services.GetService(typeof(ILogger<WorkItemApplication>));

    /// <summary>Quem agendou age no horário (dono do pedido, nome no histórico e na Timeline).</summary>
    private static ExecutionActor ScheduleActor(Guid? userId, string? name) =>
        new(userId, $"{(string.IsNullOrWhiteSpace(name) ? "Agendamento" : name.Trim())} (agendado)", false);

    private async Task<bool> ReleaseItemAsync(long number, DateTimeOffset now, CancellationToken ct)
    {
        _items.DetachWorkItems();
        var item = await _items.GetByNumberAsync(number, ct);
        if (item?.ScheduledAt is not { } at || at > now) return false;
        var actor = ScheduleActor(item.ScheduledById ?? item.CreatedBy, item.ScheduledBy ?? item.CreatedByName);
        try
        {
            await ExecuteAsync(number, actor, ct, runNow: true);
            _items.DetachWorkItems();
            if (await _items.GetByNumberAsync(number, ct) is { } started)
                await TimelineAsync(started, $"Item de trabalho **#{number}** começou no horário agendado ({ScheduleTime.Format(at)}).", actor, ct);
            return true;
        }
        catch (DomainException e)
        {
            // Não deu para começar (sem sub-tarefa aberta, executor...): tira o horário para não tentar a cada tique e conta por quê.
            _items.DetachWorkItems();
            var failed = await MutateAsync(() => _items.GetByNumberAsync(number, ct), number,
                i => i.ClearSchedule(actor.Name, $"O horário agendado ({ScheduleTime.Format(at)}) chegou, mas não deu para executar: {e.Message}", DateTimeOffset.UtcNow), ct);
            await NotifyAsync(failed, ExecutionPlanRealTimeEvents.WorkItemActions.Status, null, ct);
            return false;
        }
    }

    private async Task<bool> ReleaseTaskAsync(long taskNumber, DateTimeOffset now, CancellationToken ct)
    {
        _items.DetachWorkItems();
        var owner = await _items.GetByTaskNumberAsync(taskNumber, ct);
        var task = owner?.Tasks.FirstOrDefault(t => t.Number == taskNumber);
        if (owner is null || task is not { Held: true, ScheduledAt: { } at } || at > now) return false;
        var actor = ScheduleActor(task.ScheduledById ?? owner.CreatedBy, task.HeldBy ?? owner.CreatedByName);
        try
        {
            // Continuar = tira a pausa e o horário; o item é retomado (espelho: a etapa do plano e a retomada do plano).
            await ControlTaskAsync(taskNumber, new WorkTaskControlRequest
            {
                Action = WorkTaskControl.Resume, Reason = $"chegou o horário agendado ({ScheduleTime.Format(at)})",
                Execute = owner.AwaitsSchedule ? null : true,
            }, actor, ct);
            return true;
        }
        catch (DomainException e)
        {
            _items.DetachWorkItems();
            var again = await _items.GetByTaskNumberAsync(taskNumber, ct);
            if (again?.Tasks.FirstOrDefault(t => t.Number == taskNumber) is not { Held: true, ScheduledAt: not null })
                return false;
            // Fica pausada, sem data, com o motivo — a pessoa decide (continuar, agendar de novo, cancelar).
            _items.DetachWorkItems();
            await ControlTaskAsync(taskNumber, new WorkTaskControlRequest
            {
                Action = WorkTaskControl.Pause, Reason = $"o horário agendado ({ScheduleTime.Format(at)}) chegou, mas não deu para continuar: {e.Message}",
                Execute = false,
            }, actor, ct);
            return false;
        }
    }

    /// <summary>Texto do agendamento para a Timeline da criação.</summary>
    private static string ScheduleSuffix(WorkItem item) =>
        item.ScheduledAt is { } at ? $" Agendado para {ScheduleTime.Format(at)}."
        : item.Parked ? " Em espera, sem data (roda quando alguém decidir)."
        : item.NextTaskScheduleAt is { } next ? $" Sub-tarefa agendada para {ScheduleTime.Format(next)}." : string.Empty;
}
