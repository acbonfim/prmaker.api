using System.Collections;
using System.Reflection;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0102: pergunta da Base Solvace pelo executor — vai para a máquina de quem perguntou, numa pista própria (não espera a
/// análise que está rodando), só para executor ≥ 1.9.0; o prompt leva a pergunta e o id dela, os anexos vão no claim; a
/// resposta só vale para a pergunta do próprio pedido e o status vem do pedido.
/// </summary>
public class BaseAsk0102Tests
{
    private static readonly Guid Alex = Guid.NewGuid();
    private static readonly Guid Wagner = Guid.NewGuid();
    private static readonly ExecutionActor AlexOnScreen = new(Alex, "Alex Carlos", false);

    private static ExecutionWorker Machine(Guid owner, string agentVersion = "1.9.0", bool online = true)
    {
        var w = new ExecutionWorker(owner, owner == Alex ? "Alex Carlos" : "Wagner Macedo", null, $"mac-{owner:N}"[..12], "macOS", DateTimeOffset.UtcNow.AddDays(-1));
        // o último sinal de vida (Report) decide se está online
        w.Report(agentVersion, "2.1.289", null, null, null, online ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddHours(-2));
        return w;
    }

    private static (ExecutionQueueApplication App, FakeQueue Queue) Create(params ExecutionWorker[] workers)
    {
        var queue = FakeQueue.Create(workers);
        return (new ExecutionQueueApplication(queue.Repository, Fake<IExecutionPlanRepository>.Create(), Fake<IRealTimeNotifier>.Create()), queue);
    }

    [Fact]
    public async Task Question_goes_to_the_machine_of_who_asked_with_its_own_key()
    {
        var (app, queue) = Create(Machine(Alex), Machine(Wagner));

        var q = await app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário para outra planta?", false, [], CancellationToken.None);

        var request = Assert.Single(queue.Requests);
        Assert.Equal(ExecutionRequestKind.BaseAsk, request.Kind);
        Assert.Equal(ExecutionLane.Base, ExecutionLane.Of(request.Kind));
        Assert.Equal(Alex, request.OwnerUserId);
        Assert.Equal(q.Key, request.CardNumber);
        Assert.True(BaseQuestion.IsKey(q.Key));
        Assert.Equal(request.Id, q.RequestId);
        Assert.Equal(ExecutionRequestStatus.Queued, q.Status);
    }

    [Fact]
    public async Task Without_an_online_active_executor_the_question_is_refused_to_offer_the_server_ai()
    {
        var (app, queue) = Create(Machine(Alex, online: false), Machine(Wagner));

        var e = await Assert.ThrowsAsync<BaseAskUnavailableException>(() =>
            app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário?", false, [], CancellationToken.None));

        Assert.Equal(BaseAskUnavailableException.NoExecutor, e.Code);
        Assert.Contains("offline", e.Message);
        Assert.Empty(queue.Requests);
    }

    [Fact]
    public async Task Claim_carries_the_prompt_with_the_question_id_the_files_and_the_phase()
    {
        var alex = Machine(Alex);
        var (app, queue) = Create(alex);
        var files = new List<BaseQuestionFile> { new("erro.png", "image/png", Convert.ToBase64String([1, 2, 3])) };
        var q = await app.AskBaseAsync(AlexOnScreen, "O que é este erro?", true, files, CancellationToken.None);

        var claim = await app.NextAsync(alex.Id, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(ExecutionRequestKind.BaseAsk, claim.Request.Kind);
        Assert.Equal(ExecutionQueueApplication.BaseAskDeepPhase, claim.Phase);
        Assert.Contains(q.Id.ToString(), claim.FreshPrompt);
        Assert.Contains("O que é este erro?", claim.FreshPrompt);
        Assert.Contains("prmake_base_answer", claim.FreshPrompt);
        Assert.Contains("erro.png", claim.FreshPrompt);
        Assert.Contains("A FUNDO", claim.FreshPrompt);
        var file = Assert.Single(claim.Files!);
        Assert.Equal("erro.png", file.Name);
        Assert.Equal(claim.FreshPrompt, claim.ResumePrompt);
    }

    [Fact]
    public async Task Old_executor_does_not_get_the_question_and_the_screen_says_it_is_updating()
    {
        var alex = Machine(Alex, agentVersion: "1.8.2");
        var (app, _) = Create(alex);
        var q = await app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário?", false, [], CancellationToken.None);

        var claim = await app.NextAsync(alex.Id, CancellationToken.None);
        var state = await app.GetBaseQuestionAsync(q.Id, Alex, CancellationToken.None);

        Assert.Null(claim);
        Assert.Equal("updating", state.WaitCode);
        Assert.Contains(ExecutionQueueApplication.BaseAskAgentVersion, state.WaitReason);
    }

    [Fact]
    public async Task Question_does_not_wait_for_the_analysis_running_on_the_machine()
    {
        var alex = Machine(Alex);
        var (app, queue) = Create(alex);
        var analysis = new ExecutionRequest("78000", ExecutionRequestKind.Analyze, ExecutionRequestSource.Button, Alex, "Alex Carlos", Alex,
            "Alex Carlos", null, null, null, null, false, DateTimeOffset.UtcNow.AddMinutes(-5));
        analysis.Claim(alex, DateTimeOffset.UtcNow.AddMinutes(-5));
        queue.Requests.Add(analysis);
        queue.Busy[alex.Id] = 1; // a máquina está no limite da pista principal (1)

        await app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário?", false, [], CancellationToken.None);
        var claim = await app.NextAsync(alex.Id, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(ExecutionRequestKind.BaseAsk, claim.Request.Kind);
    }

    [Fact]
    public async Task Answer_is_accepted_only_for_the_question_of_the_same_request_and_is_shown_as_done()
    {
        var alex = Machine(Alex);
        var (app, _) = Create(alex);
        var q1 = await app.AskBaseAsync(AlexOnScreen, "Pergunta um", false, [new("a.txt", "text/plain", "YQ==")], CancellationToken.None);
        var q2 = await app.AskBaseAsync(AlexOnScreen, "Pergunta dois", false, [], CancellationToken.None);
        var claim = await app.NextAsync(alex.Id, CancellationToken.None);
        Assert.NotNull(claim);
        var (mine, other) = claim.Request.Id == q1.RequestId ? (q1, q2) : (q2, q1);

        await Assert.ThrowsAsync<ExecutionForbiddenException>(() =>
            app.SaveBaseAnswerAsync(claim.Request.Id, other.Id, "{\"answer\":\"x\"}", CancellationToken.None));
        var saved = await app.SaveBaseAnswerAsync(claim.Request.Id, mine.Id, "{\"answer\":\"Passo a passo\",\"coverage\":\"answered\"}", CancellationToken.None);
        var state = await app.GetBaseQuestionAsync(mine.Id, Alex, CancellationToken.None);

        Assert.Null(saved.Files);
        Assert.Equal(ExecutionRequestStatus.Done, state.Status);
        Assert.Equal("Passo a passo", state.Answer!.Value.GetProperty("answer").GetString());
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task Request_done_without_answer_shows_as_failed_and_someone_else_cannot_read_the_question()
    {
        var alex = Machine(Alex);
        var (app, _) = Create(alex);
        var q = await app.AskBaseAsync(AlexOnScreen, "Pergunta", false, [], CancellationToken.None);
        var claim = await app.NextAsync(alex.Id, CancellationToken.None);
        await app.FinishAsync(claim!.Request.Id, alex.Id, new solvace.executionplans.domain.Requests.FinishExecutionRequestRequest { Outcome = "done" }, CancellationToken.None);

        var state = await app.GetBaseQuestionAsync(q.Id, Alex, CancellationToken.None);

        Assert.Equal(ExecutionRequestStatus.Failed, state.Status);
        Assert.Contains("sem gravar a resposta", state.Error);
        await Assert.ThrowsAsync<ExecutionPlanNotFoundException>(() => app.GetBaseQuestionAsync(q.Id, Wagner, CancellationToken.None));
    }

    [Fact]
    public void Prompt_without_deep_keeps_the_claude_away_from_the_code()
    {
        var q = new BaseQuestion(Alex, "Alex Carlos", "Quem pode aprovar uma ação?", false, null, DateTimeOffset.UtcNow);

        var prompt = ExecutionQueueApplication.BaseAskPrompt(q, []);

        Assert.Contains("Nao leia codigo", prompt);
        Assert.DoesNotContain("PRMAKE_FILES_DIR", prompt);
        Assert.Contains("Quem pode aprovar uma ação?", prompt);
    }

    [Fact]
    public void Search_done_by_the_server_goes_in_the_prompt_and_the_claude_skips_it()
    {
        var hints = "Engenharia reversa (2):\n- revamp-actionplan#RN-031 [Regra] Concluir plano de ação";
        var q = new BaseQuestion(Alex, "Alex Carlos", "O que acontece quando um plano de ação é concluído?", false, null, DateTimeOffset.UtcNow, hints);

        var prompt = ExecutionQueueApplication.BaseAskPrompt(q, []);

        Assert.Contains("BUSCA JA FEITA", prompt);
        Assert.Contains("revamp-actionplan#RN-031", prompt);
        Assert.Contains("DIRETO ao prmake_base_get", prompt);
        Assert.DoesNotContain("BUSCA JA FEITA", ExecutionQueueApplication.BaseAskPrompt(
            new BaseQuestion(Alex, "Alex Carlos", "Outra pergunta?", false, null, DateTimeOffset.UtcNow), []));
    }

    [Fact]
    public async Task Creating_the_question_does_not_run_the_wait_diagnosis()
    {
        var (app, queue) = Create(Machine(Alex));

        await app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário?", false, [], CancellationToken.None);

        // só a leitura das máquinas de quem perguntou (o diagnóstico de espera fica para o GET)
        Assert.Equal(0, queue.Calls(nameof(IExecutionQueueRepository.CountActiveByWorkerAsync)));
        Assert.Equal(0, queue.Calls(nameof(IExecutionQueueRepository.GetQueuedForOwnerAsync)));
    }

    [Fact]
    public async Task Executor_asks_cheaply_if_there_is_something_to_do()
    {
        var alex = Machine(Alex);
        var (app, _) = Create(alex);
        Assert.False(await app.IsQueueReadyAsync(alex.Id, CancellationToken.None));

        await app.AskBaseAsync(AlexOnScreen, "Como transfiro um usuário?", false, [], CancellationToken.None);

        Assert.True(await app.IsQueueReadyAsync(alex.Id, CancellationToken.None));
        alex.Pause(DateTimeOffset.UtcNow);
        Assert.False(await app.IsQueueReadyAsync(alex.Id, CancellationToken.None));
    }

    private class FakeQueue : DispatchProxy
    {
        public List<ExecutionWorker> Workers { get; private set; } = [];
        public List<ExecutionRequest> Requests { get; } = [];
        public List<BaseQuestion> Questions { get; } = [];
        public Dictionary<Guid, int> Busy { get; } = [];
        public IExecutionQueueRepository Repository { get; private set; } = null!;

        public static FakeQueue Create(params ExecutionWorker[] workers)
        {
            var repository = DispatchProxy.Create<IExecutionQueueRepository, FakeQueue>();
            var fake = (FakeQueue)(object)repository;
            fake.Workers = workers.ToList();
            fake.Repository = repository;
            return fake;
        }

        private Dictionary<Guid, int> CountBusy(Guid owner, Func<ExecutionRequest, bool> kind) =>
            Requests.Where(r => r.OwnerUserId == owner && r.WorkerId is not null && kind(r)
                                && r.Status is ExecutionRequestStatus.Claimed or ExecutionRequestStatus.Running)
                .GroupBy(r => r.WorkerId!.Value).ToDictionary(g => g.Key, g => g.Count());

        private readonly Dictionary<string, int> _calls = [];
        public int Calls(string method) => _calls.GetValueOrDefault(method);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            _calls[method!.Name] = _calls.GetValueOrDefault(method.Name) + 1;
            switch (method.Name)
            {
                case nameof(IExecutionQueueRepository.AddRequest):
                    Requests.Add((ExecutionRequest)args![0]!);
                    return null;
                case nameof(IExecutionQueueRepository.AddBaseQuestion):
                    Questions.Add((BaseQuestion)args![0]!);
                    return null;
                case nameof(IExecutionQueueRepository.GetRequestAsync):
                    return Task.FromResult(Requests.FirstOrDefault(r => r.Id == (Guid)args![0]!));
                case nameof(IExecutionQueueRepository.GetBaseQuestionAsync):
                    return Task.FromResult(Questions.FirstOrDefault(q => q.Id == (Guid)args![0]!));
                case nameof(IExecutionQueueRepository.GetBaseQuestionByRequestAsync):
                    return Task.FromResult(Questions.FirstOrDefault(q => q.RequestId == (Guid)args![0]!));
                case nameof(IExecutionQueueRepository.GetWorkersByOwnerAsync):
                    return Task.FromResult(Workers.Where(w => w.OwnerUserId == (Guid)args![0]!).ToList());
                case nameof(IExecutionQueueRepository.GetWorkerAsync):
                    return Task.FromResult(Workers.FirstOrDefault(w => w.Id == (Guid)args![0]!));
                case nameof(IExecutionQueueRepository.GetQueuedForOwnerAsync):
                    return Task.FromResult(Requests.Where(r => r.OwnerUserId == (Guid)args![0]! && r.Status == ExecutionRequestStatus.Queued)
                        .OrderBy(r => r.CreatedAt).ToList());
                case nameof(IExecutionQueueRepository.CountActiveByWorkerAsync):
                    var main = CountBusy((Guid)args![0]!, r => ExecutionLane.Of(r.Kind) == ExecutionLane.Main);
                    foreach (var (k, v) in Busy) main[k] = Math.Max(main.GetValueOrDefault(k), v);
                    return Task.FromResult(main);
                case nameof(IExecutionQueueRepository.HasQueuedReadyAsync):
                    return Task.FromResult(Requests.Any(r => r.OwnerUserId == (Guid)args![0]! && r.Status == ExecutionRequestStatus.Queued
                                                             && (r.NotBefore is null || r.NotBefore <= (DateTimeOffset)args[1]!)));
                case nameof(IExecutionQueueRepository.CountActiveBaseAsksByWorkerAsync):
                    return Task.FromResult(CountBusy((Guid)args![0]!, r => r.Kind == ExecutionRequestKind.BaseAsk));
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
