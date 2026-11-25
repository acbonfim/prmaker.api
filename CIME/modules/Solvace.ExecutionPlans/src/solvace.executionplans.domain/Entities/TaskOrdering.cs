namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Card 78680: para onde uma sub-tarefa (ou a etapa do plano que ela espelha) vai — início (antes da primeira ainda não
/// feita), fim, uma posição acima/abaixo ou antes de outra.
/// </summary>
public static class TaskPosition
{
    public const string First = "first";
    public const string Last = "last";
    public const string Up = "up";
    public const string Down = "down";
    public const string Before = "before";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { First, Last, Up, Down, Before };

    /// <summary>Posição pedida; vazia = <c>before</c> com âncora, senão fim.</summary>
    public static string Normalize(string? position, bool hasBefore)
    {
        var p = (position ?? string.Empty).Trim().ToLowerInvariant();
        if (p.Length == 0) return hasBefore ? Before : Last;
        if (!All.Contains(p))
            throw new DomainException($"Posição inválida: '{position}' (use first, last, up, down ou before).");
        if (p == Before && !hasBefore)
            throw new DomainException("Informe antes de qual sub-tarefa (before).");
        return p;
    }

    public static string Label(string position, string? before) => position switch
    {
        First => "para o início",
        Last => "para o fim",
        Up => "uma posição acima",
        Down => "uma posição abaixo",
        _ => $"para antes da {before}",
    };
}

/// <summary>
/// Card 78680: reordenar sub-tarefas/etapas sem quebrar dependência. O que já terminou fica onde está (é histórico) e nada
/// passa para antes dele; o resto muda de lugar desde que toda dependência ainda não feita continue antes de quem depende.
/// </summary>
public static class TaskOrdering
{
    /// <summary>A sequência nova com <paramref name="item"/> na posição pedida (não valida dependências).</summary>
    public static List<T> Move<T>(IReadOnlyList<T> ordered, T item, string position, T? before, Func<T, bool> settled,
        Func<T, string> name) where T : class
    {
        var index = ordered.ToList().IndexOf(item);
        if (index < 0) throw new DomainException($"{name(item)} não está nesta lista.");
        var rest = ordered.Where(x => !ReferenceEquals(x, item)).ToList();
        int at;
        switch (position)
        {
            case TaskPosition.First:
                var firstOpen = rest.FirstOrDefault(x => !settled(x));
                at = firstOpen is null ? rest.Count : rest.IndexOf(firstOpen);
                break;
            case TaskPosition.Last:
                at = rest.Count;
                break;
            case TaskPosition.Up:
                var previous = ordered.Take(index).LastOrDefault(x => !settled(x))
                               ?? throw new DomainException($"{name(item)} já é a primeira ainda não feita.");
                at = rest.IndexOf(previous);
                break;
            case TaskPosition.Down:
                var next = ordered.Skip(index + 1).FirstOrDefault(x => !settled(x))
                           ?? throw new DomainException($"{name(item)} já é a última ainda não feita.");
                at = rest.IndexOf(next) + 1;
                break;
            case TaskPosition.Before:
                if (before is null) throw new DomainException("Informe antes de qual sub-tarefa (before).");
                if (ReferenceEquals(before, item)) throw new DomainException("Informe outra sub-tarefa em 'before'.");
                if (settled(before)) throw new DomainException($"{name(before)} já terminou — escolha uma ainda não feita.");
                at = rest.IndexOf(before);
                if (at < 0) throw new DomainException($"{name(before)} não está nesta lista.");
                break;
            default:
                throw new DomainException($"Posição inválida: '{position}'.");
        }
        rest.Insert(at, item);
        return rest;
    }

    /// <summary>Primeira dependência quebrada na sequência: quem depende e a dependência ainda não feita que ficou depois.</summary>
    public static (T Task, T Dependency)? Violation<T>(IReadOnlyList<T> sequence, Func<T, bool> settled, Func<T, IEnumerable<T>> dependencies)
        where T : class
    {
        var position = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < sequence.Count; i++) position[sequence[i]] = i;
        for (var i = 0; i < sequence.Count; i++)
        {
            if (settled(sequence[i])) continue;
            foreach (var dep in dependencies(sequence[i]))
                if (!settled(dep) && position.TryGetValue(dep, out var p) && p > i)
                    return (sequence[i], dep);
        }
        return null;
    }

    /// <summary>Quem ainda não terminou e depende (direta ou indiretamente) de <paramref name="item"/>, na ordem.</summary>
    public static List<T> Dependents<T>(IReadOnlyList<T> ordered, T item, Func<T, bool> settled, Func<T, IEnumerable<T>> dependencies)
        where T : class
    {
        var found = new HashSet<T>(ReferenceEqualityComparer.Instance) { item };
        bool grew;
        do
        {
            grew = false;
            foreach (var x in ordered)
                if (!found.Contains(x) && !settled(x) && dependencies(x).Any(found.Contains))
                    grew = found.Add(x) || grew;
        } while (grew);
        found.Remove(item);
        return ordered.Where(found.Contains).ToList();
    }

    /// <summary>Dependências ainda não feitas (diretas e indiretas) de <paramref name="item"/>.</summary>
    public static List<T> Upstream<T>(T item, Func<T, bool> settled, Func<T, IEnumerable<T>> dependencies) where T : class
    {
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<T>(dependencies(item));
        var list = new List<T>();
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (ReferenceEquals(d, item) || !seen.Add(d) || settled(d)) continue;
            list.Add(d);
            foreach (var x in dependencies(d)) queue.Enqueue(x);
        }
        return list;
    }
}
