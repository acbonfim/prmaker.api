using Microsoft.Extensions.Logging;
using Octokit;
using solvace.github.domain.Responses;

namespace solvace.github.application.Services;

/// <summary>
/// 0106: review do PR pela integração do GitHub de quem pede — a base do fluxo do pedido de mudanças (ler os pontos,
/// saber se cada um cai em código novo do PR, responder no próprio comentário e pedir nova revisão). Nunca aprova nem
/// mescla.
/// </summary>
public partial class GitHubService
{
    private const int MaxReviewBody = 4000;

    public async Task<PullRequestReviewsResponse> GetPullRequestReviewsAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        var result = new PullRequestReviewsResponse { Repository = repository, Number = number };
        try
        {
            var pr = await Client.PullRequest.Get(owner, repo, number);
            result.Title = pr.Title;
            result.Url = pr.HtmlUrl;
            result.Head = pr.Head?.Ref;
            result.Base = pr.Base?.Ref;
            result.State = pr.Merged ? "merged" : pr.State.StringValue;

            var reviews = await Client.PullRequest.Review.GetAll(owner, repo, number);
            result.Reviews = reviews.Where(r => r.User is not null)
                .Select(r => new PullRequestReviewItem
                {
                    Id = r.Id, User = r.User.Login, State = ReviewState(r.State), Body = Cut(r.Body), SubmittedAt = r.SubmittedAt, Url = r.HtmlUrl
                })
                .OrderBy(r => r.SubmittedAt).ToList();
            result.ChangesRequestedBy = reviews
                .Where(r => r.User is not null && r.State.Value is PullRequestReviewState.Approved or PullRequestReviewState.ChangesRequested or PullRequestReviewState.Dismissed)
                .GroupBy(r => r.User.Login, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.OrderBy(r => r.SubmittedAt).Last().State.Value == PullRequestReviewState.ChangesRequested)
                .Select(g => g.Key).ToList();

            var comments = await Client.PullRequest.ReviewComment.GetAll(owner, repo, number);
            result.Comments = comments.Where(c => c.User is not null)
                .Select(c => new PullRequestLineComment
                {
                    Id = c.Id,
                    ReviewId = c.PullRequestReviewId,
                    InReplyTo = c.InReplyToId,
                    User = c.User.Login,
                    Path = c.Path ?? string.Empty,
                    Line = c.Position is null ? c.OriginalPosition : c.Position,
                    Body = Cut(c.Body) ?? string.Empty,
                    OnNewCode = IsNewLine(c.DiffHunk),
                    Outdated = c.Position is null,
                    Snippet = Tail(c.DiffHunk, 6),
                    CreatedAt = c.CreatedAt,
                    Url = c.HtmlUrl
                })
                .OrderBy(c => c.CreatedAt).ToList();

            var conversation = await Client.Issue.Comment.GetAllForIssue(owner, repo, number);
            result.Conversation = conversation.Where(c => c.User is not null)
                .Select(c => new PullRequestConversationComment
                {
                    Id = c.Id, User = c.User.Login, Body = Cut(c.Body) ?? string.Empty, CreatedAt = c.CreatedAt, Url = c.HtmlUrl
                })
                .OrderBy(c => c.CreatedAt).ToList();
        }
        catch (NotFoundException)
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
        // Octokit 9 só aceita int no id do comentário respondido — os ids do GitHub já passam de int: vai direto na API.
        var uri = new Uri($"repos/{owner}/{repo}/pulls/{number}/comments/{commentId}/replies", UriKind.Relative);
        var reply = await new ApiConnection(Client.Connection).Post<Octokit.PullRequestReviewComment>(uri, new { body });
        return reply.HtmlUrl;
    }

    public async Task<string?> CommentOnPullRequestAsync(string repository, int number, string body, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        var comment = await Client.Issue.Comment.Create(owner, repo, number, body);
        return comment.HtmlUrl;
    }

    public async Task RequestReviewAsync(string repository, int number, IReadOnlyCollection<string> reviewers, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);
        var (owner, repo) = ResolveRepository(repository);
        var users = reviewers.Where(r => !string.IsNullOrWhiteSpace(r) && !r.StartsWith('@')).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var teams = reviewers.Where(r => r.StartsWith('@') && r.Length > 1).Select(r => r[1..].Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (users.Count == 0 && teams.Count == 0) return;
        await Client.PullRequest.ReviewRequest.Create(owner, repo, number, new PullRequestReviewRequest(users, teams));
    }

    private static string ReviewState(StringEnum<PullRequestReviewState> state) => state.TryParse(out var s)
        ? s switch
        {
            PullRequestReviewState.Approved => "approved",
            PullRequestReviewState.ChangesRequested => "changes_requested",
            PullRequestReviewState.Commented => "commented",
            PullRequestReviewState.Dismissed => "dismissed",
            _ => "pending"
        }
        : state.StringValue.ToLowerInvariant();

    /// <summary>O comentário fica na última linha do trecho do diff: começa com '+' = linha que o PR adicionou/alterou.</summary>
    public static bool IsNewLine(string? diffHunk)
    {
        if (string.IsNullOrEmpty(diffHunk)) return false;
        var last = diffHunk.TrimEnd('\n', '\r').Split('\n').LastOrDefault() ?? string.Empty;
        return last.StartsWith('+');
    }

    private static string? Tail(string? diffHunk, int lines)
    {
        if (string.IsNullOrEmpty(diffHunk)) return null;
        var all = diffHunk.TrimEnd('\n', '\r').Split('\n');
        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }

    private static string? Cut(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= MaxReviewBody ? text : text[..MaxReviewBody] + "…";
}
