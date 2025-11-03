using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Octokit;
using solvace.github.domain.Options;
using solvace.github.application.Contract;
using solvace.github.domain.Responses;

namespace solvace.github.application.Services;

public class GitHubService : IGitHubService
{
    private readonly GitHubOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubClient _gitHubClient;

    public GitHubService(IOptions<GitHubOptions> options, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;

        if (string.IsNullOrEmpty(_options.Token))
            throw new InvalidOperationException("Token GitHub não configurado");

        _gitHubClient = new GitHubClient(new ProductHeaderValue("SolvacePRForm"))
        {
            Credentials = new Credentials(_options.Token)
        };
    }

    public async Task<PullRequestResponse?> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default)
    {
        var owner = _options.Owner;
        var repo = _options.Repo;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new PullRequestResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(targetBranch) || string.IsNullOrWhiteSpace(title))
            return new PullRequestResponse { Error = "Parâmetro 'sourceBranch', 'targetBranch' ou 'title' é obrigatório" };

        var description = string.IsNullOrWhiteSpace(descriptionRaw) ? null
            : descriptionRaw.Replace("\u0000", string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        try
        {
            await _gitHubClient.Repository.Branch.Get(owner, repo, sourceBranch.Replace("refs/heads/", ""));
            await _gitHubClient.Repository.Branch.Get(owner, repo, targetBranch.Replace("refs/heads/", ""));
        }
        catch
        {
            return new PullRequestResponse { Error = "Branch não encontrada" };
        }

        var head = sourceBranch.Replace("refs/heads/", string.Empty);
        var @base = targetBranch.Replace("refs/heads/", string.Empty);
        var newPr = new NewPullRequest(title, head, @base)
        {
            Body = string.IsNullOrEmpty(description) ? null : description,
            Draft = draft
        };

        try
        {
            var pr = await _gitHubClient.PullRequest.Create(owner, repo, newPr);
            return new PullRequestResponse
            {
                Id = pr.Id,
                Number = pr.Number.ToString(),
                Title = pr.Title,
                Body = pr.Body,
                State = pr.State.StringValue,
                CreatedAt = pr.CreatedAt.ToString(),
                UpdatedAt = pr.UpdatedAt.ToString() ?? string.Empty,
                ClosedAt = pr.ClosedAt?.ToString() ?? string.Empty,
                MergedAt = pr.MergedAt?.ToString() ?? string.Empty,
                Author = pr.User.Login,
                AuthorAvatarUrl = pr.User.AvatarUrl,
                AuthorUrl = pr.User.HtmlUrl,
                Url = pr.HtmlUrl,
                Head = pr.Head.Label,
                Base = pr.Base.Label,
                IsDraft = pr.Draft
            };
        }
        catch (NotFoundException)
        {
            return new PullRequestResponse { Error = "Repositório ou branches não encontrados" };
        }
        catch (ApiValidationException)
        {
            return new PullRequestResponse { Error = "Validação da PR falhou na API" };
        }
        catch (ApiException)
        {
            return new PullRequestResponse { Error = "Erro na API do GitHub" };
        }
        catch (Exception)
        {
            return new PullRequestResponse { Error = "Erro ao criar PR" };
        }
    }

    public async Task<CardReferencesResponse?> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default)
    {
        var owner = _options.Owner;
        var repo = _options.Repo;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CardReferencesResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(cardNumber))
            return new CardReferencesResponse { Error = "Parâmetro 'cardNumber' é obrigatório" };

        var branchesTask = _gitHubClient.Repository.Branch.GetAll(owner, repo)
            .ContinueWith(t => t.Result
                .Where(b => b.Name.Contains(cardNumber, StringComparison.OrdinalIgnoreCase))
                .Take(maxPerType)
                .Select(b => new BranchReferenceResponse
                {
                    Name = b.Name,
                    Protected = b.Protected,
                    CommitSha = b.Commit.Sha,
                    Url = $"https://github.com/{owner}/{repo}/tree/{b.Name}"
                })
            .ToArray());

        var commitsTask = Task.Run(async () =>
        {
            using var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri("https://api.github.com/");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SolvacePRForm");
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _options.Token);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.cloak-preview+json");

            var commitQuery = $"q={Uri.EscapeDataString($"repo:{owner}/{repo} {cardNumber}")}&per_page={maxPerType}";
            var url = $"search/commits?{commitQuery}";
            var resp = await http.GetAsync(url, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return Array.Empty<CommitReferenceResponse>();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items))
                return Array.Empty<CommitReferenceResponse>();

            var results = new List<CommitReferenceResponse>();
            foreach (var item in items.EnumerateArray())
            {
                var sha = item.GetProperty("sha").GetString() ?? string.Empty;
                var htmlUrl = item.GetProperty("html_url").GetString() ?? string.Empty;
                var commit = item.GetProperty("commit");
                var message = commit.GetProperty("message").GetString() ?? string.Empty;
                var authorName = commit.TryGetProperty("author", out var author) && author.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                var authorDate = commit.TryGetProperty("author", out author) && author.TryGetProperty("date", out var dateEl)
                    ? dateEl.GetString() ?? string.Empty
                    : string.Empty;

                results.Add(new CommitReferenceResponse
                {
                    Sha = sha,
                    Parents = Array.Empty<string>(),
                    Message = message,
                    Author = authorName,
                    Date = authorDate,
                    Url = htmlUrl
                });
            }
            return results.ToArray();
        }, cancellationToken);

        var codeQuery = $"repo:{owner}/{repo} \"{cardNumber}\"";
        var codeTask = _gitHubClient.Search.SearchCode(new SearchCodeRequest(codeQuery))
            .ContinueWith(t => t.Result.Items
                .Take(maxPerType)
                .Select(code => new CodeHitReferenceResponse { Path = code.Path, Repository = code.Repository.FullName, Url = code.HtmlUrl })
                .ToArray());

        await Task.WhenAll(branchesTask, commitsTask, codeTask);

        return new CardReferencesResponse
        {
            CardNumber = cardNumber,
            Patterns = new[] { cardNumber, cardNumber },
            Branches = branchesTask.Result,
            Commits = commitsTask.Result,
            CodeHits = codeTask.Result,
            Limits = new LimitsResponse { MaxPerType = maxPerType }
        };
    }

    public async Task<CommitDiffResponse?> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default)
    {
        var owner = _options.Owner;
        var repo = _options.Repo;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CommitDiffResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(sha))
            return new CommitDiffResponse { Error = "Parâmetro 'sha' é obrigatório" };

        try
        {
            var commit = await _gitHubClient.Repository.Commit.Get(owner, repo, sha);
            return new CommitDiffResponse
            {
                Sha = commit.Sha,
                Parents = commit.Parents?.Select(p => p.Sha).ToArray() ?? Array.Empty<string>(),
                Additions = commit.Stats?.Additions ?? 0,
                Deletions = commit.Stats?.Deletions ?? 0,
                Total = commit.Stats?.Total ?? 0,
                Files = commit.Files?.Select(f => new CommitFileDiffResponse
                {
                    Filename = f.Filename,
                    Status = f.Status,
                    Additions = f.Additions,
                    Deletions = f.Deletions,
                    Changes = f.Changes,
                    Patch = f.Patch,
                    BlobUrl = f.BlobUrl,
                    RawUrl = f.RawUrl,
                    ContentsUrl = f.ContentsUrl
                }).ToArray() ?? Array.Empty<CommitFileDiffResponse>()
            };
        }
        catch (NotFoundException)
        {
            return new CommitDiffResponse { Error = "Commit não encontrado" };
        }
        catch (ApiException)
        {
            return new CommitDiffResponse { Error = "Erro ao acessar GitHub API" };
        }
        catch (Exception)
        {
            return new CommitDiffResponse { Error = "Erro ao buscar commit diff" };
        }
    }

    public async Task<CompareDiffResponse?> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default)
    {
        var owner = _options.Owner;
        var repo = _options.Repo;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CompareDiffResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head))
            return new CompareDiffResponse { Error = "Parâmetro '@base' ou 'head' é obrigatório" };

        try
        {
            var compare = await _gitHubClient.Repository.Commit.Compare(owner, repo, @base, head);
            return new CompareDiffResponse
            {
                Url = compare.Url,
                HtmlUrl = compare.HtmlUrl,
                PermalinkUrl = compare.PermalinkUrl,
                TotalCommits = compare.TotalCommits,
                AheadBy = compare.AheadBy,
                BehindBy = compare.BehindBy,
                Status = compare.Status,
                Files = compare.Files?.Select(f => new CommitFileDiffResponse
                {
                    Filename = f.Filename,
                    Status = f.Status,
                    Additions = f.Additions,
                    Deletions = f.Deletions,
                    Changes = f.Changes,
                    Patch = f.Patch,
                    BlobUrl = f.BlobUrl,
                    RawUrl = f.RawUrl,
                    ContentsUrl = f.ContentsUrl
                }).ToArray() ?? Array.Empty<CommitFileDiffResponse>()
            };
        }
        catch (NotFoundException)
        {
            return new CompareDiffResponse { Error = "Commit não encontrado" };
        }
        catch (ApiException)
        {
            return new CompareDiffResponse { Error = "Erro ao acessar GitHub API" };
        }
        catch (Exception)
        {
            return new CompareDiffResponse { Error = "Erro ao buscar commit diff" };
        }
    }
}


