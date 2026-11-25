using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.domain.RealTime;

namespace solvace.executionplans.application;

/// <summary>
/// Card 78680: a pessoa (tela ou especialista) mexe numa etapa do plano sem cancelar o item — muda a ordem, pausa, continua
/// ou cancela só ela (e, se quiser, as que dependem dela). Se o tratamento estava na etapa atropelada, o plano é pausado e
/// retomado: o Claude recomeça pela ordem nova em vez de cancelar tudo e montar outro item.
/// </summary>
public partial class ExecutionPlanApplication
{
    public async Task<ExecutionStepResponse> ControlStepAsync(Guid planId, string stepKey, WorkTaskControlRequest request, string? beforeKey,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkTaskControl.All.Contains(action))
            throw new DomainException($"Ação inválida: '{request.Action}' (use move, pause, resume, cancel ou schedule).");
        var key = ExecutionStep.NormalizeKey(stepKey);
        // 0103: agendar = pausar até o horário (no horário o PRMake continua sozinho).
        var at = action == WorkTaskControl.Schedule
            ? ScheduleTime.Parse(request.At, DateTimeOffset.UtcNow) ?? throw new DomainException("Informe o horário do agendamento (at).")
            : (DateTimeOffset?)null;
        var interrupted = new List<string>();
        var cancelled = new List<string>();
        ExecutionStep? step = null;

        var plan = await MutateAsync(planId, p =>
        {
            interrupted.Clear();
            cancelled.Clear();
            var now = DateTimeOffset.UtcNow;
            var current = p.Steps.FirstOrDefault(s => s.Key == key) ?? throw new ExecutionPlanNotFoundException($"Etapa '{stepKey}' não encontrada.");
            switch (action)
            {
                case WorkTaskControl.Move:
                    var position = TaskPosition.Normalize(request.Position, !string.IsNullOrWhiteSpace(beforeKey));
                    interrupted.AddRange(p.MoveStep(key, position, beforeKey, actor.Name, now).Select(s => s.Key));
                    break;
                case WorkTaskControl.Pause:
                    if (current.Status == ExecutionStatus.Running) interrupted.Add(key);
                    p.HoldStep(key, true, actor.Name, now);
                    break;
                case WorkTaskControl.Resume:
                    p.HoldStep(key, false, actor.Name, now);
                    break;
                case WorkTaskControl.Schedule:
                    if (current.Status == ExecutionStatus.Running) interrupted.Add(key);
                    p.ScheduleStep(key, at!.Value, actor.Name, actor.UserId, now);
                    break;
                case WorkTaskControl.Cancel:
                    var dependents = p.DependentsOf(key);
                    var mode = request.Dependents?.Trim().ToLowerInvariant();
                    if (dependents.Count > 0 && mode is not (WorkTaskControl.DependentsCancel or WorkTaskControl.DependentsKeep))
                        throw new DomainException($"Dependem desta etapa: {string.Join(", ", dependents.Select(d => $"'{d.Key}' ({d.Title})"))} — " +
                                                  "informe dependents: cancel (cancelar junto) ou keep (seguem sem ela).");
                    var why = string.IsNullOrWhiteSpace(request.Reason) ? $"Cancelada por {actor.Name}" : $"{request.Reason.Trim()} — {actor.Name}";
                    var targets = new List<ExecutionStep> { current };
                    if (mode == WorkTaskControl.DependentsCancel) targets.AddRange(dependents);
                    foreach (var t in targets)
                    {
                        if (t.Status == ExecutionStatus.Running) interrupted.Add(t.Key);
                        p.CancelStep(t.Key, t == current ? why : $"Cancelada junto com '{key}' (dependia dela) — {actor.Name}", actor.Name, now);
                        cancelled.Add(t.Key);
                    }
                    break;
            }
            step = current;
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Steps, key, cancellationToken);
        if (interrupted.Count > 0 && plan.Status == ExecutionStatus.Running)
            await RestartTreatmentAsync(plan.Id, interrupted, actor, cancellationToken);
        else if (request.Execute != false && action is not (WorkTaskControl.Pause or WorkTaskControl.Schedule))
            await TriggerResumeAsync(plan, actor, ExecutionRequestSource.UserAction, cancellationToken);
        return step!.ToResponse();
    }

    /// <summary>
    /// O tratamento estava numa etapa que a pessoa tirou do lugar/pausou/cancelou: pausa e retoma o plano — a sessão para e o
    /// executor recomeça pela primeira etapa pronta da ordem nova. Best-effort (a mudança já foi gravada).
    /// </summary>
    private async Task RestartTreatmentAsync(Guid planId, IReadOnlyList<string> interrupted, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var what = string.Join(", ", interrupted.Select(k => $"'{k}'"));
        try
        {
            await ChangeStatusAsync(planId, new ChangeExecutionPlanStatusRequest
            {
                Status = ExecutionStatus.Paused, Reason = $"{actor.Name} mudou a etapa em andamento ({what}) — recomeçando na ordem nova"
            }, actor, cancellationToken);
            await ChangeStatusAsync(planId, new ChangeExecutionPlanStatusRequest
            {
                Status = ExecutionStatus.Running, Reason = $"Retomado na ordem nova ({actor.Name})"
            }, actor, cancellationToken);
        }
        catch (DomainException)
        {
            _repository.ClearTracking();
        }
    }
}
