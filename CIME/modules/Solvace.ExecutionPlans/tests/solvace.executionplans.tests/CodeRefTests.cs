using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0075: fotografia do código citado — validações e a ligação das citações caminho:linha.</summary>
public class CodeRefTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");

    private const string Snapshot = """
        function init() {
          restoreFilters();
          $('#periodo').daterangepicker({ locale: { format: 'DD/MM/YYYY' } });
        }
        """;

    private const string Proposal = """
        @@ -240,4 +240,4 @@
         function init() {
        -  restoreFilters();
           $('#periodo').daterangepicker({ locale: { format: 'DD/MM/YYYY' } });
        +  restoreFilters();
         }
        """;

    private static ExecutionCodeCapture Capture(string? change = ExecutionCodeChange.Change, string? why = "Restaura antes do seletor existir.",
        string? proposal = Proposal, int start = 241, int? end = 242, IReadOnlyList<ExecutionCodeAnnotation>? notes = null) =>
        new("edv-solvace", "solvace-asp/systems/sa3/sa3_busca_a3.asp", "abc123", "master", "asp",
            start, end, 240, Snapshot, change, "Ordem da inicialização", why, proposal, notes);

    private static ExecutionCodeRef New(string reference = "sa3-init") => new(Guid.NewGuid(), "75812", reference, "claude", Now);

    [Fact]
    public void Capture_keeps_the_snapshot_focus_and_proposal()
    {
        var r = New();
        r.Capture(Capture(notes: [new ExecutionCodeAnnotation { Side = "OLD", From = 241, Text = "Lê o filtro antes do seletor." }]), Now);

        var response = r.ToResponse();
        Assert.Equal("solvace-asp/systems/sa3/sa3_busca_a3.asp", response.Path);
        Assert.Equal((241, 242), (response.StartLine, response.EndLine));
        Assert.Equal(240, response.FirstLine);
        Assert.True(response.HasProposal);
        var note = Assert.Single(response.Annotations);
        Assert.Equal(("old", 241, 241), (note.Side, note.From, note.To));
    }

    [Theory]
    [InlineData("Sa3-Init", "sa3-init")]
    [InlineData(" restore_date.v2 ", "restore_date.v2")]
    public void Ref_is_normalized(string given, string expected) => Assert.Equal(expected, ExecutionCodeRef.NormalizeRef(given));

    [Theory]
    [InlineData("")]
    [InlineData("-começa-com-traço")]
    [InlineData("tem espaço")]
    [InlineData("ref\"><script>")]
    public void Invalid_ref_is_rejected(string given) => Assert.Throws<DomainException>(() => ExecutionCodeRef.NormalizeRef(given));

    [Fact]
    public void Change_without_why_or_proposal_is_rejected()
    {
        Assert.Throws<DomainException>(() => New().Capture(Capture(why: null), Now));
        Assert.Throws<DomainException>(() => New().Capture(Capture(proposal: null), Now));
    }

    [Fact]
    public void Read_and_suspect_need_neither_why_nor_proposal()
    {
        New().Capture(Capture(ExecutionCodeChange.Read, why: null, proposal: null), Now);
        New().Capture(Capture(ExecutionCodeChange.Suspect, why: null, proposal: null), Now);
    }

    [Fact]
    public void Focus_or_old_note_outside_the_snapshot_is_rejected()
    {
        Assert.Throws<DomainException>(() => New().Capture(Capture(start: 250, end: 251), Now));
        Assert.Throws<DomainException>(() => New().Capture(Capture(notes: [new ExecutionCodeAnnotation { Side = "old", From = 300, Text = "x" }]), Now));
        // nota do lado novo usa a numeração do arquivo proposto: não é conferida contra a fotografia
        New().Capture(Capture(notes: [new ExecutionCodeAnnotation { Side = "new", From = 300, Text = "x" }]), Now);
    }

    [Fact]
    public void Snapshot_over_the_line_limit_is_rejected()
    {
        var big = string.Join('\n', Enumerable.Range(1, ExecutionCodeRef.MaxSnapshotLines + 1).Select(i => $"linha {i}"));
        var capture = Capture(ExecutionCodeChange.Read, why: null, proposal: null, start: 1, end: 1) with { Snapshot = big, FirstLine = 1 };
        Assert.Throws<DomainException>(() => New().Capture(capture, Now));
    }

    [Fact]
    public void Create_needs_no_snapshot()
    {
        var r = New("novo-helper");
        r.Capture(new ExecutionCodeCapture("edv-solvace", "solvace-core/js/dateFilter.js", null, null, null, 1, 12, null, null,
            ExecutionCodeChange.Create, null, "Helper novo para restaurar datas com formato.", "@@ -0,0 +1,2 @@\n+export const x = 1;\n+export const y = 2;\n", null), Now);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void Recapture_replaces_everything_and_clears_the_implemented_diff()
    {
        var r = New();
        r.Capture(Capture(), Now);
        r.Apply(new ExecutionCodeRefPatch(Implemented: "@@ diff real @@", ImplementedSha: "def456"), Now);
        Assert.NotNull(r.Implemented);

        r.Capture(Capture(ExecutionCodeChange.Read, why: null, proposal: null), Now);
        Assert.Null(r.Implemented);
        Assert.Null(r.Proposal);
        Assert.Null(r.Why);
        Assert.Equal(ExecutionCodeChange.Read, r.Change);
    }

    [Fact]
    public void Patch_changes_only_what_was_sent()
    {
        var r = New();
        r.Capture(Capture(), Now);
        r.Apply(new ExecutionCodeRefPatch(Why: "Novo porquê."), Now);

        Assert.Equal("Novo porquê.", r.Why);
        Assert.Equal(ExecutionCodeChange.Change, r.Change);
        Assert.NotNull(r.Proposal);
    }

    [Theory]
    [InlineData("solvace-asp/systems/sa3/sa3_busca_a3.asp", 239, 289, true)]
    [InlineData("systems/sa3/sa3_busca_a3.asp", 242, 242, true)]
    [InlineData("sa3_busca_a3.asp", 241, 241, true)]
    [InlineData("edv-solvace/solvace-asp/systems/sa3/sa3_busca_a3.asp", 241, 241, true)]
    [InlineData("solvace-asp/systems/sa3/sa3_busca_a3.asp", 300, 310, false)]
    [InlineData("a3.asp", 241, 241, false)]
    [InlineData("solvace-asp/systems/sa3/outra.asp", 241, 241, false)]
    public void Citation_matches_by_path_suffix_and_overlapping_lines(string path, int start, int end, bool expected)
    {
        var r = New();
        r.Capture(Capture(), Now);
        Assert.Equal(expected, r.Matches(path, start, end));
    }

    [Fact]
    public void Card_level_ref_has_no_plan()
    {
        var r = new ExecutionCodeRef(null, "78161", "gs-096-legado", "claude", Now);
        r.Capture(Capture(ExecutionCodeChange.Read, why: null, proposal: null), Now);

        var response = r.ToResponse();
        Assert.Null(response.PlanId);
        Assert.Equal("78161", response.CardNumber);
        Assert.Equal("gs-096-legado", response.Ref);
    }

    [Fact]
    public void Card_without_plan_writes_the_card_level_ref() => Assert.Null(ExecutionCodeRef.PlanForCard([]));

    [Fact]
    public void Card_plan_is_the_newest_open_one_or_else_the_newest()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Assert.Equal(b, ExecutionCodeRef.PlanForCard([(a, ExecutionStatus.Completed), (b, ExecutionStatus.Paused), (c, ExecutionStatus.Running)]));
        Assert.Equal(a, ExecutionCodeRef.PlanForCard([(a, ExecutionStatus.Completed), (b, ExecutionStatus.Cancelled)]));
    }
}
