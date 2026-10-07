using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.application;

/// <summary>
/// Sumário em pedaços das seções grandes (0070), em cache pela marca do conteúdo: o texto é lido uma vez por versão e
/// descartado — só ficam as posições dos pedaços. Usado pela Base Solvace e pelos documentos da engenharia reversa.
/// </summary>
public static class SectionOutlines
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (string Hash, List<MarkdownChunk> Chunks)> Cache = new();

    /// <summary>Marca do conteúdo da seção (o hash do texto + a versão).</summary>
    public static string Hash(ArchitectureSection section) => $"{section.ContentHash}.{section.Version}";

    public static async Task<List<MarkdownChunk>> GetAsync(IKnowledgeRepository repository, ArchitectureSection head, CancellationToken cancellationToken)
    {
        var hash = Hash(head);
        if (Cache.TryGetValue(head.Id, out var cached) && cached.Hash == hash) return cached.Chunks;
        return await HeavyReads.RunAsync(async () =>
        {
            if (Cache.TryGetValue(head.Id, out var again) && again.Hash == hash) return again.Chunks;
            var content = (await repository.GetSectionContentsAsync([head.Id], cancellationToken)).GetValueOrDefault(head.Id) ?? string.Empty;
            var chunks = MarkdownOutline.Split(content);
            if (Cache.Count > 2000) Cache.Clear();
            Cache[head.Id] = (hash, chunks);
            return chunks;
        }, cancellationToken);
    }
}
