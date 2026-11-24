using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0099 (ajustes 2): histórias e bugs do épico — dados, número da sequência, conversão única (409 alreadyConverted / converting),
/// não editável depois de convertida (409 notEditable), descartar e o contexto do especialista (rascunhos × convertidas).
/// </summary>
public class EpicStory0099Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T12:00:00Z");
    private static readonly ExecutionActor Alex = new(Guid.NewGuid(), "Alex", false);

    private static EpicStory New(string? type = "user-story", string title = "Exportar relatório 5S", string? status = null,
        IReadOnlyDictionary<string, JsonElement>? fields = null) =>
        new(Guid.NewGuid(), 10, type, title, "Como gestor quero exportar", "- exporta em PDF", null, 2, null, ["5S", " 5s "], fields, status,
            Guid.NewGuid(), "Alex", Now);

    // ── domínio ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "user-story")]
    [InlineData("User Story", "user-story")]
    [InlineData("us", "user-story")]
    [InlineData("Bug", "bug")]
    public void Type_is_normalized(string? given, string expected) => Assert.Equal(expected, EpicStoryType.Normalize(given));

    [Fact]
    public void Type_rejects_other_kinds() => Assert.Throws<DomainException>(() => EpicStoryType.Normalize("Feature"));

    [Fact]
    public void Creates_draft_with_clean_tags_and_fields()
    {
        var s = New(fields: new Dictionary<string, JsonElement>
        {
            ["System.AreaPath"] = JsonSerializer.SerializeToElement(@"Solvace\Revamp"),
            [" System.AssignedTo "] = JsonSerializer.SerializeToElement("alex@solvace.com"),
            ["Custom.Empty"] = JsonSerializer.SerializeToElement(""),
            ["Custom.Null"] = JsonSerializer.SerializeToElement<string?>(null),
            ["Microsoft.VSTS.Scheduling.StoryPoints"] = JsonSerializer.SerializeToElement(3)
        });
        Assert.Equal(EpicStoryStatus.Draft, s.Status);
        Assert.Equal(["5S"], s.Tags);
        Assert.Equal(3, s.Fields.Count);
        Assert.Equal("alex@solvace.com", s.Fields["System.AssignedTo"].GetString());
        Assert.Equal(3, s.Fields["Microsoft.VSTS.Scheduling.StoryPoints"].GetInt32());
        Assert.False(s.IsConverted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string title) => Assert.Throws<DomainException>(() => New(title: title));

    [Fact]
    public void Priority_and_field_shapes_are_validated()
    {
        Assert.Throws<DomainException>(() => new EpicStory(Guid.NewGuid(), 1, "bug", "x", null, null, null, 0, null, null, null, null, null, "A", Now));
        Assert.Throws<DomainException>(() => New(fields: new Dictionary<string, JsonElement>
        {
            ["System.Tags"] = JsonSerializer.SerializeToElement(new[] { "a" })
        }));
    }

    [Fact]
    public void Status_converted_is_only_set_by_the_conversion()
    {
        Assert.Throws<DomainException>(() => New(status: "converted"));
        var s = New(status: "ready");
        Assert.Equal(EpicStoryStatus.Ready, s.Status);
    }

    [Fact]
    public void Number_is_assigned_once()
    {
        var s = New();
        s.AssignNumber(77);
        Assert.Equal(77, s.Number);
        Assert.Throws<InvalidOperationException>(() => s.AssignNumber(78));
    }

    [Fact]
    public void Conversion_happens_once_and_locks_editing()
    {
        var s = New();
        s.AssignNumber(77);
        s.BeginConversion(Now);
        Assert.True(s.IsConverting(Now));
        var during = Assert.Throws<EpicStoryConflictException>(() => s.BeginConversion(Now.AddSeconds(5)));
        Assert.Equal(EpicStoryConflictException.Converting, during.Code);
        Assert.Equal(EpicStoryConflictException.Converting,
            Assert.Throws<EpicStoryConflictException>(() => s.Update(null, "x", null, null, null, null, null, null, null, null, Now)).Code);

        s.CompleteConversion("#78123", "Alex", Now.AddSeconds(10));
        Assert.True(s.IsConverted);
        Assert.Equal("78123", s.CardId);
        Assert.Null(s.ConvertingAt);

        var again = Assert.Throws<EpicStoryConflictException>(() => s.BeginConversion(Now.AddMinutes(1)));
        Assert.Equal(EpicStoryConflictException.AlreadyConverted, again.Code);
        Assert.Equal("78123", again.CardId);

        var edit = Assert.Throws<EpicStoryConflictException>(() => s.Update("bug", "Outro", null, null, null, null, null, null, null, null, Now));
        Assert.Equal(EpicStoryConflictException.NotEditable, edit.Code);
        Assert.Equal(EpicStoryConflictException.NotEditable, Assert.Throws<EpicStoryConflictException>(() => s.Discard(Now)).Code);
    }

    [Fact]
    public void Stale_conversion_lock_expires_and_abort_releases()
    {
        var s = New();
        s.BeginConversion(Now);
        s.BeginConversion(Now + EpicStory.ConversionLock + TimeSpan.FromSeconds(1)); // trava velha (servidor caiu) não segura
        s.AbortConversion();
        Assert.Null(s.ConvertingAt);
        s.Update(null, "Título novo", null, null, null, null, null, null, null, null, Now);
        Assert.Equal("Título novo", s.Title);
    }

    [Fact]
    public void Discarded_does_not_convert_until_back_to_draft()
    {
        var s = New();
        Assert.True(s.Discard(Now));
        Assert.False(s.Discard(Now));
        Assert.Throws<DomainException>(() => s.BeginConversion(Now));
        s.Update(null, s.Title, null, null, null, null, null, null, null, "draft", Now);
        s.BeginConversion(Now);
        Assert.True(s.IsConverting(Now));
    }

    [Fact]
    public void Status_filter_defaults_to_not_discarded()
    {
        Assert.Equal([EpicStoryStatus.Draft, EpicStoryStatus.Ready, EpicStoryStatus.Converted], EpicStoryStatus.ParseFilter(null));
        Assert.Null(EpicStoryStatus.ParseFilter("all"));
        Assert.Equal([EpicStoryStatus.Discarded], EpicStoryStatus.ParseFilter("discarded"));
        Assert.Throws<DomainException>(() => EpicStoryStatus.ParseFilter("feito"));
    }

    // ── aplicação (repositório em memória) ───────────────────────────────────────────────────

    [Fact]
    public async Task Create_numbers_in_order_and_resolves_by_number()
    {
        var (app, repo, _) = App();
        var a = await app.CreateAsync(5, new SaveEpicStoryRequest { Title = "A" }, Alex, default);
        var b = await app.CreateAsync(5, new SaveEpicStoryRequest { Title = "B", Type = "Bug", ReproSteps = "1. abrir" }, Alex, default);
        Assert.True(b.Number > a.Number);
        Assert.Equal("E5", a.EpicKey);
        Assert.Equal(EpicStoryType.Bug, b.Type);
        Assert.True(a.Editable);
        Assert.Equal(b.Number, (await app.FindAsync(b.Number, default))!.Number);
        Assert.Null(await app.FindAsync(999_999, default));
        Assert.Equal([b.Number, a.Number], (await app.ListAsync(5, null, default)).Select(s => s.Number));
        Assert.Equal(2, repo.Saves);
    }

    [Fact]
    public async Task Create_in_unknown_epic_is_404() =>
        await Assert.ThrowsAsync<ExecutionPlanNotFoundException>(() => NewApp().CreateAsync(404, new SaveEpicStoryRequest { Title = "A" }, Alex, default));

    [Fact]
    public async Task Convert_once_then_409_and_not_editable()
    {
        var (app, _, rt) = App();
        var s = await app.CreateAsync(5, new SaveEpicStoryRequest { Title = "A", AcceptanceCriteria = "- ok" }, Alex, default);

        var locked = await app.BeginConversionAsync(s.Number, Alex, default);
        Assert.True(locked.Converting);
        Assert.False(locked.Editable);
        Assert.Equal(EpicStoryConflictException.Converting,
            (await Assert.ThrowsAsync<EpicStoryConflictException>(() => app.BeginConversionAsync(s.Number, Alex, default))).Code);

        var done = await app.CompleteConversionAsync(s.Number, "78123", Alex, default);
        Assert.Equal(EpicStoryStatus.Converted, done.Status);
        Assert.Equal("78123", done.CardId);
        Assert.Equal("Alex", done.ConvertedByName);
        Assert.False(done.Converting);

        var again = await Assert.ThrowsAsync<EpicStoryConflictException>(() => app.BeginConversionAsync(s.Number, Alex, default));
        Assert.Equal(EpicStoryConflictException.AlreadyConverted, again.Code);
        Assert.Equal("78123", again.CardId);
        Assert.Equal(EpicStoryConflictException.NotEditable,
            (await Assert.ThrowsAsync<EpicStoryConflictException>(() => app.UpdateAsync(s.Number, new SaveEpicStoryRequest { Title = "B" }, Alex, default))).Code);
        Assert.Equal(EpicStoryConflictException.NotEditable,
            (await Assert.ThrowsAsync<EpicStoryConflictException>(() => app.DiscardAsync(s.Number, Alex, default))).Code);
        Assert.Contains(rt.Events, e => e.Contains("\"action\":\"stories\""));
    }

    [Fact]
    public async Task Failed_conversion_releases_the_lock()
    {
        var (app, _, _) = App();
        var s = await app.CreateAsync(5, new SaveEpicStoryRequest { Title = "A" }, Alex, default);
        await app.BeginConversionAsync(s.Number, Alex, default);
        await app.AbortConversionAsync(s.Number, default);
        var after = await app.GetAsync(s.Number, default);
        Assert.False(after.Converting);
        Assert.True(after.Editable);
        Assert.Equal(EpicStoryStatus.Draft, after.Status);
    }

    [Fact]
    public async Task Discard_hides_from_default_list_and_put_clears_fields()
    {
        var (app, _, _) = App();
        var s = await app.CreateAsync(5, new SaveEpicStoryRequest { Title = "A", Description = "d", Priority = 1, Tags = ["x"] }, Alex, default);
        var edited = await app.UpdateAsync(s.Number, new SaveEpicStoryRequest { Title = "A2" }, Alex, default);
        Assert.Null(edited.Description);
        Assert.Null(edited.Priority);
        Assert.Empty(edited.Tags);
        Assert.Equal(EpicStoryType.UserStory, edited.Type);

        await app.DiscardAsync(s.Number, Alex, default);
        Assert.Empty(await app.ListAsync(5, null, default));
        Assert.Single(await app.ListAsync(5, "all", default));
    }

    [Fact]
    public void Context_lists_drafts_and_converted()
    {
        var draft = New(title: "Rascunho");
        draft.AssignNumber(11);
        var bug = New(type: "bug", title: "Bug sem passos");
        bug.AssignNumber(12);
        var conv = New(title: "Já é card");
        conv.AssignNumber(13);
        conv.BeginConversion(Now);
        conv.CompleteConversion("78123", "Alex", Now);
        var gone = New(title: "Descartada");
        gone.AssignNumber(14);
        gone.Discard(Now);

        var md = EpicStoryApplication.ContextMarkdown(10, [draft, bug, conv, gone], Now);
        Assert.Contains("2 rascunho(s), 1 convertida(s)", md);
        Assert.Contains("#11 [US] Rascunho", md);
        Assert.Contains("#12 [Bug] Bug sem passos", md);
        Assert.Contains("sem passos de reprodução", md);
        Assert.Contains("#13 [US] Já é card → card 78123", md);
        Assert.Contains("Descartadas: 1", md);
        Assert.DoesNotContain("#14", md);
        Assert.Contains("SÓ quando a pessoa pedir", md);
    }

    // ── fakes ────────────────────────────────────────────────────────────────────────────────

    private static EpicStoryApplication NewApp() => App().App;

    private static (EpicStoryApplication App, StoryRepo Repo, Notifier Rt) App()
    {
        var epic = new Epic("Épico", null, null, null, null, null, null, null, "Alex", Now);
        epic.AssignNumber(5);
        var repo = new StoryRepo();
        var rt = new Notifier();
        return (new EpicStoryApplication(repo, new EpicRepo(epic), rt), repo, rt);
    }

    private sealed class Notifier : IRealTimeNotifier
    {
        public List<string> Events { get; } = [];

        public Task NotifyGroupAsync(string group, string eventName, object? payload = null, CancellationToken cancellationToken = default)
        {
            Events.Add($"{group}/{eventName}/{JsonSerializer.Serialize(payload)}");
            return Task.CompletedTask;
        }

        public Task NotifyAllAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Repositório em memória: numera os novos pela "sequência" na ordem de criação, como o real.</summary>
    private sealed class StoryRepo : IEpicStoryRepository
    {
        private readonly List<EpicStory> _all = [];
        private readonly List<EpicStory> _added = [];
        private long _seq = 100;
        public int Saves { get; private set; }

        public void Add(EpicStory story) => _added.Add(story);
        public Task<EpicStory?> GetByNumberAsync(long number, CancellationToken ct) => Task.FromResult(_all.FirstOrDefault(s => s.Number == number));
        public Task<EpicStory?> FindByNumberAsync(long number, CancellationToken ct) => GetByNumberAsync(number, ct);

        public Task<List<EpicStory>> ListByEpicAsync(Guid epicId, IReadOnlyCollection<string>? statuses, CancellationToken ct) =>
            Task.FromResult(_all.Where(s => s.EpicId == epicId && (statuses is null || statuses.Contains(s.Status))).OrderByDescending(s => s.Number).ToList());

        public Task SaveChangesAsync(CancellationToken ct)
        {
            foreach (var s in _added.OrderBy(s => s.CreatedAt)) s.AssignNumber(++_seq);
            _all.AddRange(_added);
            _added.Clear();
            Saves++;
            return Task.CompletedTask;
        }

        public void Detach() { }
    }

    private sealed class EpicRepo(Epic epic) : IEpicRepository
    {
        public Task<List<Epic>> ListByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct) =>
            Task.FromResult(numbers.Contains(epic.Number) ? new List<Epic> { epic } : []);

        public void Add(Epic e) => throw new NotSupportedException();
        public Task<Epic?> GetByNumberAsync(long number, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Epic>> ListAsync(IReadOnlyCollection<string>? statuses, string? query, Guid? createdBy, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Epic>> GetByNumbersAsync(IReadOnlyCollection<long> numbers, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<string>> ListTagsJsonAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Epic>> ListByCardAsync(string card, CancellationToken ct) => throw new NotSupportedException();
        public Task<Dictionary<Guid, EpicCounts>> CountsAsync(IReadOnlyCollection<Epic> epics, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Detach() { }
    }
}
