using System.Text;
using System.Text.Json;
using Octokit;
using solvace.github.domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using solvace.github.application.Contract;
using Microsoft.Extensions.Http;
using System.Net.Http;
using solvace.github.domain.Responses;

namespace solvace.github.application.Services;

public class GitHubService : IGitHubService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubClient _gitHubClient;

    public GitHubService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;

        var token = _configuration["GITHUB_TOKEN"];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("GITHUB_TOKEN não configurado");

        _gitHubClient = new GitHubClient(new ProductHeaderValue("SolvacePRForm"))
        {
            Credentials = new Credentials(token)
        };
    }

    public HttpJsonResponse TestConfiguration()
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        var branch = _configuration["GITHUB_BRANCH"];
        var token = _configuration["GITHUB_TOKEN"];
        var payload = new
        {
            owner,
            repo,
            branch,
            hasToken = !string.IsNullOrEmpty(token),
            tokenLength = token?.Length ?? 0,
            message = "Configuração do GitHub carregada com sucesso"
        };
        return HttpJsonResponse.Ok(JsonSerializer.Serialize(payload));
    }

    public async Task<HttpJsonResponse> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default)
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Configurações do GitHub (owner/repo) não encontradas" }));
        if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(targetBranch) || string.IsNullOrWhiteSpace(title))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Parâmetros obrigatórios: sourceBranch, targetBranch, title" }));

        var description = string.IsNullOrWhiteSpace(descriptionRaw) ? null
            : descriptionRaw.Replace("\u0000", string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        try
        {
            await _gitHubClient.Repository.Branch.Get(owner, repo, sourceBranch.Replace("refs/heads/", ""));
            await _gitHubClient.Repository.Branch.Get(owner, repo, targetBranch.Replace("refs/heads/", ""));
        }
        catch
        {
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "\r\nSource/destination branch not found." }));
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
            var payload = new
            {
                number = pr.Number,
                title = pr.Title,
                state = pr.State.StringValue,
                url = pr.HtmlUrl,
                head = pr.Head.Label,
                @base = pr.Base.Label,
                isDraft = pr.Draft
            };
            return HttpJsonResponse.Ok(JsonSerializer.Serialize(payload));
        }
        catch (NotFoundException ex)
        {
            return HttpJsonResponse.From(404, JsonSerializer.Serialize(new { error = "Repositório ou branches não encontrados", details = ex.Message }));
        }
        catch (ApiValidationException ex)
        {
            return HttpJsonResponse.From((int)ex.StatusCode, JsonSerializer.Serialize(new { error = "Validação da PR falhou na API", details = ex.ApiError?.Message, ex.ApiError?.Errors }));
        }
        catch (ApiException ex)
        {
            return HttpJsonResponse.From((int)ex.StatusCode, JsonSerializer.Serialize(new { error = "Erro na API do GitHub", details = ex.Message }));
        }
        catch (Exception ex)
        {
            return HttpJsonResponse.From(500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    public async Task<HttpJsonResponse> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default)
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Configurações do GitHub (owner/repo) não encontradas" }));
        if (string.IsNullOrWhiteSpace(cardNumber))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Parâmetro 'cardNumber' é obrigatório" }));

        var branchesTask = _gitHubClient.Repository.Branch.GetAll(owner, repo)
            .ContinueWith(t => t.Result
                .Where(b => b.Name.Contains(cardNumber, StringComparison.OrdinalIgnoreCase))
                .Take(maxPerType)
                .Select(b => new
                {
                    name = b.Name,
                    @protected = b.Protected,
                    commitSha = b.Commit.Sha,
                    url = $"https://github.com/{owner}/{repo}/tree/{b.Name}"
                })
                .ToArray());

        var prsTask = Task.Run(async () =>
        {
            using var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri("https://api.github.com/");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SolvacePRForm");
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _configuration["GITHUB_TOKEN"]);

            var q = Uri.EscapeDataString($"repo:{owner}/{repo} is:pr {cardNumber} in:title,body");
            var url = $"search/issues?q={q}&per_page={maxPerType}";
            var resp = await http.GetAsync(url, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode) return Array.Empty<object>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return Array.Empty<object>();
            var results = new List<object>();
            foreach (var item in items.EnumerateArray())
            {
                results.Add(new
                {
                    number = item.GetProperty("number").GetInt32(),
                    title = item.GetProperty("title").GetString(),
                    state = item.GetProperty("state").GetString(),
                    createdAt = item.GetProperty("created_at").GetDateTime(),
                    user = item.GetProperty("user").GetProperty("login").GetString(),
                    url = item.GetProperty("html_url").GetString()
                });
            }
            return results.ToArray();
        }, cancellationToken);

        var commitsTask = Task.Run(async () =>
        {
            using var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri("https://api.github.com/");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SolvacePRForm");
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _configuration["GITHUB_TOKEN"]);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.cloak-preview+json");

            var commitQuery = $"q={Uri.EscapeDataString($"repo:{owner}/{repo} {cardNumber}")}&per_page={maxPerType}";
            var url = $"search/commits?{commitQuery}";
            var resp = await http.GetAsync(url, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode) return Array.Empty<object>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return Array.Empty<object>();
            var results = new List<object>();
            foreach (var item in items.EnumerateArray())
            {
                var sha = item.GetProperty("sha").GetString();
                var htmlUrl = item.GetProperty("html_url").GetString();
                var commit = item.GetProperty("commit");
                var message = commit.GetProperty("message").GetString();
                var authorName = commit.TryGetProperty("author", out var author) && author.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var authorDate = commit.TryGetProperty("author", out author) && author.TryGetProperty("date", out var dateEl) ? (DateTime?)dateEl.GetDateTime() : null;
                results.Add(new { sha, message, author = authorName, date = authorDate, url = htmlUrl });
            }
            return results.ToArray();
        }, cancellationToken);

        var codeQuery = $"repo:{owner}/{repo} \"{cardNumber}\"";
        var codeTask = _gitHubClient.Search.SearchCode(new SearchCodeRequest(codeQuery))
            .ContinueWith(t => t.Result.Items
                .Take(maxPerType)
                .Select(code => new { path = code.Path, repository = code.Repository.FullName, url = code.HtmlUrl })
                .ToArray(), cancellationToken);

        await Task.WhenAll(branchesTask, prsTask, commitsTask, codeTask);
        var payloadOk = new
        {
            cardNumber,
            patterns = new[] { cardNumber, cardNumber },
            branches = branchesTask.Result,
            pullRequests = prsTask.Result,
            commits = commitsTask.Result,
            codeHits = codeTask.Result,
            limits = new { maxPerType }
        };
        return HttpJsonResponse.Ok(JsonSerializer.Serialize(payloadOk));
    }

    public async Task<HttpJsonResponse> GetTemplateAsync(string branch, CancellationToken cancellationToken = default)
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        var defaultBranch = _configuration["GITHUB_BRANCH"];
        var workflowPath = ".github/workflows/apply_pr_template.yml";

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(defaultBranch))
        {
            var err = new { error = "Configurações do GitHub não encontradas", owner = owner ?? "não configurado", repo = repo ?? "não configurado", branch = defaultBranch ?? "não configurado" };
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(err));
        }
        if (string.IsNullOrEmpty(branch))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Parâmetro 'branch' é obrigatório" }));

        try
        {
            var workflowContent = await _gitHubClient.Repository.Content.GetRawContentByRef(owner, repo, workflowPath, defaultBranch);
            var yamlContent = Encoding.UTF8.GetString(workflowContent);

            var regex = new System.Text.RegularExpressions.Regex($"\"{branch}\"\\)[\\s\\S]*?TEMPLATE=\"(.+?)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(yamlContent);
            if (!match.Success && branch.Contains("hotfix"))
            {
                var fallbackRegex = new System.Text.RegularExpressions.Regex("\"release-version\"\\)[\\s\\S]*?TEMPLATE=\"(.+?)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                match = fallbackRegex.Match(yamlContent);
            }
            if (!match.Success)
                return HttpJsonResponse.From(404, JsonSerializer.Serialize(new { error = $"Template para a branch \"{branch}\" não encontrado." }));

            var templatePath = match.Groups[1].Value;
            var templateContent = await _gitHubClient.Repository.Content.GetRawContentByRef(owner, repo, templatePath, defaultBranch);
            var templateText = Encoding.UTF8.GetString(templateContent);
            return new HttpJsonResponse { StatusCode = 200, Content = templateText, ContentType = "text/plain" };
        }
        catch (NotFoundException ex)
        {
            return HttpJsonResponse.From(404, JsonSerializer.Serialize(new { error = "Arquivo ou template não encontrado no GitHub", details = ex.Message }));
        }
        catch (ApiException ex)
        {
            return HttpJsonResponse.From((int)ex.StatusCode, JsonSerializer.Serialize(new { error = "Erro ao acessar GitHub API", details = ex.Message, statusCode = ex.StatusCode }));
        }
        catch (Exception ex)
        {
            return HttpJsonResponse.From(500, JsonSerializer.Serialize(new { error = "Erro ao buscar template do GitHub", details = ex.Message }));
        }
    }

    public async Task<HttpJsonResponse> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default)
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Configurações do GitHub (owner/repo) não encontradas" }));
        if (string.IsNullOrWhiteSpace(sha))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Parâmetro 'sha' é obrigatório" }));

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
            // var files = commit.Files?.Select(f => new
            // {
            //     filename = f.Filename,
            //     status = f.Status,
            //     additions = f.Additions,
            //     deletions = f.Deletions,
            //     changes = f.Changes,
            //     patch = f.Patch,              // unified diff
            //     blobUrl = f.BlobUrl,
            //     rawUrl = f.RawUrl,
            //     contentsUrl = f.ContentsUrl
            // }).ToArray() ?? Array.Empty<object>();

            // var payload = new
            // {
            //     sha = commit.Sha,
            //     parents = commit.Parents?.Select(p => p.Sha).ToArray() ?? Array.Empty<string>(),
            //     stats = new { additions = commit.Stats?.Additions ?? 0, deletions = commit.Stats?.Deletions ?? 0, total = commit.Stats?.Total ?? 0 },
            //     files
            // };
            // return HttpJsonResponse.Ok(JsonSerializer.Serialize(payload));
        }
        catch (NotFoundException ex)
        {
            return HttpJsonResponse.From(404, JsonSerializer.Serialize(new { error = "Commit não encontrado", details = ex.Message }));
        }
        catch (ApiException ex)
        {
            return HttpJsonResponse.From((int)ex.StatusCode, JsonSerializer.Serialize(new { error = "Erro na API do GitHub", details = ex.Message }));
        }
        catch (Exception ex)
        {
            return HttpJsonResponse.From(500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    public async Task<HttpJsonResponse> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default)
    {
        var owner = _configuration["GITHUB_OWNER"];
        var repo = _configuration["GITHUB_REPO"];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Configurações do GitHub (owner/repo) não encontradas" }));
        if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head))
            return HttpJsonResponse.From(400, JsonSerializer.Serialize(new { error = "Parâmetros 'base' e 'head' são obrigatórios" }));

        try
        {
            var compare = await _gitHubClient.Repository.Commit.Compare(owner, repo, @base, head);
            var files = compare.Files?.Select(f => new
            {
                filename = f.Filename,
                status = f.Status,
                additions = f.Additions,
                deletions = f.Deletions,
                changes = f.Changes,
                patch = f.Patch,
                blobUrl = f.BlobUrl,
                rawUrl = f.RawUrl,
                contentsUrl = f.ContentsUrl
            }).ToArray() ?? Array.Empty<object>();

            var payload = new
            {
                url = compare.Url,
                htmlUrl = compare.HtmlUrl,
                permalinkUrl = compare.PermalinkUrl,
                totalCommits = compare.TotalCommits,
                aheadBy = compare.AheadBy,
                behindBy = compare.BehindBy,
                status = compare.Status,
                files
            };
            return HttpJsonResponse.Ok(JsonSerializer.Serialize(payload));
        }
        catch (NotFoundException ex)
        {
            return HttpJsonResponse.From(404, JsonSerializer.Serialize(new { error = "Refs não encontradas ou inválidas", details = ex.Message }));
        }
        catch (ApiException ex)
        {
            return HttpJsonResponse.From((int)ex.StatusCode, JsonSerializer.Serialize(new { error = "Erro na API do GitHub", details = ex.Message }));
        }
        catch (Exception ex)
        {
            return HttpJsonResponse.From(500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }
}


