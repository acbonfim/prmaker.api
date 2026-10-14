namespace solvace.knowledge.application;

/// <summary>
/// Cargas pesadas (texto de muitas seções/itens de uma vez) uma por vez na instância (0070): com 512 MiB, duas ou três
/// cargas em paralelo — a tela da Base, a busca e o espelho ao mesmo tempo — estouravam a memória (o EF ainda guarda o
/// resultado inteiro por causa do retry). Quem chega depois espera e, em geral, encontra o cache pronto.
/// </summary>
public static class HeavyReads
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { Gate.Release(); }
    }

    /// <summary>Lotes de ids cujo tamanho somado fica perto de <paramref name="maxChars"/> (uma seção maior vai sozinha).</summary>
    public static IEnumerable<List<T>> BySize<T>(IEnumerable<T> items, Func<T, int> size, int maxChars = 3_000_000)
    {
        var batch = new List<T>();
        var total = 0;
        foreach (var item in items)
        {
            var s = Math.Max(1, size(item));
            if (batch.Count > 0 && total + s > maxChars)
            {
                yield return batch;
                batch = [];
                total = 0;
            }
            batch.Add(item);
            total += s;
        }
        if (batch.Count > 0) yield return batch;
    }
}
