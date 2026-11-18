using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application;

/// <summary>Um card do histórico: o texto (título + repro normalizados), os refs lidos e os módulos que a análise registrou.</summary>
public sealed record ReverseCardSample(string Card, string Query, IReadOnlyList<string> ReadRefs, IReadOnlyList<string> Modules)
{
    public static ReverseCardSample From(ReverseCardContext c) => new(c.CardNumber, c.Query ?? string.Empty, c.ReadRefs, c.Modules);
}

/// <summary>Card parecido: similaridade (0–1) e os módulos que ele usou (pela fatia dos refs lidos).</summary>
public sealed record ReverseSimilarCard(string Card, double Similarity, IReadOnlyDictionary<string, double> Shares);

/// <summary>Módulo sugerido pelo histórico: soma de similaridade × fatia e quantos cards parecidos o usaram.</summary>
public sealed record ReverseHistoryModule(string Module, double Score, int Cards);

public sealed record ReverseCardHistoryResult(IReadOnlyList<ReverseSimilarCard> Similar, IReadOnlyList<ReverseHistoryModule> Modules)
{
    public static ReverseCardHistoryResult Empty { get; } = new([], []);
    public bool IsEmpty => Modules.Count == 0;

    /// <summary>Linha do contexto da análise: "Pelo histórico (cards parecidos: 77697 62%, 78266 41%): legado-checklist (2 cards), …".</summary>
    public string? Line(int maxCards = 5, int maxModules = 4) => IsEmpty
        ? null
        : $"Pelo histórico (cards parecidos: {string.Join(", ", Similar.Take(maxCards).Select(c => $"{c.Card} {Math.Round(c.Similarity * 100)}%"))}"
          + $"{(Similar.Count > maxCards ? " …" : "")}): "
          + string.Join(", ", Modules.Take(maxModules).Select(m => $"{m.Module} ({m.Cards} {(m.Cards == 1 ? "card" : "cards")})"));
}

/// <summary>
/// 0097: acha o módulo de um card pelos cards parecidos — o campo Module erra ("All", módulo trocado: 77697, 78266, 77698).
/// Puro (testável): termos normalizados como na busca (<see cref="ArchitectureSearch.Terms"/>, mesmas stopwords), peso IDF
/// sobre os textos do histórico, similaridade do cosseno entre os conjuntos de termos; os <see cref="Top"/> mais parecidos
/// com similaridade ≥ <see cref="MinSimilarity"/> votam nos módulos que LERAM (fatia dos refs lidos; sem refs, os módulos
/// registrados em partes iguais).
/// </summary>
public static class ReverseCardHistory
{
    public const int Top = 8;
    public const double MinSimilarity = 0.25;
    private const int MaxTerms = 60;

    /// <summary>Valores do campo Module que não dizem nada ("All", "Todos", "Geral"…): tratar como vazio.</summary>
    public static readonly IReadOnlySet<string> GenericFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "all", "todos", "todas", "geral", "general", "other", "outros", "outro", "n a", "na", "none", "nenhum", ""
    };

    /// <summary>Campo Module vazio ou genérico ("All", "Todos", "Geral", "General", "Other", "Outros", "N/A", "-").</summary>
    public static bool IsGenericField(string? moduleField)
    {
        var letters = new string(ArchitectureSearch.Normalize(moduleField ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var n = string.Join(' ', letters.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return GenericFields.Contains(n);
    }

    /// <summary>Texto do card como fica guardado: sem acento/caixa, espaços simples, até 1000 caracteres.</summary>
    public static string NormalizeQuery(string? query)
    {
        var n = string.Join(' ', ArchitectureSearch.Normalize(query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return n.Length <= ReverseCardContext.MaxQueryLength ? n : n[..ReverseCardContext.MaxQueryLength];
    }

    public static ReverseCardHistoryResult Find(string? query, IEnumerable<ReverseCardSample> history, string? exceptCard = null)
    {
        var mine = Terms(query);
        if (mine.Count == 0) return ReverseCardHistoryResult.Empty;
        var samples = history.Where(h => h.Card != exceptCard && !string.IsNullOrWhiteSpace(h.Query))
            .Select(h => (Sample: h, Terms: Terms(h.Query), Shares: Shares(h))).Where(x => x.Terms.Count > 0 && x.Shares.Count > 0).ToList();
        if (samples.Count == 0) return ReverseCardHistoryResult.Empty;

        // IDF suavizado sobre o histórico: termo raro pesa mais; termo que não aparece no histórico ainda pesa (diminui a
        // similaridade de quem não o tem)
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in samples.SelectMany(s => s.Terms)) df[t] = df.GetValueOrDefault(t) + 1;
        double Idf(string t) => Math.Log((samples.Count + 1.0) / (df.GetValueOrDefault(t) + 1.0)) + 1.0;
        double Norm(HashSet<string> terms) => Math.Sqrt(terms.Sum(t => Idf(t) * Idf(t)));
        var myNorm = Norm(mine);

        var similar = samples
            .Select(s => new ReverseSimilarCard(s.Sample.Card,
                Math.Round(s.Terms.Where(mine.Contains).Sum(t => Idf(t) * Idf(t)) / (myNorm * Norm(s.Terms)), 4), s.Shares))
            .Where(c => c.Similarity >= MinSimilarity)
            .OrderByDescending(c => c.Similarity).ThenBy(c => c.Card, StringComparer.Ordinal)
            .Take(Top).ToList();
        var modules = similar.SelectMany(c => c.Shares.Select(s => (Module: s.Key, Score: c.Similarity * s.Value)))
            .GroupBy(x => x.Module)
            .Select(g => new ReverseHistoryModule(g.Key, Math.Round(g.Sum(x => x.Score), 4), g.Count()))
            .OrderByDescending(m => m.Score).ThenBy(m => m.Module, StringComparer.Ordinal).ToList();
        return new ReverseCardHistoryResult(similar, modules);
    }

    /// <summary>Fatia de cada módulo no que o card leu (<c>módulo#ID</c>, <c>projeto/seção</c>); sem leitura, os módulos registrados.</summary>
    public static IReadOnlyDictionary<string, double> Shares(ReverseCardSample sample)
    {
        var read = sample.ReadRefs.Select(ModuleOf).Where(m => m is not null).Select(m => m!).ToList();
        if (read.Count > 0)
            return read.GroupBy(m => m).ToDictionary(g => g.Key, g => (double)g.Count() / read.Count);
        var modules = sample.Modules.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().ToLowerInvariant()).Distinct().ToList();
        return modules.ToDictionary(m => m, _ => 1.0 / modules.Count);
    }

    /// <summary>Módulo de um ref lido: <c>legado-checklist#RN-012</c> ou <c>legado-checklist/020-modulos</c>; artigo do KC não tem.</summary>
    public static string? ModuleOf(string? reference)
    {
        var r = (reference ?? string.Empty).Trim();
        var cut = r.IndexOfAny(['#', '/']);
        if (cut <= 0) return null;
        var module = r[..cut].Trim().ToLowerInvariant();
        return module.Length == 0 || module.StartsWith("art-", StringComparison.Ordinal) || module.Contains(':') ? null : module;
    }

    private static HashSet<string> Terms(string? text) => ArchitectureSearch.Terms(text, null, MaxTerms).ToHashSet(StringComparer.Ordinal);
}
