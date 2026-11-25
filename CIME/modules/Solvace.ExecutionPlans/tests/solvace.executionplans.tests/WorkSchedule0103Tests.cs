using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0103: agendamento. Esteira de deploy que leva ~2h depois do merge, pipeline manual, job que só roda de madrugada (card
/// 78794, #1080: conferir depois das 04:30 de Brasília) — a sub-tarefa (ou o item) espera o horário; as que dependem dela
/// esperam junto e as independentes seguem.
/// </summary>
public class WorkSchedule0103Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-25T20:00:00Z");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-26T07:30:00Z"); // 04:30 em Brasília
    private static readonly Guid Alex = Guid.NewGuid();

    private static WorkItem Loose(params WorkTaskInput[] tasks)
    {
        var item = new WorkItem("78794", "task", "Validar", null, WorkItemOrigin.User, null, null, tasks.ToList(), Alex, "Alex", Now);
        typeof(WorkItem).GetProperty(nameof(WorkItem.Number))!.SetValue(item, 2000L);
        var n = 2000L;
        foreach (var t in item.OrderedTasks)
            typeof(WorkItemTask).GetProperty(nameof(WorkItemTask.Number))!.SetValue(t, ++n);
        return item;
    }

    private static WorkTaskInput T(string title, params long[] dependsOn) =>
        new() { Title = title, Kind = "task", DependsOn = dependsOn.Length == 0 ? null : dependsOn.ToList() };

    private static WorkItemTask Task(WorkItem item, long n) => item.Tasks.Single(t => t.Number == n);

    // ---------------------------------------------------------------- horário

    [Fact]
    public void Time_without_zone_is_brasilia_and_is_kept_in_utc()
    {
        Assert.Equal(At, ScheduleTime.Parse("2026-11-26T04:30", Now));
        Assert.Equal(At, ScheduleTime.Parse("2026-11-26T04:30:00-03:00", Now));
        Assert.Equal(At, ScheduleTime.Parse("2026-11-26T07:30:00Z", Now));
        Assert.Equal(At, ScheduleTime.Parse("26/11/2026 04:30", Now));
        Assert.Equal(TimeSpan.Zero, ScheduleTime.Parse("2026-11-26T04:30", Now)!.Value.Offset);
        Assert.Null(ScheduleTime.Parse("  ", Now));
    }

    [Fact]
    public void Relative_time_counts_from_now()
    {
        Assert.Equal(Now.AddMinutes(150), ScheduleTime.Parse("+2h30m", Now));
        Assert.Equal(Now.AddMinutes(90), ScheduleTime.Parse("+90m", Now));
        Assert.Equal(Now.AddDays(1), ScheduleTime.Parse("+1d", Now));
        Assert.Throws<DomainException>(() => ScheduleTime.Parse("+0m", Now));
    }

    [Fact]
    public void Past_or_too_far_is_refused()
    {
        Assert.Contains("já passou", Assert.Throws<DomainException>(() => ScheduleTime.Parse("2026-11-25T10:00:00Z", Now)).Message);
        Assert.Contains("60 dias", Assert.Throws<DomainException>(() => ScheduleTime.Parse("+61d", Now)).Message);
        Assert.Throws<DomainException>(() => ScheduleTime.Parse("amanhã cedo", Now));
    }

    [Fact]
    public void Format_is_brasilia()
    {
        Assert.StartsWith("26/11", ScheduleTime.Format(At));
        Assert.Contains("04:30", ScheduleTime.Format(At));
        Assert.EndsWith("(Brasília)", ScheduleTime.Format(At));
    }

    // ---------------------------------------------------------------- sub-tarefa agendada

    [Fact]
    public void Scheduled_task_and_its_dependents_wait_while_independent_ones_go_on()
    {
        // #2001 código → #2002 validar em QA (depende) → #2003 fechar (depende da validação); #2004 avisar (independente).
        var item = Loose(T("Código"), T("Validar em QA", 2001), T("Fechar card", 2002), T("Avisar o time"));
        item.ApplyTaskAction(2001, WorkTaskAction.Start, null, null, null, "claude", Now);
        item.ApplyTaskAction(2001, WorkTaskAction.Complete, "ok", null, null, "claude", Now);

        item.ScheduleTask(2002, At, "Alex", Alex, Now);

        var validate = Task(item, 2002);
        Assert.True(validate.Held);
        Assert.Equal(At, validate.ScheduledAt);
        Assert.Equal(Alex, validate.ScheduledById);
        Assert.Equal(2002, item.HeldBlocker(Task(item, 2003))!.Number); // indireta: espera junto
        Assert.Equal(2004, item.NextTask!.Number);                    // independente segue
        Assert.Equal(At, item.NextTaskScheduleAt);
        Assert.Equal(At, item.Controls(validate).ScheduledAt);
        Assert.Null(item.Controls(validate).Pause); // pausar = tirar a data
    }

    [Fact]
    public void Waiting_claude_task_scheduled_goes_back_to_pending_until_the_time()
    {
        // #1080: o Claude deixou "aguardando a próxima execução do job" — agora espera o horário.
        var item = Loose(T("Conferir a geração"), T("Fechamento", 2001));
        item.ApplyTaskAction(2001, WorkTaskAction.Start, null, null, null, "claude", Now);
        item.ApplyTaskAction(2001, WorkTaskAction.Wait, "o job roda às 07:15 UTC", null, null, "claude", Now);

        item.ScheduleTask(2001, At, "Alex", Alex, Now);

        Assert.Equal(WorkTaskStatus.Pending, Task(item, 2001).Status);
        Assert.True(item.OnlyHeldLeft);
        Assert.Null(item.NextTask);
        Assert.Contains("Agendada para", Task(item, 2001).Result);
    }

    [Fact]
    public void Executor_scheduling_its_own_running_task_is_not_an_interruption()
    {
        var item = Loose(T("Conferir a geração"), T("Avisar o time"));
        item.MarkQueued(Guid.NewGuid(), "Alex", Now);
        item.ApplyTaskAction(2001, WorkTaskAction.Start, null, null, null, "claude", Now);

        // A pessoa (tela/especialista) precisaria parar a execução antes...
        Assert.Throws<WorkTaskInterruptRequiredException>(() => item.ScheduleTask(2001, At, "Alex", Alex, Now));
        // ...o Claude que está nela agenda e segue com a independente.
        item.ScheduleTask(2001, At, "Claude", Alex, Now, byExecutor: true);

        Assert.Equal(WorkTaskStatus.Pending, Task(item, 2001).Status);
        Assert.Equal(At, Task(item, 2001).ScheduledAt);
        Assert.Equal(2002, item.NextTask!.Number);
    }

    [Fact]
    public void Pause_on_scheduled_removes_the_date_and_resume_releases_it()
    {
        var item = Loose(T("Validar"));
        item.ScheduleTask(2001, At, "Alex", Alex, Now);

        item.HoldTask(2001, true, null, "Alex", Now);
        Assert.True(Task(item, 2001).Held);
        Assert.Null(Task(item, 2001).ScheduledAt);
        Assert.Equal("Já está pausada.", item.Controls(Task(item, 2001)).Pause);

        item.ScheduleTask(2001, At.AddHours(1), "Alex", Alex, Now);
        Assert.Equal(At.AddHours(1), Task(item, 2001).ScheduledAt);

        item.HoldTask(2001, false, "chegou o horário", "Alex (agendado)", Now);
        Assert.False(Task(item, 2001).Held);
        Assert.Null(Task(item, 2001).ScheduledAt);
        Assert.Null(Task(item, 2001).ScheduledById);
        Assert.Equal(2001, item.NextTask!.Number);
    }

    [Fact]
    public void Task_can_be_created_already_scheduled()
    {
        var item = new WorkItem("78794", "task", "Validar", null, WorkItemOrigin.Specialist, null, null,
            [new WorkTaskInput { Title = "Validar em DEV", Kind = "env-validation", ScheduledAt = "+2h30m" }], Alex, "Alex", Now);

        var task = item.Tasks.Single();
        Assert.True(task.Held);
        Assert.Equal(Now.AddMinutes(150), task.ScheduledAt);
        Assert.Equal("Alex", task.HeldBy);
        Assert.Equal(Alex, task.ScheduledById);
    }

    [Fact]
    public void Finished_task_cannot_be_scheduled()
    {
        var item = Loose(T("Código"), T("PR", 2001));
        item.ApplyTaskAction(2001, WorkTaskAction.Start, null, null, null, "claude", Now);
        item.ApplyTaskAction(2001, WorkTaskAction.Complete, "ok", null, null, "claude", Now);

        Assert.Contains("já terminou", Assert.Throws<DomainException>(() => item.ScheduleTask(2001, At, "Alex", Alex, Now)).Message);
    }

    // ---------------------------------------------------------------- item agendado / em espera

    [Fact]
    public void Item_scheduled_or_parked_awaits_and_clear_releases()
    {
        var item = Loose(T("Validar em QA"));

        item.Schedule(At, false, "Alex", Alex, Now);
        Assert.True(item.AwaitsSchedule);
        Assert.Equal(At, item.ScheduledAt);
        Assert.False(item.Parked);
        var response = WorkItemResponse.From(item);
        Assert.Equal(At, response.ScheduledAt);
        Assert.Equal("Alex", response.ScheduledBy);

        item.Schedule(null, true, "Alex", Alex, Now);
        Assert.True(item.Parked);
        Assert.Null(item.ScheduledAt);

        Assert.True(item.ClearSchedule("Alex", null, Now));
        Assert.False(item.AwaitsSchedule);
        Assert.False(item.ClearSchedule("Alex", null, Now));
    }

    [Fact]
    public void Plan_item_and_running_item_cannot_be_scheduled_as_a_whole()
    {
        var plan = new ExecutionPlan("78794", "analisar-bug", "Correção", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        var mirror = new WorkItem("78794", WorkItemType.Fix, "Correção", null, WorkItemOrigin.Correction, null, plan.Id,
            [new WorkTaskInput { Title = "Conferir", StepKey = "conferir" }], Alex, "Alex", Now);
        Assert.Contains("agende a sub-tarefa", mirror.ScheduleBlock);
        Assert.Throws<DomainException>(() => mirror.Schedule(At, false, "Alex", Alex, Now));

        var running = Loose(T("Código"));
        running.MarkQueued(Guid.NewGuid(), "Alex", Now);
        Assert.Contains("fila", running.ScheduleBlock);
    }

    [Fact]
    public void Cancel_drops_the_schedule()
    {
        var item = Loose(T("Validar"));
        item.Schedule(At, false, "Alex", Alex, Now);

        item.Cancel("não precisa mais", "Alex", Now);

        Assert.False(item.AwaitsSchedule);
    }

    // ---------------------------------------------------------------- etapa do plano (item espelho)

    [Fact]
    public void Scheduled_step_is_not_ready_and_the_mirror_task_follows()
    {
        var plan = new ExecutionPlan("78794", "analisar-bug", "Correção", null, Guid.NewGuid(), "Alex", Now, ExecutionPhase.Correction, Guid.NewGuid());
        plan.UpsertSteps([
            new ExecutionStepDefinition { Key = "conferir", Title = "Conferir a geração" },
            new ExecutionStepDefinition { Key = "fechar", Title = "Fechar", DependsOn = ["conferir"] },
            new ExecutionStepDefinition { Key = "avisar", Title = "Avisar" },
        ], Now);
        plan.UpdateStep("conferir", ExecutionStatus.Running, null, null, null, null, null, "claude", Now);
        plan.UpdateStep("conferir", ExecutionStatus.Waiting, "o job roda às 07:15 UTC", null, null, null, null, "claude", Now);

        plan.ScheduleStep("conferir", At, "Alex", Alex, Now);

        var step = plan.Steps.Single(s => s.Key == "conferir");
        Assert.Equal(ExecutionStatus.Pending, step.Status);
        Assert.True(step.Held);
        Assert.Equal(At, step.ScheduledAt);
        Assert.Equal(["avisar"], plan.ReadySteps().Select(s => s.Key));
        Assert.Equal(At, step.ToResponse().ScheduledAt);

        var mirror = new WorkItem("78794", WorkItemType.Fix, "Correção", null, WorkItemOrigin.Correction, null, plan.Id,
            plan.Steps.OrderBy(s => s.Order).Select(s => new WorkTaskInput { Title = s.Title, StepKey = s.Key }).ToList(), Alex, "Alex", Now);
        Assert.True(mirror.SyncStepControl("conferir", step.Held, step.HeldBy, step.DependsOn, Now, step.ScheduledAt, step.ScheduledById));
        var task = mirror.Tasks.Single(t => t.StepKey == "conferir");
        Assert.Equal(At, task.ScheduledAt);

        // Pausar a agendada tira a data; continuar libera.
        plan.HoldStep("conferir", true, "Alex", Now);
        Assert.True(step.Held);
        Assert.Null(step.ScheduledAt);
        plan.HoldStep("conferir", false, "Alex", Now);
        Assert.False(step.Held);
        Assert.Contains("conferir", plan.ReadySteps().Select(s => s.Key));
    }
}
