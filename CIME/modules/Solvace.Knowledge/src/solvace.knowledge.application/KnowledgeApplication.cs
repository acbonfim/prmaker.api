using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>
/// Cópia filtrada do Knowledge Center (0033). O Cloud Run não alcança o banco do KC (VPC da AWS): quem tem a
/// credencial (a skill, na máquina do usuário) lê o que mudou e manda aqui. O piso de filtro de teste é reaplicado
/// em todo lote e sobre o que já está guardado — uma exclusão nova no plugin vale na próxima sincronização.
/// </summary>
public class KnowledgeApplication(IKnowledgeRepository repository, IKnowledgeSettingsProvider settings) : IKnowledgeApplication
{
    public const int MaxArticlesPerSync = 5_000;
    private const int SnippetLength = 600;

    public async Task<KnowledgeSyncResponse> SyncAsync(KnowledgeSyncRequest request, string actor, CancellationToken cancellationToken)
    {
        var environment = KnowledgeEnvironment.Normalize(request.Environment);
        if (request.Articles.Count > MaxArticlesPerSync)
            throw new DomainException($"Envie no máximo {MaxArticlesPerSync} artigos por lote.");

        var filter = (await settings.GetAsync(cancellationToken)).Filter;
        var now = DateTimeOffset.UtcNow;
        var stored = (await repository.GetArticlesAsync(environment, tracked: true, cancellationToken)).ToDictionary(a => a.SourceId);
        var response = new KnowledgeSyncResponse { Environment = environment, Received = request.Articles.Count };
        var seen = new HashSet<string>();

        foreach (var item in request.Articles.Where(a => !string.IsNullOrWhiteSpace(a.SourceId)))
        {
            item.SourceId = item.SourceId.Trim();
            seen.Add(item.SourceId);
            var verdict = KnowledgeNoiseFilter.Evaluate(new KnowledgeArticleCandidate(item.ArticleNumber, item.Title, item.Category,
                item.Subcategory, item.StatusId, item.IsDeleted, item.CategoryActive, item.SubcategoryActive, item.Content), filter);
            stored.TryGetValue(item.SourceId, out var existing);

            if (!verdict.Accepted)
            {
                response.Rejected++;
                var reason = ReasonGroup(verdict.Reason);
                response.RejectedReasons[reason] = response.RejectedReasons.GetValueOrDefault(reason) + 1;
                if (existing is not null)
                {
                    repository.RemoveArticle(existing);
                    stored.Remove(item.SourceId);
                    response.Removed++;
                }
                continue;
            }

            response.Accepted++;
            if (existing is null)
            {
                existing = new KnowledgeArticle(environment, item.SourceId);
                repository.AddArticle(existing);
                stored[item.SourceId] = existing;
            }
            if (existing.Apply(item.ArticleNumber, item.Title!, item.Content ?? string.Empty, item.Category, item.Subcategory,
                    item.Tags, item.SourceUpdatedAt, actor, now))
                response.Changed++;
        }

        // Carga completa: o que não veio não existe mais (ou deixou de ser visível) no KC.
        if (request.Full)
            foreach (var gone in stored.Values.Where(a => !seen.Contains(a.SourceId)).ToList())
            {
                repository.RemoveArticle(gone);
                stored.Remove(gone.SourceId);
                response.Removed++;
            }

        // Regras novas do administrador valem também para o que já estava guardado.
        foreach (var kept in stored.Values.Where(a => !seen.Contains(a.SourceId)).ToList())
        {
            var verdict = KnowledgeNoiseFilter.Evaluate(new KnowledgeArticleCandidate(kept.ArticleNumber, kept.Title, kept.Category,
                kept.Subcategory, KnowledgeNoiseFilter.PublishedStatusId, false, true, true, kept.Content), filter);
            if (verdict.Accepted) continue;
            repository.RemoveArticle(kept);
            stored.Remove(kept.SourceId);
            response.Removed++;
        }

        var state = await repository.GetStateAsync(environment, cancellationToken);
        if (state is null)
        {
            state = new KnowledgeSyncState(environment);
            repository.AddState(state);
        }
        state.Register(request.Articles.Max(a => a.SourceUpdatedAt), request.Full, stored.Count, KnowledgeArticle.Hash(filter.Fingerprint()), actor, now);
        await repository.SaveChangesAsync(cancellationToken);

        response.Total = stored.Count;
        response.Watermark = state.Watermark;
        return response;
    }

    public async Task<KnowledgeStateResponse> GetStateAsync(string? environment, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var active = current.ActiveEnvironment;
        var env = string.IsNullOrWhiteSpace(environment) ? active : KnowledgeEnvironment.Normalize(environment);
        var state = await repository.GetStateAsync(env, cancellationToken);
        return new KnowledgeStateResponse
        {
            ActiveEnvironment = active,
            Environment = env,
            Watermark = state?.Watermark,
            LastSyncAt = state?.LastSyncAt,
            LastFullSyncAt = state?.LastFullSyncAt,
            LastSyncBy = state?.LastSyncBy,
            ArticleCount = state?.ArticleCount ?? 0,
            FilterChanged = state?.FilterHash is null || state.FilterHash != KnowledgeArticle.Hash(current.Filter.Fingerprint())
        };
    }

    public async Task<List<KnowledgeArticleResponse>> SearchAsync(string? term, int limit, CancellationToken cancellationToken)
    {
        var env = (await settings.GetAsync(cancellationToken)).ActiveEnvironment;
        var articles = await repository.SearchArticlesAsync(env, term, Math.Clamp(limit, 1, 50), cancellationToken);
        return articles.Select(a => ToResponse(a, Snippet(a.Content, term))).ToList();
    }

    public async Task<KnowledgeArticleResponse> GetArticleAsync(int articleNumber, CancellationToken cancellationToken)
    {
        var env = (await settings.GetAsync(cancellationToken)).ActiveEnvironment;
        var article = await repository.GetArticleAsync(env, articleNumber, cancellationToken)
                      ?? throw new KnowledgeNotFoundException($"ART-{articleNumber} não está na base ({env}) — não existe, não foi publicado ou foi filtrado como teste.");
        return ToResponse(article, article.Content);
    }

    internal static KnowledgeArticleResponse ToResponse(KnowledgeArticle a, string content) => new()
    {
        ArticleNumber = a.ArticleNumber,
        Environment = a.Environment,
        Title = a.Title,
        Category = a.Category,
        Subcategory = a.Subcategory,
        Tags = a.Tags,
        Content = content,
        SourceUpdatedAt = a.SourceUpdatedAt,
        SyncedAt = a.SyncedAt
    };

    /// <summary>Trecho em volta do primeiro termo encontrado (ou o começo do texto).</summary>
    private static string Snippet(string content, string? term)
    {
        if (content.Length <= SnippetLength) return content;
        var word = (term ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(w => w.Length > 2);
        var at = word is null ? -1 : content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, Math.Min(at < 0 ? 0 : at - SnippetLength / 3, content.Length - SnippetLength));
        return (start > 0 ? "…" : "") + content.Substring(start, SnippetLength).Trim() + "…";
    }

    private static string ReasonGroup(string? reason) =>
        reason is null ? "outro" : reason.Contains("parece teste") ? reason[..reason.IndexOf(" (", StringComparison.Ordinal)]
            : reason.StartsWith("texto curto", StringComparison.Ordinal) ? "texto curto" : reason;
}
