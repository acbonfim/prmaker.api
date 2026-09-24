using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Octokit;
using solvace.github.domain.Options;
using solvace.github.application.Contract;
using solvace.github.domain.Responses;
using solvace.prform.application;
using solvace.prform.application.UserIntegrations;
using Cime.BuildingBlocks.Cache;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Enums;
using solvace.prform.domain.Extensions;

namespace solvace.github.application.Services;

public class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const int RepositoriesCacheMinutes = 10;
    private const int StatusCacheMinutes = 1;
    private const int StatusMaxParallelism = 5;
    private const int RateLimitWarningThreshold = 100;
    private static readonly TimeSpan StatusRequestTimeout = TimeSpan.FromSeconds(5);

    private const string PluginName = "Github Configurations";

    private readonly IPluginConfigurationResolver _configurationResolver;
    private readonly ICacheService _cacheService;
    private readonly ILogger<GitHubService> _logger;

    // Configuração e cliente da requisição: token global, ou o do usuário quando o plugin é de uso
    // pessoal (feature 0002). Criados em EnsureClientAsync, no início de cada operação.
    private PluginConfiguration? _config;
    private GitHubClient? _gitHubClient;
    private string _tokenScope = string.Empty;

    public GitHubService(IOptions<GitHubOptions> options, IHttpClientFactory httpClientFactory, IPluginConfigurationResolver configurationResolver, ICacheService cacheService, ILogger<GitHubService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configurationResolver = configurationResolver;
        _cacheService = cacheService;
    }

    /// <summary>
    /// Resolve a configuração efetiva e cria o cliente com o token dela. Lança
    /// PersonalIntegrationRequiredException (→ 403) se o plugin for pessoal e o usuário não
    /// configurou. Chamar no início de cada operação, fora de try/catch.
    /// </summary>
    private async Task EnsureClientAsync(CancellationToken cancellationToken)
    {
        if (_gitHubClient is not null)
            return;

        _config = await _configurationResolver.GetEffectiveConfigurationAsync(PluginName, cancellationToken);
        var token = _config.GetConfigurationValue("Token");
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Token GitHub não configurado");

        _gitHubClient = new GitHubClient(new ProductHeaderValue("SolvacePRForm"))
        {
            Credentials = new Credentials(token)
        };

        // Caches de repositórios/status separados por token: o que se enxerga depende das permissões dele.
        _tokenScope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];
    }

    private PluginConfiguration Config =>
        _config ?? throw new InvalidOperationException("Configuração do GitHub não resolvida");

    private GitHubClient Client =>
        _gitHubClient ?? throw new InvalidOperationException("Cliente do GitHub não inicializado");

    public async Task<PullRequestResponse?> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, bool draft, string? descriptionRaw, CancellationToken cancellationToken = default, string? repository = null)
    {
        await EnsureClientAsync(cancellationToken);

        var repositoryId = string.IsNullOrWhiteSpace(repository) ? Config.GetConfigurationValue("Repo") : repository.Trim();
        var (owner, repo) = ResolveRepository(repositoryId);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new PullRequestResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(targetBranch) || string.IsNullOrWhiteSpace(title))
            return new PullRequestResponse { Error = "Parâmetro 'sourceBranch', 'targetBranch' ou 'title' é obrigatório" };

        var description = NormalizeBody(descriptionRaw);
        var head = sourceBranch.Replace("refs/heads/", string.Empty);
        var @base = targetBranch.Replace("refs/heads/", string.Empty);

        try
        {
            await Client.Repository.Branch.Get(owner, repo, head);
        }
        catch
        {
            return new PullRequestResponse { Error = $"Branch '{head}' não encontrada no repositório '{repo}'" };
        }

        try
        {
            await Client.Repository.Branch.Get(owner, repo, @base);
        }
        catch
        {
            return new PullRequestResponse { Error = $"Branch '{@base}' não encontrada no repositório '{repo}'" };
        }

        var newPr = new NewPullRequest(title, head, @base)
        {
            Body = description,
            Draft = draft
        };

        try
        {
            var pr = await Client.PullRequest.Create(owner, repo, newPr);
            CacheStatus(pr, repositoryId!);
            return ToPullRequestResponse(pr, repositoryId!);
        }
        catch (ApiValidationException e) when (IsPullRequestAlreadyExists(e))
        {
            // Idempotência: já existe PR aberto para head→base, devolve o existente.
            var existing = await FindOpenPullRequestAsync(owner, repo, head, @base);
            if (existing is null)
                return new PullRequestResponse { Error = $"Já existe um PR para '{head}' → '{@base}', mas não foi possível localizá-lo" };

            CacheStatus(existing, repositoryId!);
            var response = ToPullRequestResponse(existing, repositoryId!);
            response.AlreadyExisted = true;
            return response;
        }
        catch (NotFoundException)
        {
            return new PullRequestResponse { Error = "Repositório ou branches não encontrados" };
        }
        catch (ApiValidationException e)
        {
            return new PullRequestResponse { Error = $"Validação da PR falhou na API: {DescribeApiError(e)}" };
        }
        catch (ApiException e)
        {
            return new PullRequestResponse { Error = $"Erro na API do GitHub: {DescribeApiError(e)}" };
        }
        catch (Exception)
        {
            return new PullRequestResponse { Error = "Erro ao criar PR" };
        }
    }

    public async Task<PullRequestResponse?> UpdatePullRequestAsync(string repository, int number, string title, string? descriptionRaw, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(repository) || number <= 0 || string.IsNullOrWhiteSpace(title))
            return new PullRequestResponse { Error = "Parâmetro 'repository', 'number' ou 'title' é obrigatório" };
        var (owner, repo) = ResolveRepository(repository);
        if (string.IsNullOrWhiteSpace(owner))
            return new PullRequestResponse { Error = "Configurações do GitHub (owner) não encontradas" };

        var update = new PullRequestUpdate
        {
            Title = title,
            // String vazia limpa a descrição no GitHub; null manteria a anterior.
            Body = NormalizeBody(descriptionRaw) ?? string.Empty
        };

        try
        {
            var pr = await Client.PullRequest.Update(owner, repo, number, update);
            CacheStatus(pr, repository);
            return ToPullRequestResponse(pr, repository);
        }
        catch (NotFoundException)
        {
            return new PullRequestResponse { Error = $"PR #{number} não encontrado no repositório '{repository}'" };
        }
        catch (ApiException e)
        {
            return new PullRequestResponse { Error = $"Erro na API do GitHub: {DescribeApiError(e)}" };
        }
    }

    /// <summary>
    /// Todos os repositórios (não arquivados) que o usuário do token acessa — próprios, de
    /// colaborador e das organizações. Id = nome quando o dono é o Owner do plugin (formato já
    /// usado nos registros existentes) e "dono/nome" para os demais.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryResponse>> ListRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);

        var owner = Config.GetConfigurationValue("Owner") ?? string.Empty;

        return await _cacheService.GetOrCreateAsync<IReadOnlyList<RepositoryResponse>>(
            $"github:repositories:{_tokenScope}:{owner}",
            async () =>
            {
                var repositories = await Client.Repository.GetAllForCurrent(new RepositoryRequest
                {
                    Affiliation = RepositoryAffiliation.All,
                    Sort = RepositorySort.FullName
                });

                return repositories
                    .Where(r => !r.Archived)
                    .Select(r =>
                    {
                        var id = string.Equals(r.Owner?.Login, owner, StringComparison.OrdinalIgnoreCase) ? r.Name : r.FullName;
                        return new RepositoryResponse
                        {
                            Id = id,
                            Label = id,
                            Private = r.Private,
                            DefaultBranch = r.DefaultBranch ?? string.Empty
                        };
                    })
                    // Repos do Owner do plugin primeiro, depois os demais; alfabético dentro de cada grupo.
                    .OrderBy(r => r.Id.Contains('/'))
                    .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            },
            RepositoriesCacheMinutes);
    }

    public async Task<IReadOnlyList<PullRequestStatusResponse>> GetPullRequestsStatusAsync(IEnumerable<(string Repository, int Number)> pullRequests, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        await EnsureClientAsync(cancellationToken);

        var owner = Config.GetConfigurationValue("Owner");
        var targets = pullRequests
            .Where(p => !string.IsNullOrWhiteSpace(p.Repository) && p.Number > 0)
            .Distinct()
            .ToList();

        if (targets.Count == 0)
            return Array.Empty<PullRequestStatusResponse>();
        if (string.IsNullOrWhiteSpace(owner))
            return targets.Select(t => new PullRequestStatusResponse
            {
                Repository = t.Repository, Number = t.Number, Error = "Configurações do GitHub (owner) não encontradas"
            }).ToList();

        // Consultas em paralelo com limite, para não estourar o rate limit secundário do GitHub.
        using var throttle = new SemaphoreSlim(StatusMaxParallelism);
        var tasks = targets.Select(async t =>
        {
            var cacheKey = StatusCacheKey(owner, t.Repository, t.Number);
            if (!bypassCache && _cacheService.TryGetValue<PullRequestStatusResponse>(cacheKey, out var cached) && cached is not null)
                return cached;

            await throttle.WaitAsync(cancellationToken);
            try
            {
                // Octokit não aceita CancellationToken: o timeout evita que um PR lento segure a listagem.
                var (prOwner, prRepo) = ResolveRepository(t.Repository);
                var pr = await Client.PullRequest.Get(prOwner, prRepo, t.Number)
                    .WaitAsync(StatusRequestTimeout, cancellationToken);
                var status = new PullRequestStatusResponse
                {
                    Repository = t.Repository,
                    Number = t.Number,
                    Status = PullRequestGithubStatus.From(pr.State.StringValue, pr.Merged),
                    IsDraft = pr.Draft,
                    MergedAt = pr.MergedAt,
                    ClosedAt = pr.ClosedAt
                };
                _cacheService.Set(cacheKey, status, StatusCacheMinutes);
                return status;
            }
            catch (NotFoundException)
            {
                return new PullRequestStatusResponse { Repository = t.Repository, Number = t.Number, Error = "PR não encontrado" };
            }
            catch (ApiException e)
            {
                return new PullRequestStatusResponse { Repository = t.Repository, Number = t.Number, Error = DescribeApiError(e) };
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout ao consultar status do PR {Repository}#{Number} no GitHub", t.Repository, t.Number);
                return new PullRequestStatusResponse { Repository = t.Repository, Number = t.Number, Error = "Tempo esgotado ao consultar o GitHub" };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Falha de rede etc.: devolve o item com erro (o chamador usa o status persistido).
                _logger.LogWarning(e, "Falha ao consultar status do PR {Repository}#{Number} no GitHub", t.Repository, t.Number);
                return new PullRequestStatusResponse { Repository = t.Repository, Number = t.Number, Error = "Falha ao consultar o GitHub" };
            }
            finally
            {
                throttle.Release();
            }
        });

        var result = await Task.WhenAll(tasks);
        LogRateLimit();
        return result;
    }

    /// <summary>
    /// Id de repositório do CIME → (dono, nome): "dono/nome" para repositórios de outros donos;
    /// só "nome" para os do Owner configurado no plugin.
    /// </summary>
    private (string Owner, string Repo) ResolveRepository(string? repository)
    {
        var defaultOwner = Config.GetConfigurationValue("Owner") ?? string.Empty;
        var value = repository?.Trim() ?? string.Empty;
        var slash = value.IndexOf('/');
        return slash > 0 && slash < value.Length - 1
            ? (value[..slash], value[(slash + 1)..])
            : (defaultOwner, value);
    }

    /// <summary>Loga o rate limit restante da última chamada (Warning quando está acabando).</summary>
    private void LogRateLimit()
    {
        var rateLimit = Client.GetLastApiInfo()?.RateLimit;
        if (rateLimit is null) return;

        if (rateLimit.Remaining < RateLimitWarningThreshold)
            _logger.LogWarning("Rate limit do GitHub baixo: {Remaining}/{Limit} (reset {Reset:u})", rateLimit.Remaining, rateLimit.Limit, rateLimit.Reset);
        else
            _logger.LogDebug("Rate limit do GitHub: {Remaining}/{Limit}", rateLimit.Remaining, rateLimit.Limit);
    }

    private async Task<PullRequest?> FindOpenPullRequestAsync(string owner, string repo, string head, string @base)
    {
        var request = new PullRequestRequest
        {
            State = ItemStateFilter.Open,
            Head = $"{owner}:{head}",
            Base = @base
        };
        var prs = await Client.PullRequest.GetAllForRepository(owner, repo, request, new ApiOptions { PageSize = 10, PageCount = 1 });
        return prs.FirstOrDefault();
    }

    private static bool IsPullRequestAlreadyExists(ApiValidationException e) =>
        e.ApiError?.Errors?.Any(x => x.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true) == true;

    private static string DescribeApiError(ApiException e)
    {
        var details = e.ApiError?.Errors?
            .Select(x => x.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        var message = e.ApiError?.Message ?? e.Message;
        return details is { Count: > 0 } ? $"{message} ({string.Join("; ", details)})" : message;
    }

    private static string? NormalizeBody(string? descriptionRaw) =>
        string.IsNullOrWhiteSpace(descriptionRaw) ? null
            : descriptionRaw.Replace("\u0000", string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();

    private string StatusCacheKey(string owner, string repository, int number) =>
        $"github:pr-status:{_tokenScope}:{owner}/{repository}#{number}";

    /// <summary>
    /// O CIME acabou de ler o PR (criar/atualizar): grava o status fresco no cache para que uma
    /// listagem logo em seguida não devolva um status antigo (ex.: draft que já virou aberto).
    /// </summary>
    private void CacheStatus(PullRequest pr, string repositoryId)
    {
        var owner = Config.GetConfigurationValue("Owner") ?? string.Empty;
        _cacheService.Set(StatusCacheKey(owner, repositoryId, pr.Number), new PullRequestStatusResponse
        {
            Repository = repositoryId,
            Number = pr.Number,
            Status = PullRequestGithubStatus.From(pr.State.StringValue, pr.Merged),
            IsDraft = pr.Draft,
            MergedAt = pr.MergedAt,
            ClosedAt = pr.ClosedAt
        }, StatusCacheMinutes);
    }

    private static PullRequestResponse ToPullRequestResponse(PullRequest pr, string repository) =>
        new()
        {
            Id = pr.Id,
            Number = pr.Number,
            Repository = repository,
            Title = pr.Title,
            Body = pr.Body,
            State = pr.State.StringValue,
            Status = PullRequestGithubStatus.From(pr.State.StringValue, pr.Merged),
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

    public async Task<CardReferencesResponse?> GetCardReferencesAsync(string cardNumber, int maxPerType, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);

        var owner = Config.GetConfigurationValue("Owner");
        var repo = Config.GetConfigurationValue("Repo");
        var token = Config.GetConfigurationValue("Token");
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CardReferencesResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(cardNumber))
            return new CardReferencesResponse { Error = "Parâmetro 'cardNumber' é obrigatório" };

        var branchesTask = Client.Repository.Branch.GetAll(owner, repo)
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
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
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
        var codeTask = Client.Search.SearchCode(new SearchCodeRequest(codeQuery))
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

    public async Task<CommitDiffResponse?> GetCommitDiffAsync(string sha, CancellationToken cancellationToken = default, string? repository = null)
    {
        await EnsureClientAsync(cancellationToken);

        var (owner, repo) = ResolveRepository(repository ?? Config.GetConfigurationValue("Repo"));
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CommitDiffResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(sha))
            return new CommitDiffResponse { Error = "Parâmetro 'sha' é obrigatório" };

        try
        {
            var commit = await Client.Repository.Commit.Get(owner, repo, sha);
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
        catch (ApiException e)
        {
            return new CommitDiffResponse { Error = "Erro ao acessar GitHub API" };
        }
        catch (Exception e)
        {
            throw new Exception($"Erro ao buscar commit diff: {e.Message}");
        }
    }

    public async Task<List<BranchCommitResponse>> GetBranchCommitsAsync(string repository, string branch, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);

        var (owner, repo) = ResolveRepository(string.IsNullOrWhiteSpace(repository)
            ? Config.GetConfigurationValue("Repo")
            : repository);

        var request = new CommitRequest { Sha = branch };
        var options = new ApiOptions { PageSize = 20, PageCount = 1 };

        var commits = await Client.Repository.Commit.GetAll(owner, repo, request, options);

        return commits.Select(c =>
        {
            var message = c.Commit.Message ?? string.Empty;
            var lines = message.Split('\n', 2, StringSplitOptions.None);
            var title = lines[0].Trim();
            var description = lines.Length > 1 ? lines[1].Trim() : null;
            if (string.IsNullOrWhiteSpace(description))
                description = null;

            return new BranchCommitResponse
            {
                Sha = c.Sha,
                Title = title,
                Description = description,
                Author = c.Commit.Author?.Name ?? c.Author?.Login ?? string.Empty,
                Date = c.Commit.Author?.Date.ToString("o") ?? string.Empty,
                Url = c.HtmlUrl
            };
        }).ToList();
    }

    public async Task<CompareDiffResponse?> CompareRefsDiffAsync(string @base, string head, CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken);

        var owner = Config.GetConfigurationValue("Owner");
        var repo = Config.GetConfigurationValue("Repo");
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new CompareDiffResponse { Error = "Configurações do GitHub (owner/repo) não encontradas" };
        if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head))
            return new CompareDiffResponse { Error = "Parâmetro '@base' ou 'head' é obrigatório" };

        try
        {
            var compare = await Client.Repository.Commit.Compare(owner, repo, @base, head);
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


