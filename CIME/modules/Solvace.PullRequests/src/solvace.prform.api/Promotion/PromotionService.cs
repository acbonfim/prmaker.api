using System.Text.RegularExpressions;
using Cime.BuildingBlocks.RealTime;
using Microsoft.EntityFrameworkCore;
using solvace.azure.application.Contract;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.RealTime;
using solvace.github.application.Contract;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Enums;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Promotion;
using solvace.prform.domain.Requests;
using solvace.prform.Infra.Contexts;
using solvace.prform.Skills;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Requests;
using PrDomainException = solvace.prform.domain.Entities.DomainException;

namespace solvace.prform.Promotion;

/// <summary>
/// 0074: promoção de PRs em lote. A prévia sugere repositório, branch principal, commits e destinos por card; a criação
/// enfileira um item por PR; o executor do usuário faz o cherry-pick (git local) e o PRMake abre o PR com o GitHub do
/// próprio usuário, registrando no card como qualquer outro PR. Nunca mescla.
/// </summary>
public class PromotionService(
    DefaultContext context,
    IGitHubService github,
    IPullRequestGithubApplication githubApplication,
    IAzureService azure,
    ITimelineApplication timeline,
    IPluginConfigurationResolver configurationResolver,
    IExecutionQueueApplication queue,
    IRealTimeNotifier realTime,
    ILogger<PromotionService> logger)
{
    /// <summary>Quantos itens "direct" (só API, sem git) o servidor abre na própria requisição; o resto fica para o executor.</summary>
    private const int MaxInlineDirect = 20;
    private const int MaxCommitsInPreview = 10;
    private const int MaxBaseCandidates = 6;
    private static readonly Regex ReleaseLike = new(@"^(release|hotfix)[-/_]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Guid InlineWorker = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    // ── Configuração ─────────────────────────────────────────────────────────────────────────────

    private record Settings(PromotionProfileSet Profiles, string BranchStrategy, string BranchNamePattern, string PrTitlePattern, string PromotionBranchPattern);

    private async Task<Settings> LoadSettingsAsync(CancellationToken ct)
    {
        try
        {
            var config = await configurationResolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, ct);
            string V(string key, string fallback = "") => config.GetConfigurationValueOrDefault(key, string.Empty) is { Length: > 0 } v ? v : fallback;
            return new Settings(
                PromotionProfileSet.Parse(V(SkillsConfigurationKeys.PromotionProfiles)),
                V(SkillsConfigurationKeys.BranchStrategy),
                V(SkillsConfigurationKeys.BranchNamePattern, "hotfix/{card}"),
                V(SkillsConfigurationKeys.PrTitlePattern, "AB#{card} {TARGET}"),
                V(SkillsConfigurationKeys.PromotionBranchPattern, "{branch}-{target}"));
        }
        catch (InvalidOperationException)
        {
            // "Skills Configurations" ainda não existe: padrões, sem perfis (a tela avisa).
            return new Settings(PromotionProfileSet.Empty, string.Empty, "hotfix/{card}", "AB#{card} {TARGET}", "{branch}-{target}");
        }
    }

    // ── Prévia ───────────────────────────────────────────────────────────────────────────────────

    public async Task<PromotionPreviewResponse> PreviewAsync(PromotionPreviewRequest request, Guid userId, CancellationToken ct)
    {
        var version = PromotionTargetResolver.NormalizeVersion(request.Version)
                      ?? throw new PrDomainException("Informe a versão do lote (ex.: 3.274.10).");
        var cards = NormalizeCards(request.Cards);
        if (cards.Count == 0)
            throw new PrDomainException("Informe ao menos um card.");
        if (cards.Count > PromotionBatch.MaxCards)
            throw new PrDomainException($"O lote aceita no máximo {PromotionBatch.MaxCards} cards.");

        var settings = await LoadSettingsAsync(ct);
        var summaries = await CardSummariesAsync(cards.Select(c => c.Card), ct);

        var cardNumbers = cards.Select(c => c.Card).ToList();
        var prRows = await context.PullRequestsGithub.AsNoTracking()
            .Where(x => cardNumbers.Contains(x.CardNumber))
            .Select(x => new KnownPr(x.CardNumber, x.RepositoryId, x.BranchPrefix + x.BranchName, x.TargetBranch, x.Status, x.Url))
            .ToListAsync(ct);

        var response = new PromotionPreviewResponse
        {
            Version = version,
            ExecutorOnline = await HasOnlineExecutorAsync(userId, ct),
            Profiles = settings.Profiles.All.Select(p => new PromotionProfileResponse
            {
                Kind = p.Key,
                Label = p.Label,
                Targets = p.Targets.Select(t => new PromotionTargetDto { Branch = t.Branch, Draft = t.Draft, Mode = t.Mode }).ToList()
            }).ToList()
        };

        foreach (var card in cards)
        {
            summaries.TryGetValue(card.Card, out var summary);
            var known = prRows.Where(r => r.Card == card.Card).ToList();
            var repos = !string.IsNullOrWhiteSpace(card.Repository)
                ? [card.Repository.Trim()]
                : known.Select(r => r.Repository).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var options = known.Select(r => r.Repository).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (repos.Count == 0)
            {
                var row = BaseRow(card.Card, summary);
                row.RepositoryOptions = options;
                row.Errors.Add("O card ainda não tem PR registrado: informe o repositório.");
                response.Rows.Add(row);
                continue;
            }

            foreach (var repo in repos)
            {
                var row = BaseRow(card.Card, summary);
                row.Repository = repo;
                row.RepositoryOptions = options;
                await FillRowAsync(row, known.Where(r => string.Equals(r.Repository, repo, StringComparison.OrdinalIgnoreCase)).ToList(),
                    settings, version, request.Profiles, request.Refresh, ct);
                response.Rows.Add(row);
            }
        }
        return response;
    }

    private record KnownPr(string Card, string Repository, string Branch, string Target, string Status, string Url);

    private static PromotionPreviewRow BaseRow(string card, AzureSummary? summary) => new()
    {
        Card = card,
        Title = summary?.Title ?? string.Empty,
        WorkItemType = summary?.WorkItemType,
        AssignedTo = summary?.AssignedTo,
        State = summary?.State
    };

    private async Task FillRowAsync(PromotionPreviewRow row, List<KnownPr> known, Settings settings, string version,
        Dictionary<string, List<PromotionTargetDto>>? overrides, bool refresh, CancellationToken ct)
    {
        IReadOnlyList<string> branches;
        try
        {
            branches = await github.ListBranchesAsync(row.Repository!, ct, bypassCache: refresh);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Promoção: não consegui listar as branches de {Repository}", row.Repository);
            row.Errors.Add($"Não consegui consultar o GitHub ({row.Repository}): {e.Message}");
            return;
        }

        // Perfil do tipo do repositório (o lote pode sobrepor o que o PRMake tem).
        var kind = BranchStrategyKinds.KindOf(row.Repository, settings.BranchStrategy);
        row.Kind = kind;
        List<PromotionTarget>? templates = null;
        if (kind is not null && overrides is not null && overrides.FirstOrDefault(o => string.Equals(o.Key, kind, StringComparison.OrdinalIgnoreCase)) is { Value: { Count: > 0 } custom })
        {
            templates = custom.Select(t => new PromotionTarget(t.Branch, t.Draft, PromotionMode.Normalize(t.Mode))).ToList();
            row.ProfileLabel = settings.Profiles.Find(kind)?.Label is { } l ? $"{l} (do lote)" : "Do lote";
        }
        else if (settings.Profiles.Find(kind) is { } profile)
        {
            templates = profile.Targets.ToList();
            row.ProfileLabel = profile.Label;
        }
        else
        {
            row.Warnings.Add(kind is null
                ? $"O repositório {row.Repository} não casa com nenhuma regra do BranchStrategy: escolha os destinos."
                : $"Não há perfil de destino para o tipo '{kind}': escolha os destinos.");
        }

        // Branch recém-criada pode não estar na lista em cache (5 min): se algo não foi achado, consulta de novo uma vez.
        if (!refresh && (MissingPrincipal(branches, settings, row.Card) || MissingTarget(branches, settings, templates, version)))
        {
            try { branches = await github.ListBranchesAsync(row.Repository!, ct, bypassCache: true); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Promoção: não consegui atualizar as branches de {Repository}", row.Repository); }
        }

        // Branch principal: o padrão de nome do PRMake (hotfix/{card}) ou, na falta, bugfix/{card}.
        var principal = new[] { settings.BranchNamePattern, "bugfix/{card}" }
            .Select(p => p.Replace("{card}", row.Card, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(b => branches.Contains(b, StringComparer.Ordinal));
        if (principal is null)
        {
            row.SourceBranch = settings.BranchNamePattern.Replace("{card}", row.Card, StringComparison.OrdinalIgnoreCase);
            row.Errors.Add($"Branch principal não encontrada em {row.Repository} ({row.SourceBranch}).");
        }
        else
        {
            row.SourceBranch = principal;
            row.SourceFound = true;
            // Candidatas a base (de onde a principal foi cortada): os destinos do perfil que existem no remoto entram na disputa.
            var profileBranches = (templates ?? [])
                .Select(t => PromotionTargetResolver.Resolve(t, version, branches.ToList()).Branch)
                .Where(b => b is not null).Cast<string>().ToList();
            await FillSourceAsync(row, known, branches, profileBranches, ct);
        }

        row.Targets.AddRange(BuildTargets(templates ?? [], version, branches, row.SourceBase, row.SourceBranch, known));

        if (row.SourceFound && row.CommitCount == 0 && !row.Errors.Any())
            row.Warnings.Add("Não encontrei commits da branch principal em relação à base — confira antes de disparar.");
    }

    private static bool MissingPrincipal(IReadOnlyList<string> branches, Settings settings, string card) =>
        !new[] { settings.BranchNamePattern, "bugfix/{card}" }
            .Select(p => p.Replace("{card}", card, StringComparison.OrdinalIgnoreCase))
            .Any(b => branches.Contains(b, StringComparer.Ordinal));

    private static bool MissingTarget(IReadOnlyList<string> branches, Settings settings, List<PromotionTarget>? templates, string version) =>
        (templates ?? []).Any(t => PromotionTargetResolver.Resolve(t, version, branches.ToList()).Status != PromotionTargetStatus.Resolved);

    /// <summary>Resolve os modelos de destino contra as branches do remoto e marca o que já tem PR aberto.</summary>
    private static List<PromotionPreviewTarget> BuildTargets(IEnumerable<PromotionTarget> templates, string version, IReadOnlyList<string> branches,
        string? sourceBase, string? sourceBranch, List<KnownPr> known)
    {
        var result = new List<PromotionPreviewTarget>();
        foreach (var template in templates)
        {
            var resolved = PromotionTargetResolver.Resolve(template, version, branches.ToList());
            var target = new PromotionPreviewTarget
            {
                Template = resolved.Template,
                Branch = resolved.Branch,
                Status = resolved.Status,
                Candidates = resolved.Candidates.ToList(),
                Draft = resolved.Draft,
                Mode = resolved.Mode
            };

            if (resolved.Branch is not null)
            {
                // A principal nasceu deste destino: cherry-pick sobre ele mesmo não muda nada — o PR é da própria branch.
                if (sourceBase is not null && string.Equals(resolved.Branch, sourceBase, StringComparison.OrdinalIgnoreCase)
                    && target.Mode == PromotionMode.CherryPick)
                {
                    target.Mode = PromotionMode.Direct;
                    target.Note = $"A branch {sourceBranch} foi cortada de {sourceBase}: PR direto, sem cherry-pick.";
                }
                var open = known.FirstOrDefault(k => string.Equals(k.Target, resolved.Branch, StringComparison.OrdinalIgnoreCase)
                                                     && k.Status == PullRequestGithubStatus.Open);
                if (open is not null)
                {
                    target.AlreadyOpen = true;
                    target.ExistingUrl = open.Url;
                }
            }
            result.Add(target);
        }
        return result;
    }

    /// <summary>Resolve destinos digitados na tela (adicionar um destino a uma linha) sem refazer a prévia inteira.</summary>
    public async Task<List<PromotionPreviewTarget>> ResolveAsync(PromotionResolveRequest request, CancellationToken ct)
    {
        var version = PromotionTargetResolver.NormalizeVersion(request.Version)
                      ?? throw new PrDomainException("Informe a versão do lote (ex.: 3.274.10).");
        var repo = request.Repository?.Trim() ?? string.Empty;
        if (repo.Length == 0)
            throw new PrDomainException("Informe o repositório.");
        if (request.Targets.Count == 0 || request.Targets.Count > 8)
            throw new PrDomainException("Informe de 1 a 8 destinos.");

        var card = NormalizeCard(request.Card);
        var known = (await context.PullRequestsGithub.AsNoTracking()
                .Where(x => x.CardNumber == card)
                .Select(x => new KnownPr(x.CardNumber, x.RepositoryId, x.BranchPrefix + x.BranchName, x.TargetBranch, x.Status, x.Url))
                .ToListAsync(ct))
            .Where(k => string.Equals(k.Repository, repo, StringComparison.OrdinalIgnoreCase)).ToList();
        var branches = await github.ListBranchesAsync(repo, ct);
        var templates = request.Targets
            .Select(t => new PromotionTarget(PromotionMessageParser.NormalizeTemplate(t.Branch) ?? throw new PrDomainException($"Nome de branch inválido: '{t.Branch}'."),
                t.Draft, PromotionMode.Normalize(t.Mode)))
            .ToList();
        var built = BuildTargets(templates, version, branches, request.SourceBase, request.SourceBranch, known);
        if (built.Any(t => t.Status != PromotionTargetStatus.Resolved))
        {
            // Branch recém-criada pode não estar na lista em cache: consulta de novo uma vez.
            branches = await github.ListBranchesAsync(repo, ct, bypassCache: true);
            built = BuildTargets(templates, version, branches, request.SourceBase, request.SourceBranch, known);
        }
        return built;
    }

    /// <summary>Base de corte (a que deixa menos commits à frente) e os commits da branch principal em relação a ela.</summary>
    private async Task FillSourceAsync(PromotionPreviewRow row, List<KnownPr> known, IReadOnlyList<string> branches, IReadOnlyList<string> profileBranches, CancellationToken ct)
    {
        // A base é a branch (master ou uma release) de que a principal saiu: a que deixa MENOS commits à frente. Ordem de
        // tentativa: master, os destinos dos PRs já registrados do card, os destinos do perfil e, por fim, as branches
        // release/hotfix mais novas — no máximo MaxBaseCandidates comparações.
        var candidates = new List<string> { "master" };
        candidates.AddRange(known.Where(k => string.Equals(k.Branch, row.SourceBranch, StringComparison.Ordinal)).Select(k => k.Target));
        candidates.AddRange(profileBranches);
        candidates.AddRange(branches.Where(b => ReleaseLike.IsMatch(b))
            .OrderByDescending(b => b, StringComparer.OrdinalIgnoreCase));
        var bases = candidates
            .Where(b => !string.Equals(b, row.SourceBranch, StringComparison.Ordinal))
            .Select(b => branches.FirstOrDefault(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase)))
            .Where(b => b is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxBaseCandidates)
            .ToList();

        solvace.github.domain.Responses.BranchDeltaResponse? best = null;
        foreach (var candidate in bases)
        {
            try
            {
                var delta = await github.GetBranchDeltaAsync(row.Repository!, candidate, row.SourceBranch!, ct);
                if (delta is not null && (best is null || delta.AheadBy < best.AheadBy))
                    best = delta;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Promoção: compare {Base}...{Head} falhou em {Repository}", candidate, row.SourceBranch, row.Repository);
            }
        }

        if (best is null)
        {
            row.Warnings.Add("Não consegui calcular os commits da branch principal.");
            return;
        }
        row.SourceBase = best.Base;
        row.CommitCount = best.Commits.Count;
        row.CommitsTruncated = best.Truncated;
        row.Commits = best.Commits.Take(MaxCommitsInPreview).Select(c => new PromotionCommitResponse { Sha = c.Sha, Title = c.Title }).ToList();
        if (best.Truncated)
            row.Warnings.Add("A branch tem muitos commits: a lista está cortada (o cherry-pick leva todos).");
    }

    // ── Criação e reenvio ────────────────────────────────────────────────────────────────────────

    public async Task<PromotionBatchResponse> CreateAsync(PromotionCreateRequest request, Guid userId, string userName, CancellationToken ct)
    {
        if (request.Items.Count == 0)
            throw new PrDomainException("O lote está vazio.");
        if (request.Items.Select(i => i.Card.Trim()).Distinct().Count() > PromotionBatch.MaxCards)
            throw new PrDomainException($"O lote aceita no máximo {PromotionBatch.MaxCards} cards.");

        var now = DateTimeOffset.UtcNow;
        var batch = new PromotionBatch(request.Version, request.Kind, userId, userName, now);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var branchesByRepo = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Items)
        {
            var card = NormalizeCard(item.Card);
            var repo = item.Repository?.Trim() ?? string.Empty;
            if (card.Length == 0 || repo.Length == 0)
                throw new PrDomainException("Todo item precisa de card e repositório.");
            if (item.Targets.Count == 0)
                throw new PrDomainException($"O card {card} ({repo}) não tem destino.");

            if (!branchesByRepo.TryGetValue(repo, out var branches))
                branchesByRepo[repo] = branches = await github.ListBranchesAsync(repo, ct);
            var wanted = new[] { item.SourceBranch?.Trim() ?? string.Empty }.Concat(item.Targets.Select(t => t.Branch?.Trim() ?? string.Empty));
            if (wanted.Any(w => !branches.Contains(w, StringComparer.Ordinal)))
                branchesByRepo[repo] = branches = await github.ListBranchesAsync(repo, ct, bypassCache: true); // pode ser branch recém-criada
            if (!branches.Contains(item.SourceBranch?.Trim() ?? string.Empty, StringComparer.Ordinal))
                throw new PrDomainException($"A branch principal {item.SourceBranch} não existe em {repo} (card {card}).");

            foreach (var target in item.Targets)
            {
                var name = target.Branch?.Trim() ?? string.Empty;
                if (!branches.Contains(name, StringComparer.Ordinal))
                    throw new PrDomainException($"A branch de destino '{name}' não existe em {repo} (card {card}).");
                if (!seen.Add($"{card}|{repo}|{name}"))
                    throw new PrDomainException($"O card {card} aparece duas vezes para {name} em {repo}.");

                batch.AddItem(new PromotionItem(card, item.Title, repo, item.SourceBranch!.Trim(), item.SourceBase, name, name,
                    target.Draft, target.Mode, now));
            }
        }

        context.PromotionBatches.Add(batch);
        await context.SaveChangesAsync(ct);
        await DispatchAsync(batch, userId, ct);
        return batch.ToResponse();
    }

    public async Task<PromotionBatchResponse> RetryAsync(Guid batchId, PromotionRetryRequest request, Guid userId, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchId, ct);
        EnsureOwner(batch, userId);
        var only = request.ItemIds is { Count: > 0 } ? request.ItemIds.ToHashSet() : null;
        var now = DateTimeOffset.UtcNow;

        var retried = 0;
        foreach (var item in batch.Items.Where(i => i.IsRetryable && (only is null || only.Contains(i.Id))))
        {
            item.Retry(now);
            retried++;
        }
        if (retried == 0)
            throw new PrDomainException("Nada para reenviar neste lote.");

        batch.Refresh(now);
        await context.SaveChangesAsync(ct);
        await DispatchAsync(batch, userId, ct);
        return batch.ToResponse();
    }

    public async Task<PromotionBatchResponse> CancelAsync(Guid batchId, Guid userId, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchId, ct);
        EnsureOwner(batch, userId);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in batch.Items.Where(i => i.Status == PromotionItemStatus.Queued))
            item.Cancel(now);
        batch.Refresh(now);
        await context.SaveChangesAsync(ct);
        return batch.ToResponse();
    }

    /// <summary>Itens "direct" saem aqui mesmo (só API); os de cherry-pick esperam o executor, que é acordado.</summary>
    private async Task DispatchAsync(PromotionBatch batch, Guid owner, CancellationToken ct)
    {
        // Idempotência: PR aberto do mesmo card/repositório/destino não se repete (nem passa pelo executor).
        var reused = false;
        foreach (var queued in batch.Items.Where(i => i.Status == PromotionItemStatus.Queued).ToList())
            reused |= await TryReuseExistingAsync(queued, ct);
        if (reused)
        {
            batch.Refresh(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(ct);
        }

        var direct = batch.Items.Where(i => i.Status == PromotionItemStatus.Queued && i.Mode == PromotionMode.Direct).Take(MaxInlineDirect).ToList();
        if (direct.Count > 0)
        {
            var settings = await LoadSettingsAsync(ct);
            foreach (var item in direct)
            {
                try
                {
                    item.Claim(InlineWorker, DateTimeOffset.UtcNow);
                    await context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await context.Entry(item).ReloadAsync(ct); // o executor pegou antes
                    continue;
                }
                await OpenPullRequestAsync(batch, item, item.SourceBranch, settings, owner, ct);
            }
        }

        if (batch.Items.Any(i => i.Status == PromotionItemStatus.Queued))
        {
            try
            {
                await realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.WorkerGroup(owner), ExecutionPlanRealTimeEvents.EventQueueReady, null, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Promoção: não consegui acordar os executores de {Owner}", owner);
            }
        }
    }

    // ── Consulta ─────────────────────────────────────────────────────────────────────────────────

    public async Task<PromotionBatchResponse> GetAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchId, ct);
        // Item preso com a trava vencida volta para a fila mesmo com o executor sumido.
        if (await ExpireLeasesAsync(batch.Items, ct))
        {
            batch.Refresh(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(ct);
        }
        return batch.ToResponse();
    }

    public async Task<List<PromotionBatchResponse>> ListMineAsync(Guid userId, int take, CancellationToken ct)
    {
        var batches = await context.PromotionBatches.AsNoTracking().Include(b => b.Items)
            .Where(b => b.OwnerUserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);
        return batches.Select(b => b.ToResponse()).ToList();
    }

    public async Task<bool> HasOnlineExecutorAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            return (await queue.GetWorkersAsync(userId, ct)).Any(w => w.Online && w.Status == "active");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Promoção: não consegui consultar os executores de {Owner}", userId);
            return false;
        }
    }

    // ── Executor ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Entrega o próximo item do dono (um repositório por vez) ou null.</summary>
    public async Task<PromotionClaimResponse?> NextForExecutorAsync(Guid owner, Guid workerId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var open = await context.PromotionItems
            .Where(i => i.Batch!.OwnerUserId == owner && (i.Status == PromotionItemStatus.Queued || i.Status == PromotionItemStatus.Running))
            .OrderBy(i => i.Batch!.CreatedAt).ThenBy(i => i.Position)
            .Include(i => i.Batch)
            .ToListAsync(ct);
        if (await ExpireLeasesAsync(open, ct))
            open = open.Where(i => i.IsActive).ToList();

        var busy = open.Where(i => i.Status == PromotionItemStatus.Running && i.WorkerId != InlineWorker)
            .Select(i => i.RepositoryId.ToLowerInvariant()).ToHashSet();
        PromotionItem? next = null;
        foreach (var candidate in open.Where(i => i.Status == PromotionItemStatus.Queued && !busy.Contains(i.RepositoryId.ToLowerInvariant())))
        {
            // Outro lote (ou o fluxo normal) abriu o PR enquanto este esperava na fila: não repete.
            if (await TryReuseExistingAsync(candidate, ct))
            {
                candidate.Batch!.Refresh(now);
                await context.SaveChangesAsync(ct);
                continue;
            }
            next = candidate;
            break;
        }
        if (next is null)
            return null;

        try
        {
            next.Claim(workerId, now);
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // outro executor pegou: o próximo aviso tenta de novo
        }

        var settings = await LoadSettingsAsync(ct);
        return new PromotionClaimResponse
        {
            ItemId = next.Id,
            BatchId = next.BatchId,
            Version = next.Batch!.Version,
            Card = next.CardNumber,
            Repository = next.RepositoryId,
            SourceBranch = next.SourceBranch,
            SourceBase = next.SourceBase,
            Target = next.TargetBranch,
            Mode = next.Mode,
            NewBranch = BuildNewBranchName(settings.PromotionBranchPattern, next.SourceBranch, next.TargetBranch)
        };
    }

    public async Task<PromotionHeartbeatResponse> HeartbeatAsync(Guid itemId, Guid owner, Guid workerId, CancellationToken ct)
    {
        var item = await LoadItemAsync(itemId, owner, ct);
        if (item.Status != PromotionItemStatus.Running || item.WorkerId != workerId)
            return new PromotionHeartbeatResponse { Stop = true };
        item.Heartbeat(workerId, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(ct);
        return new PromotionHeartbeatResponse();
    }

    public async Task<PromotionItemResponse> ReportAsync(Guid itemId, Guid owner, Guid workerId, PromotionReportRequest report, CancellationToken ct)
    {
        var item = await LoadItemAsync(itemId, owner, ct);
        if (item.Status != PromotionItemStatus.Running || item.WorkerId != workerId)
            throw new PrDomainException("Este item não está rodando neste executor.");

        var now = DateTimeOffset.UtcNow;
        var batch = item.Batch!;
        switch (report.Outcome)
        {
            case PromotionReportOutcome.Pushed:
                if (string.IsNullOrWhiteSpace(report.NewBranch))
                    throw new PrDomainException("Informe a branch empurrada.");
                item.RecordCommits(report.CommitCount, report.NewBranch);
                await context.SaveChangesAsync(ct);
                await OpenPullRequestAsync(batch, item, report.NewBranch.Trim(), await LoadSettingsAsync(ct), owner, ct);
                break;
            case PromotionReportOutcome.Conflict:
                item.RecordCommits(report.CommitCount, report.NewBranch);
                item.MarkConflict(report.ConflictFiles, report.Message, now);
                break;
            case PromotionReportOutcome.MissingTarget:
                item.Finish(PromotionItemStatus.MissingTarget, report.Message ?? $"A branch {item.TargetBranch} não existe no remoto.", now);
                break;
            case PromotionReportOutcome.NoChanges:
                item.RecordCommits(0, null);
                item.Finish(PromotionItemStatus.NoChanges, report.Message ?? "Os commits já estão no destino.", now);
                break;
            case PromotionReportOutcome.Error:
                item.Finish(PromotionItemStatus.Error, report.Message ?? "Erro no executor.", now);
                break;
            default:
                throw new PrDomainException($"Resultado desconhecido: '{report.Outcome}'.");
        }

        batch.Refresh(now);
        await context.SaveChangesAsync(ct);
        return item.ToResponse();
    }

    // ── PR ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Abre (ou reaproveita) o PR do item e fecha o item com o resultado. Nunca lança: o erro vira a situação do item.</summary>
    private async Task OpenPullRequestAsync(PromotionBatch batch, PromotionItem item, string head, Settings settings, Guid owner, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (await TryReuseExistingAsync(item, ct))
            {
                batch.Refresh(now);
                await context.SaveChangesAsync(ct);
                return;
            }

            var title = settings.PrTitlePattern
                .Replace("{card}", item.CardNumber, StringComparison.OrdinalIgnoreCase)
                .Replace("{TARGET}", item.TargetBranch.ToUpperInvariant(), StringComparison.Ordinal)
                .Replace("{target}", item.TargetBranch, StringComparison.Ordinal);

            var pr = await githubApplication.Open(item.CardNumber, new OpenPullRequestGithubRequest
            {
                RepositoryId = item.RepositoryId,
                BranchPrefix = string.Empty,
                BranchName = head,
                TargetBranch = item.TargetBranch,
                Title = title,
                Description = await BuildDescriptionAsync(item, batch.Version, ct),
                Draft = item.Draft,
                UserId = owner
            }, ct);

            item.MarkOpened(pr.Number ?? 0, pr.Url, pr.Id, pr.AlreadyExisted, now);
            await WriteTimelineAsync(item, batch.Version, owner, pr.AlreadyExisted, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (e is not PrDomainException)
                logger.LogError(e, "Promoção: falha ao abrir o PR do card {Card} para {Target}", item.CardNumber, item.TargetBranch);
            DiscardPendingPullRequestEntities();
            item.Finish(PromotionItemStatus.Error, e switch
            {
                PrDomainException => e.Message,
                DbUpdateException => "O PR foi aberto, mas não consegui registrá-lo no PRMake (banco). Reenvie o item.",
                _ => $"Erro ao abrir o PR: {e.Message}"
            }, now);
        }

        batch.Refresh(now);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O DefaultContext é o mesmo do <see cref="IPullRequestGithubApplication"/>: se o SaveChanges dele falhou, o registro do
    /// card e o PR que ele tentou gravar continuam rastreados e fariam o SaveChanges seguinte (o do item) falhar de novo.
    /// </summary>
    private void DiscardPendingPullRequestEntities()
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.Entity is PullRequestRegister or PullRequestGithub).ToList())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
                entry.State = EntityState.Unchanged;
        }
    }

    /// <summary>PR aberto do mesmo card para o mesmo repositório e destino (de outro lote, do fluxo normal ou deste mesmo).</summary>
    private async Task<PullRequestGithub?> FindOpenPullRequestAsync(PromotionItem item, CancellationToken ct)
    {
        var repo = item.RepositoryId.ToLower();
        var target = item.TargetBranch.ToLower();
        return await context.PullRequestsGithub.AsNoTracking()
            .Where(x => x.CardNumber == item.CardNumber && x.Status == PullRequestGithubStatus.Open && x.GithubPrNumber != null
                        && x.RepositoryId.ToLower() == repo && x.TargetBranch.ToLower() == target)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Já existe PR aberto para este item? Fecha o item como "já existia" (sem executor, sem duplicar) e devolve true.</summary>
    private async Task<bool> TryReuseExistingAsync(PromotionItem item, CancellationToken ct)
    {
        var existing = await FindOpenPullRequestAsync(item, ct);
        if (existing is null)
            return false;
        item.MarkOpened(existing.GithubPrNumber!.Value, existing.Url, existing.Id, alreadyExisted: true, DateTimeOffset.UtcNow);
        return true;
    }

    private async Task<string> BuildDescriptionAsync(PromotionItem item, string version, CancellationToken ct)
    {
        var description = await context.PullRequests.AsNoTracking()
            .Where(p => p.CardNumber == item.CardNumber).Select(p => p.Description).FirstOrDefaultAsync(ct);
        var how = item.Mode == PromotionMode.Direct
            ? $"PR da branch `{item.SourceBranch}`"
            : $"Cherry-pick de {item.CommitCount} commit(s) de `{item.SourceBranch}`";
        var note = $"Promoção {version}: {how} → `{item.TargetBranch}`.";
        return string.IsNullOrWhiteSpace(description) ? note : $"{description.Trim()}\n\n---\n{note}";
    }

    private async Task WriteTimelineAsync(PromotionItem item, string version, Guid owner, bool alreadyExisted, CancellationToken ct)
    {
        try
        {
            await timeline.CreateAsync(new CreateTimelineEntryRequest
            {
                CardNumber = item.CardNumber,
                Description = $"PR #{item.PullRequestNumber} {(alreadyExisted ? "já existente registrado" : "aberto")} pela promoção {version}: " +
                              $"{item.RepositoryId} ({item.NewBranch ?? item.SourceBranch} → {item.TargetBranch}) — {item.PullRequestUrl}"
            }, owner, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Promoção: PR aberto, mas a timeline do card {Card} não foi atualizada", item.CardNumber);
        }
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────────────────────────

    private async Task<bool> ExpireLeasesAsync(IEnumerable<PromotionItem> items, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var item in items.Where(i => i.Status == PromotionItemStatus.Running).ToList())
            changed |= item.ExpireLease(now);
        if (changed)
            await context.SaveChangesAsync(ct);
        return changed;
    }

    private async Task<PromotionBatch> LoadBatchAsync(Guid id, CancellationToken ct) =>
        await context.PromotionBatches.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct)
        ?? throw new PrDomainException("Lote não encontrado.");

    private async Task<PromotionItem> LoadItemAsync(Guid id, Guid owner, CancellationToken ct)
    {
        var item = await context.PromotionItems.Include(i => i.Batch).FirstOrDefaultAsync(i => i.Id == id, ct)
                   ?? throw new PrDomainException("Item não encontrado.");
        if (item.Batch!.OwnerUserId != owner)
            throw new PrDomainException("Este item é de outro usuário.");
        return item;
    }

    private static void EnsureOwner(PromotionBatch batch, Guid userId)
    {
        if (batch.OwnerUserId != userId)
            throw new PrDomainException("Só quem criou o lote pode alterá-lo.");
    }

    private async Task<Dictionary<string, AzureSummary>> CardSummariesAsync(IEnumerable<string> cards, CancellationToken ct)
    {
        var ids = cards.Where(c => long.TryParse(c, out _)).Select(long.Parse).Select(l => (int)l).Distinct().ToList();
        var map = new Dictionary<string, AzureSummary>();
        if (ids.Count == 0)
            return map;
        try
        {
            foreach (var s in await azure.GetCardsSummaryAsync(ids, ct))
                map[s.Id.ToString()] = new AzureSummary(s.Title, s.WorkItemType, s.AssignedTo, s.State);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem o DevOps a prévia segue só com o que o PRMake sabe do card.
            logger.LogWarning(e, "Promoção: não consegui ler os cards no Azure DevOps");
        }
        return map;
    }

    private record AzureSummary(string? Title, string? WorkItemType, string? AssignedTo, string? State);

    /// <summary>"AB#77317", " 77317 " → "77317".</summary>
    public static string NormalizeCard(string? raw)
    {
        var v = raw?.Trim() ?? string.Empty;
        if (v.StartsWith("AB#", StringComparison.OrdinalIgnoreCase))
            v = v[3..];
        return v.Trim();
    }

    private static List<PromotionPreviewCard> NormalizeCards(IEnumerable<PromotionPreviewCard> cards) =>
        cards.Select(c => new PromotionPreviewCard { Card = NormalizeCard(c.Card), Repository = c.Repository })
            .Where(c => c.Card.Length > 0)
            .GroupBy(c => c.Card + "|" + (c.Repository ?? string.Empty).ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

    /// <summary>Nome da branch do cherry-pick a partir do padrão ({branch}, {target}); "/" do destino vira "-" para não criar pasta de ref.</summary>
    public static string BuildNewBranchName(string pattern, string sourceBranch, string target)
    {
        var safeTarget = Regex.Replace(target.Trim(), @"[^A-Za-z0-9._-]+", "-").Trim('-');
        var name = (string.IsNullOrWhiteSpace(pattern) ? "{branch}-{target}" : pattern)
            .Replace("{branch}", sourceBranch, StringComparison.OrdinalIgnoreCase)
            .Replace("{target}", safeTarget, StringComparison.OrdinalIgnoreCase);
        return name.Length <= PromotionItem.MaxBranchLength ? name : name[..PromotionItem.MaxBranchLength];
    }
}
