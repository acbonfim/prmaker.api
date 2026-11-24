using System.Collections;
using System.Reflection;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Card 78723 (segurança): Alex fez a análise; Wagner agiu no plano (respondeu, mandou continuar, executou o item) e a
/// correção rodou na máquina do Alex — PRs e textos no card saíram com o nome do Alex. O executor usa o token, o gh e as
/// integrações do dono da máquina: o pedido tem de ir SEMPRE para a máquina de quem agiu, nunca para a de quem criou o
/// plano ou o item.
/// </summary>
public class ActsAsRequester78723Tests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.UtcNow.AddHours(-3);
    private static readonly Guid Alex = Guid.NewGuid();
    private static readonly Guid Wagner = Guid.NewGuid();
    private static readonly ExecutionActor WagnerOnScreen = new(Wagner, "Wagner Macedo", false);

    private static ExecutionPlan PlanByAlex()
    {
        var plan = new ExecutionPlan("78723", "analisar-bug", "Correção 78723", null, Alex, "Alex Carlos", Created);
        plan.UpsertSteps([new() { Key = "abrir-prs", Title = "Abrir os PRs", Executor = ExecutionExecutor.Claude }], Created);
        return plan;
    }

    private static ExecutionWorker Machine(Guid owner, string name, string host) => new(owner, name, null, host, "macOS", Created);

    [Fact]
    public async Task Action_on_the_screen_runs_on_the_machine_of_who_acted_not_of_the_plan_owner()
    {
        var queue = FakeQueue.Create(Machine(Alex, "Alex Carlos", "mac-alex"), Machine(Wagner, "Wagner Macedo", "mac-wagner"));
        var app = new ExecutionQueueApplication(queue.Repository, Fake<IExecutionPlanRepository>.Create(), Fake<IRealTimeNotifier>.Create());

        var request = await app.PlanChangedAsync(PlanByAlex(), ExecutionRequestSource.UserAction, WagnerOnScreen, true, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal(Wagner, request.OwnerUserId);
        Assert.Equal("Wagner Macedo", request.OwnerName);
        Assert.Equal(Wagner, request.RequestedByUserId);
    }

    [Fact]
    public async Task Who_acted_without_executor_never_falls_back_to_the_plan_owner_machine()
    {
        var queue = FakeQueue.Create(Machine(Alex, "Alex Carlos", "mac-alex"));
        var app = new ExecutionQueueApplication(queue.Repository, Fake<IExecutionPlanRepository>.Create(), Fake<IRealTimeNotifier>.Create());

        var request = await app.PlanChangedAsync(PlanByAlex(), ExecutionRequestSource.Answers, WagnerOnScreen, true, CancellationToken.None);

        Assert.Null(request);
        Assert.DoesNotContain(queue.Added, r => r.OwnerUserId == Alex);
    }

    [Fact]
    public async Task Session_of_the_plan_owner_machine_is_not_targeted_for_someone_else()
    {
        var queue = FakeQueue.Create(Machine(Alex, "Alex Carlos", "mac-alex"), Machine(Wagner, "Wagner Macedo", "mac-wagner"));
        var app = new ExecutionQueueApplication(queue.Repository, Fake<IExecutionPlanRepository>.Create(), Fake<IRealTimeNotifier>.Create());
        var plan = PlanByAlex();
        plan.RegisterSession(Guid.NewGuid().ToString(), "mac-alex", null, Created);

        var request = await app.PlanChangedAsync(plan, ExecutionRequestSource.Resume, WagnerOnScreen, true, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal(Wagner, request.OwnerUserId);
        Assert.Null(request.TargetWorkerId);
    }

    [Fact]
    public async Task Work_item_runs_for_who_asked_even_without_executor_never_for_who_created_it()
    {
        var queue = FakeQueue.Create(Machine(Alex, "Alex Carlos", "mac-alex"));
        var app = new ExecutionQueueApplication(queue.Repository, Fake<IExecutionPlanRepository>.Create(), Fake<IRealTimeNotifier>.Create());
        var item = new WorkItem("78723", WorkItemType.Fix, "Correção", null, WorkItemOrigin.Correction, null, null,
            [new() { Title = "Abrir os PRs", Kind = "pr" }], Alex, "Alex Carlos", Created);

        await app.EnqueueAsync(item, WagnerOnScreen, CancellationToken.None);

        var request = Assert.Single(queue.Added);
        Assert.Equal(Wagner, request.OwnerUserId);
        Assert.Equal("Wagner Macedo", request.OwnerName);
    }

    // ---------------------------------------------------------------- fakes

    /// <summary>Repositório da fila em memória: só as máquinas e os pedidos novos; o resto devolve vazio.</summary>
    private class FakeQueue : DispatchProxy
    {
        public List<ExecutionWorker> Workers { get; private set; } = [];
        public List<ExecutionRequest> Added { get; } = [];
        public IExecutionQueueRepository Repository { get; private set; } = null!;

        public static FakeQueue Create(params ExecutionWorker[] workers)
        {
            var repository = DispatchProxy.Create<IExecutionQueueRepository, FakeQueue>();
            var fake = (FakeQueue)(object)repository;
            fake.Workers = workers.ToList();
            fake.Repository = repository;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IExecutionQueueRepository.AddRequest):
                    Added.Add((ExecutionRequest)args![0]!);
                    return null;
                case nameof(IExecutionQueueRepository.GetWorkersByOwnerAsync):
                    return Task.FromResult(Workers.Where(w => w.OwnerUserId == (Guid)args![0]!).ToList());
                case nameof(IExecutionQueueRepository.GetOwnersWithWorkersAsync):
                    var ids = (IEnumerable<Guid>)args![0]!;
                    return Task.FromResult(ids.Where(id => Workers.Any(w => w.OwnerUserId == id)).ToHashSet());
            }
            return Fake.Default(method.ReturnType);
        }
    }

    private class Fake<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, Fake<T>>();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Fake.Default(method!.ReturnType);
    }

    private static class Fake
    {
        /// <summary>Valor vazio do tipo de retorno (Task concluída, coleção vazia, default).</summary>
        public static object? Default(Type type)
        {
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type == typeof(ValueTask)) return ValueTask.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [Default(inner)]);
            }
            if (type == typeof(string)) return null;
            if (!type.IsInterface && !type.IsAbstract && typeof(IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
                return Activator.CreateInstance(type);
            if (type.IsInterface && type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            {
                var args = type.GetGenericArguments();
                var concrete = args.Length == 2 ? typeof(Dictionary<,>).MakeGenericType(args) : typeof(List<>).MakeGenericType(args);
                return Activator.CreateInstance(concrete);
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
