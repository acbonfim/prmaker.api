using System.Text;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Custo do especialista (card 78161: 110 itens viravam ~80 KB de contexto relido em toda chamada) e confirmação de
/// leitura da conversa (✓✓).
/// </summary>
public class CaseChatCostAndReadsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    private static WorkItem Item(string title) =>
        new("78161", WorkItemType.Bug, title, null, WorkItemOrigin.User, null, null,
            [new WorkTaskInput { Title = "Analisar " + title, Kind = WorkTaskKind.Manual }], null, "Alex", Now);

    private static WorkItem Cancelled(string title)
    {
        var item = Item(title);
        item.Cancel("não precisa", "Alex", Now);
        return item;
    }

    private static string Compact(IReadOnlyList<WorkItem> items)
    {
        var sb = new StringBuilder();
        WorkItemSummary.AppendCompact(sb, items);
        return sb.ToString();
    }

    [Fact]
    public void Compact_summary_lists_only_the_recent_items_and_counts_the_rest()
    {
        var items = Enumerable.Range(1, 30).Select(i => Item($"GS-{i:000}")).ToList();
        items.AddRange(Enumerable.Range(1, 12).Select(i => Cancelled($"Antigo-{i:00}")));

        var text = Compact(items);

        Assert.Contains("0 em andamento · 30 pendentes · 12 fechados", text);
        Assert.Equal(WorkItemSummary.PendingItems + WorkItemSummary.ClosedItems, text.Split('\n').Count(l => l.StartsWith("- **#")));
        Assert.Contains("+ 7 itens fora deste resumo", text);
        // Pendente não traz as sub-tarefas (o detalhe é prmake_work_item).
        Assert.DoesNotContain("Analisar GS-", text);
    }

    [Fact]
    public void Compact_summary_of_a_big_card_stays_small()
    {
        var items = Enumerable.Range(1, 110).Select(i => Item($"Gap GS-{i:000} com um título razoavelmente comprido como os reais")).ToList();
        var text = Compact(items);
        Assert.True(text.Length < 8_000, $"resumo compacto com {text.Length} caracteres");
    }

    [Fact]
    public void Compact_summary_without_hidden_items_has_no_overflow_line() =>
        Assert.DoesNotContain("fora deste resumo", Compact([Item("Único")]));

    [Theory]
    [InlineData(null, "Usuário")]
    [InlineData("  Alex  ", "Alex")]
    public void Reader_normalizes_name(string? name, string expected) =>
        Assert.Equal(expected, new CaseReader("#78161", Guid.NewGuid(), name, 3, Now).Name);

    [Fact]
    public void Reader_normalizes_card_and_never_goes_negative()
    {
        var r = new CaseReader(" #78161 ", Guid.NewGuid(), "Alex", -4, Now);
        Assert.Equal("78161", r.CardNumber);
        Assert.Equal(0, r.LastSeenNumber);
    }
}
