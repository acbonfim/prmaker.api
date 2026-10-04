namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0055: de onde a sessão LEU — engenharia reversa, base antiga/KC, código (confirmação × exploração) e buscas no
/// código — com as chamadas e os tokens estimados do que entrou no contexto (tamanho do resultado ÷ 4). Acumulado do
/// transcript, como os totais da sessão.
/// </summary>
public class ExecutionReadSource
{
    public const int MaxKeyLength = 20;
    public const string Reverse = "re";
    public const string Base = "base";
    public const string CodeConfirm = "code-confirm";
    public const string CodeExplore = "code-explore";
    public const string CodeSearch = "code-search";
    public static readonly IReadOnlyList<string> Keys = [Reverse, Base, CodeConfirm, CodeExplore, CodeSearch];

    public string Key { get; set; } = string.Empty;
    public int Calls { get; set; }
    public long Tokens { get; set; }

    public bool IsEmpty => Calls == 0 && Tokens == 0;

    public ExecutionReadSource Copy() => new() { Key = Key, Calls = Calls, Tokens = Tokens };

    public ExecutionReadSource Minus(ExecutionReadSource? baseline) => baseline is null ? Copy() : new()
    {
        Key = Key, Calls = Math.Max(0, Calls - baseline.Calls), Tokens = Math.Max(0, Tokens - baseline.Tokens)
    };

    /// <summary>Soma por origem, na ordem fixa (engenharia reversa primeiro); origem desconhecida fica de fora.</summary>
    public static List<ExecutionReadSource> Sum(IEnumerable<ExecutionReadSource> items)
    {
        var groups = items.Where(s => Keys.Contains(s.Key)).GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.ToList());
        return Keys.Where(groups.ContainsKey)
            .Select(k => new ExecutionReadSource { Key = k, Calls = groups[k].Sum(s => s.Calls), Tokens = groups[k].Sum(s => s.Tokens) })
            .Where(s => !s.IsEmpty)
            .ToList();
    }

    /// <summary>Fração dos tokens lidos que veio da engenharia reversa (null = nada lido).</summary>
    public static double? ReverseShare(IReadOnlyCollection<ExecutionReadSource> sources)
    {
        var total = sources.Sum(s => s.Tokens);
        return total <= 0 ? null : (double)sources.Where(s => s.Key == Reverse).Sum(s => s.Tokens) / total;
    }
}

/// <summary>0055: arquivo de código lido sem item da engenharia reversa que o cite — candidato a lacuna.</summary>
public class ExecutionExploredFile
{
    public const int MaxPathLength = 200;
    public const int MaxFiles = 10;

    public string Path { get; set; } = string.Empty;
    public int Reads { get; set; }
    public long Tokens { get; set; }

    public ExecutionExploredFile Copy() => new() { Path = Path, Reads = Reads, Tokens = Tokens };

    public ExecutionExploredFile Minus(ExecutionExploredFile? baseline) => baseline is null ? Copy() : new()
    {
        Path = Path, Reads = Math.Max(0, Reads - baseline.Reads), Tokens = Math.Max(0, Tokens - baseline.Tokens)
    };

    /// <summary>Soma por arquivo e fica com os que mais pesaram.</summary>
    public static List<ExecutionExploredFile> Top(IEnumerable<ExecutionExploredFile> items, int max = MaxFiles) => items
        .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
        .Select(g => new ExecutionExploredFile { Path = g.First().Path, Reads = g.Sum(f => f.Reads), Tokens = g.Sum(f => f.Tokens) })
        .Where(f => f.Reads > 0 || f.Tokens > 0)
        .OrderByDescending(f => f.Tokens).ThenBy(f => f.Path)
        .Take(max)
        .ToList();
}
