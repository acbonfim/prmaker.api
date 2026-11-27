using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using solvace.github.domain.Responses;

namespace solvace.github.application.Services;

/// <summary>
/// 0106: review do PR pela integração do GitHub de quem pede — a base do fluxo do pedido de mudanças (ler os pontos,
/// saber se cada um cai em código novo do PR, responder no próprio comentário e pedir nova revisão). Nunca aprova nem
/// mescla. Vai direto na REST do GitHub (o Octokit 9 lê o id dos comentários como int e os ids do GitHub já passam do
/// limite — quebrava ao ler o review).
/// </summary>
public partial class GitHubService
{
    private const int MaxReviewBody = 4000;
    private const int ReviewPages = 5;

    public async Task<PullRequestReviewsResponse> GetPullRequestReviewsAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        var result = new PullRequestReviewsResponse { Repository = repository, Number = number };
        var prefix = $"repos/{owner}/{repo}";
        try
        {
            using (var pr = await RestAsync(HttpMethod.Get, $"{prefix}/pulls/{number}", null, cancellationToken))
            {
                var p = pr.RootElement;
                result.Title = Str(p, "title");
                result.Url = Str(p, "html_url");
                result.Head = p.TryGetProperty("head", out var head) ? Str(head, "ref") : null;
                result.Base = p.TryGetProperty("base", out var @base) ? Str(@base, "ref") : null;
                result.State = p.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True ? "merged" : Str(p, "state");
            }

            var reviews = await ListAsync($"{prefix}/pulls/{number}/reviews", cancellationToken);
            result.Reviews = reviews
                .Select(r => new PullRequestReviewItem
                {
                    Id = Long(r, "id") ?? 0, User = Login(r), State = (Str(r, "state") ?? string.Empty).ToLowerInvariant(),
                    Body = Cut(Str(r, "body")), SubmittedAt = Date(r, "submitted_at"), Url = Str(r, "html_url")
                })
                .Where(r => r.User.Length > 0).OrderBy(r => r.SubmittedAt).ToList();
            // Vale a última revisão de cada pessoa entre aprovou / pediu mudanças / dispensada.
            result.ChangesRequestedBy = result.Reviews
                .Where(r => r.State is "approved" or "changes_requested" or "dismissed")
                .GroupBy(r => r.User, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.OrderBy(r => r.SubmittedAt).Last().State == "changes_requested")
                .Select(g => g.Key).ToList();

            var comments = await ListAsync($"{prefix}/pulls/{number}/comments", cancellationToken);
            result.Comments = comments
                .Select(c =>
                {
                    var position = Int(c, "position");
                    var hunk = Str(c, "diff_hunk");
                    return new PullRequestLineComment
                    {
                        Id = Long(c, "id") ?? 0,
                        ReviewId = Long(c, "pull_request_review_id"),
                        InReplyTo = Long(c, "in_reply_to_id"),
                        User = Login(c),
                        Path = Str(c, "path") ?? string.Empty,
                        Line = Int(c, "line") ?? Int(c, "original_line") ?? position ?? Int(c, "original_position"),
                        Body = Cut(Str(c, "body")) ?? string.Empty,
                        OnNewCode = IsNewLine(hunk),
                        Outdated = position is null && Int(c, "line") is null,
                        Snippet = Tail(hunk, 6),
                        CreatedAt = Date(c, "created_at") ?? DateTimeOffset.MinValue,
                        Url = Str(c, "html_url")
                    };
                })
                .Where(c => c.User.Length > 0).OrderBy(c => c.CreatedAt).ToList();

            var conversation = await ListAsync($"{prefix}/issues/{number}/comments", cancellationToken);
            result.Conversation = conversation
                .Select(c => new PullRequestConversationComment
                {
                    Id = Long(c, "id") ?? 0, User = Login(c), Body = Cut(Str(c, "body")) ?? string.Empty,
                    CreatedAt = Date(c, "created_at") ?? DateTimeOffset.MinValue, Url = Str(c, "html_url")
                })
                .Where(c => c.User.Length > 0).OrderBy(c => c.CreatedAt).ToList();
        }
        catch (GitHubRestException e) when (e.Status == HttpStatusCode.NotFound)
        {
            result.Error = $"PR #{number} não encontrado em {owner}/{repo}.";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Falha ao ler o review do PR {Repository}#{Number}", repository, number);
            result.Error = "Falha ao consultar o GitHub: " + e.Message;
        }
        return result;
    }

    public async Task<string?> ReplyToReviewCommentAsync(string repository, int number, long commentId, string body, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        using var reply = await RestAsync(HttpMethod.Post, $"repos/{owner}/{repo}/pulls/{number}/comments/{commentId}/replies", new { body }, cancellationToken);
        return Str(reply.RootElement, "html_url");
    }

    public async Task<string?> CommentOnPullRequestAsync(string repository, int number, string body, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        using var comment = await RestAsync(HttpMethod.Post, $"repos/{owner}/{repo}/issues/{number}/comments", new { body }, cancellationToken);
        return Str(comment.RootElement, "html_url");
    }

    public async Task RequestReviewAsync(string repository, int number, IReadOnlyCollection<string> reviewers, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        var users = reviewers.Where(r => !string.IsNullOrWhiteSpace(r) && !r.StartsWith('@')).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var teams = reviewers.Where(r => r.StartsWith('@') && r.Length > 1).Select(r => r[1..].Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (users.Count == 0 && teams.Count == 0) return;
        using var _ = await RestAsync(HttpMethod.Post, $"repos/{owner}/{repo}/pulls/{number}/requested_reviewers",
            new { reviewers = users, team_reviewers = teams }, cancellationToken);
    }

    /// <summary>O comentário fica na última linha do trecho do diff: começa com '+' = linha que o PR adicionou/alterou.</summary>
    public static bool IsNewLine(string? diffHunk)
    {
        if (string.IsNullOrEmpty(diffHunk)) return false;
        var last = diffHunk.TrimEnd('\n', '\r').Split('\n').LastOrDefault() ?? string.Empty;
        return last.StartsWith('+');
    }

    /// <summary>Erro da REST do GitHub com o status (404 = PR não existe).</summary>
    public sealed class GitHubRestException(HttpStatusCode status, string message) : Exception(message)
    {
        public HttpStatusCode Status { get; } = status;
    }

    /// <summary>Lista paginada (100 por página, até 5 páginas).</summary>
    private async Task<List<JsonElement>> ListAsync(string path, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        for (var page = 1; page <= ReviewPages; page++)
        {
            using var doc = await RestAsync(HttpMethod.Get, $"{path}?per_page=100&page={page}", null, ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) break;
            var items = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            all.AddRange(items);
            if (items.Count < 100) break;
        }
        return all;
    }

    /// <summary>REST do GitHub no mesmo endereço e com o mesmo token do Octokit (GitHub Enterprise: &lt;host&gt;/api/v3/).</summary>
    private async Task<JsonDocument> RestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var baseAddress = Client.Connection.BaseAddress;
        if (!baseAddress.AbsoluteUri.EndsWith('/')) baseAddress = new Uri(baseAddress.AbsoluteUri + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        request.Headers.UserAgent.ParseAdd("SolvacePRForm");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new GitHubRestException(response.StatusCode, $"GitHub {(int)response.StatusCode}: {Truncate(text, 300)}");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string Login(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("user", out var u) ? Str(u, "login") ?? string.Empty : string.Empty;

    private static string? Tail(string? diffHunk, int lines)
    {
        if (string.IsNullOrEmpty(diffHunk)) return null;
        var all = diffHunk.TrimEnd('\n', '\r').Split('\n');
        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }

    private static string? Cut(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= MaxReviewBody ? text : text[..MaxReviewBody] + "…";
}
