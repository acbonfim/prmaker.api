using System.Globalization;
using System.Text.Json;
using Cime.BuildingBlocks.RealTime;
using Microsoft.EntityFrameworkCore;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.infra.Contexts;
using solvace.github.application.Contract;
using solvace.prform.Skills;

namespace solvace.prform.AwsDeploy;

/// <summary>0100: a pessoa pediu a coleta sem executor ativo (a tela mostra o comando manual) → 409 <c>NO_EXECUTOR</c>.</summary>
public class AwsNoExecutorException() : DomainException(
    "Você não tem executor (prmake-agent) ativo agora — rode a coleta no seu terminal: bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card>.")
{
    public const string Code = "NO_EXECUTOR";
}

/// <summary>
/// 0100: aba AWS na Bancada. A API não fala com a AWS (Cloud Run, sem credencial): guarda o índice de pipelines
/// (compartilhado), o último retrato de cada card e a fila de coleta, que roda só no executor de quem pediu (regra 78723).
/// Toda leitura é para qualquer pessoa autenticada; o retrato e o índice são gravados pelo script da máquina.
/// </summary>
public class AwsDeployService(
    ExecutionPlanContext db,
    SkillsRulesService rules,
    IPullRequestGithubApplication pullRequests,
    IRealTimeNotifier realTime,
    ILogger<AwsDeployService> logger)
{
    private static readonly string[] CodeDeliverables =
        [WorkDeliverableKind.PullRequest, WorkDeliverableKind.Branch, WorkDeliverableKind.Commit, WorkDeliverableKind.Code];

    // ── Configuração ─────────────────────────────────────────────────────────────────────────────

    public async Task<AwsDeployConfigResponse> GetConfigAsync(CancellationToken ct) =>
        AwsDeploySettings.From(await rules.SettingsOnlyAsync(ct));

    // ── Índice de pipelines ──────────────────────────────────────────────────────────────────────

    public async Task<AwsPipelineIndexResponse> GetIndexAsync(CancellationToken ct)
    {
        var rows = await db.AwsPipelines.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new { p.Name, p.Updated, p.SeenAt }).ToListAsync(ct);
        return new AwsPipelineIndexResponse
        {
            UpdatedAt = rows.Count == 0 ? null : rows.Max(r => r.SeenAt),
            Items = rows.Select(r => new AwsPipelineIndexItem { Name = r.Name, Updated = r.Updated }).ToList()
        };
    }

    public async Task<AwsPipelineIndexResult> UpsertIndexAsync(AwsPipelineIndexRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await db.AwsPipelines.Select(p => p.Name).ToListAsync(ct);
        var plan = AwsPipelineIndex.Build(existing, request.Upserts, request.Removed, request.Full);
        var now = DateTimeOffset.UtcNow;

        var names = plan.Upserts.Select(u => u.Name).ToList();
        var tracked = names.Count == 0 ? [] : await db.AwsPipelines.Where(p => names.Contains(p.Name)).ToDictionaryAsync(p => p.Name, ct);
        foreach (var dto in plan.Upserts)
        {
            if (tracked.TryGetValue(dto.Name, out var row))
                row.Apply(dto, request.Account, request.Region, now);
            else
                db.AwsPipelines.Add(new AwsPipeline(dto, request.Account, request.Region, now));
        }
        await db.SaveChangesAsync(ct);

        var removed = 0;
        if (plan.Remove.Count > 0)
        {
            var remove = plan.Remove.ToList();
            removed = await db.AwsPipelines.Where(p => remove.Contains(p.Name)).ExecuteDeleteAsync(ct);
        }
        if (plan.Skipped.Count > 0)
            logger.LogWarning("AWS: {Count} pipeline(s) recusadas no índice (sem nome ou > 16 KB): {Names}", plan.Skipped.Count,
                string.Join(", ", plan.Skipped.Take(10)));

        return new AwsPipelineIndexResult
        {
            Total = await db.AwsPipelines.CountAsync(ct),
            Upserted = plan.Upserts.Count,
            Removed = removed,
            Skipped = plan.Skipped.ToList()
        };
    }

    public async Task<List<AwsPipelineDto>> ListByRepositoryAsync(string? repository, CancellationToken ct)
    {
        var repo = repository?.Trim();
        if (string.IsNullOrEmpty(repo))
            throw new DomainException("Informe o repositório (ex.: electradv/edv-solvace-apps).");
        var key = AwsPipeline.RepositoryKey(repo);
        var rows = await db.AwsPipelines.AsNoTracking().Where(p => p.RepositoryKeys.Contains(key)).OrderBy(p => p.Name).ToListAsync(ct);
        return rows.Select(r => r.ToDto()).ToList();
    }

    // ── Contexto do card (para o script) ─────────────────────────────────────────────────────────

    public async Task<AwsCardContextResponse> GetContextAsync(string card, CancellationToken ct)
    {
        var c = AwsRefresh.NormalizeCard(card);
        var repos = new Dictionary<string, AwsContextRepository>(StringComparer.OrdinalIgnoreCase);
        AwsContextRepository Repo(string name)
        {
            var n = name.Trim();
            if (!repos.TryGetValue(n, out var r))
                repos[n] = r = new AwsContextRepository { Repository = n };
            return r;
        }
        static void AddDistinct(List<string> list, string? value)
        {
            var v = value?.Trim();
            if (!string.IsNullOrEmpty(v) && !list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
        }

        // PRs do registro (sem ir ao GitHub: status como o PRMake conhece).
        try
        {
            foreach (var pr in await pullRequests.ListByCard(c, refreshStatus: false, ct))
            {
                if (string.IsNullOrWhiteSpace(pr.RepositoryId)) continue;
                var r = Repo(pr.RepositoryId);
                var head = pr.BranchPrefix + pr.BranchName;
                AddDistinct(r.Branches, head);
                r.Prs.Add(new AwsContextPr { Number = pr.Number, Base = pr.TargetBranch, Head = head, Status = PrStatus(pr.Status), Url = pr.Url });
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "AWS: PRs do card {Card} indisponíveis para o contexto", c);
        }

        // Entregas dos itens (0091): branch, commit, código (commits no Data) e PR.
        var deliverables = await db.WorkItemDeliverables.AsNoTracking()
            .Where(d => d.CardNumber == c && CodeDeliverables.Contains(d.Kind) && d.Repository != null)
            .Select(d => new { d.Kind, d.Repository, d.Branch, d.Target, d.Sha, d.Number, d.Url, d.Status, d.DataJson })
            .ToListAsync(ct);
        foreach (var d in deliverables)
        {
            var r = Repo(d.Repository!);
            AddDistinct(r.Branches, d.Branch);
            AddDistinct(r.Shas, d.Sha);
            foreach (var sha in CommitShas(d.DataJson)) AddDistinct(r.Shas, sha);
            if (d.Kind == WorkDeliverableKind.PullRequest && d.Number is { } n && !r.Prs.Any(p => p.Number == n))
                r.Prs.Add(new AwsContextPr { Number = n, Base = d.Target, Head = d.Branch, Status = PrStatus(d.Status), Url = d.Url });
        }

        // Regras do PRMake: branch principal e as de cada destino do fluxo (nunca inventadas aqui).
        string? title = null;
        try
        {
            var r = await rules.GetAsync(c, ct);
            title = r.Title;
            if (repos.Count == 0 && !string.IsNullOrWhiteSpace(r.DefaultRepository))
                Repo(r.DefaultRepository);
            foreach (var repo in repos.Values)
            {
                AddDistinct(repo.Branches, r.MainBranch);
                foreach (var t in SkillsRulesService.PlanForRepository(r, repo.Repository).Targets)
                    AddDistinct(repo.Branches, t.Branch);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "AWS: regras do card {Card} indisponíveis para o contexto", c);
        }

        return new AwsCardContextResponse
        {
            Card = c,
            Title = title,
            Repositories = repos.Values.OrderBy(r => r.Repository, StringComparer.OrdinalIgnoreCase).ToList(),
            Markers = [$"AB#{c}"],
            Environments = (await GetConfigAsync(ct)).Environments
        };
    }

    private static string? PrStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "OPEN" => "open",
        "MERGED" => "merged",
        "CLOSED" => "closed",
        null or "" => null,
        var s => s.ToLowerInvariant()
    };

    private static IEnumerable<string> CommitShas(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(dataJson); }
        catch (JsonException) { yield break; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("commits", out var commits)
                || commits.ValueKind != JsonValueKind.Array)
                yield break;
            foreach (var commit in commits.EnumerateArray())
                if (commit.ValueKind == JsonValueKind.Object && commit.TryGetProperty("sha", out var sha) && sha.ValueKind == JsonValueKind.String
                    && sha.GetString() is { Length: > 0 } s)
                    yield return s;
        }
    }

    // ── Retrato do card ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Grava o retrato (≤ 2 MB), conclui o pedido de <c>requestId</c> (se é de quem grava) e, com ambiente em andamento,
    /// agenda o acompanhamento para a mesma pessoa.
    /// </summary>
    public async Task<AwsCardResponse> PutSnapshotAsync(string card, JsonElement body, Guid? userId, string userName, CancellationToken ct)
    {
        var c = AwsRefresh.NormalizeCard(card);
        if (body.ValueKind != JsonValueKind.Object)
            throw new DomainException("O retrato precisa ser um objeto JSON.");
        if (body.TryGetProperty("card", out var bodyCard) && bodyCard.ValueKind is JsonValueKind.String or JsonValueKind.Number
            && !string.Equals(bodyCard.ToString().Trim(), c, StringComparison.Ordinal))
            throw new DomainException($"O retrato é do card {bodyCard} e foi enviado para o card {c}.");

        var json = body.GetRawText();
        var now = DateTimeOffset.UtcNow;
        var collectedAt = Str(body, "collectedAt") is { } at && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : now;
        if (collectedAt > now.AddMinutes(5)) collectedAt = now; // relógio da máquina adiantado
        var version = body.TryGetProperty("version", out var v) && v.TryGetInt32(out var vi) ? vi : 1;
        long? durationMs = body.TryGetProperty("durationMs", out var dm) && dm.TryGetInt64(out var dl) ? dl : null;
        var deploying = HasVerdict(body, AwsVerdict.Deploying);

        var snapshot = await db.AwsCardSnapshots.FirstOrDefaultAsync(s => s.CardNumber == c, ct);
        if (snapshot is null)
            db.AwsCardSnapshots.Add(snapshot = new AwsCardSnapshot(c, json, collectedAt, userName, userId, version, now));
        else
            snapshot.Replace(json, collectedAt, userName, userId, version, now);

        // O pedido que esta coleta atendeu (só o de quem grava: o retrato chega pela credencial dele).
        AwsRefresh? finished = null;
        if (Guid.TryParse(Str(body, "requestId"), out var requestId))
        {
            var refresh = await db.AwsRefreshes.FirstOrDefaultAsync(r => r.Id == requestId && r.CardNumber == c, ct);
            if (refresh is not null && refresh.RequestedByUserId == userId && refresh.CompleteBySnapshot(durationMs, now))
                finished = refresh;
        }
        await db.SaveChangesAsync(ct);

        // Acompanhamento do deploy em andamento: mesma pessoa, daqui a N minutos, até o máximo.
        AwsRefresh? follow = null;
        if (deploying)
        {
            var owner = finished?.RequestedByUserId ?? userId;
            var hasActive = owner is { } o && await db.AwsRefreshes.AnyAsync(r => r.CardNumber == c && r.RequestedByUserId == o
                && (r.Status == AwsRefreshStatus.Queued || r.Status == AwsRefreshStatus.Claimed), ct);
            var config = await GetConfigAsync(ct);
            follow = AwsRefreshQueue.Follow(c, deploying, finished, userId, userName, hasActive, config.RefreshFollowMinutes,
                config.RefreshFollowMax, now);
            if (follow is not null)
            {
                db.AwsRefreshes.Add(follow);
                await db.SaveChangesAsync(ct);
            }
        }

        await NotifyCardAsync(c, follow?.Status ?? finished?.Status ?? "snapshot", ct);
        return await GetCardAsync(c, userId, ct);
    }

    private static bool HasVerdict(JsonElement body, string verdict) =>
        body.TryGetProperty("environments", out var envs) && envs.ValueKind == JsonValueKind.Array
        && envs.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object && Str(e, "verdict") == verdict);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<AwsCardResponse> GetCardAsync(string card, Guid? viewer, CancellationToken ct)
    {
        var c = AwsRefresh.NormalizeCard(card);
        await ExpireAsync(db.AwsRefreshes.Where(r => r.CardNumber == c), ct);

        var snapshot = await db.AwsCardSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.CardNumber == c, ct);
        var refresh = await CurrentRefreshAsync(c, viewer, ct);
        return new AwsCardResponse
        {
            Snapshot = snapshot is null ? null : Parse(snapshot.DataJson),
            CollectedAt = snapshot?.CollectedAt,
            CollectedBy = snapshot?.CollectedBy,
            Refresh = refresh is null ? null : AwsRefreshResponse.From(refresh),
            ExecutorOnline = viewer is { } u && await HasActiveExecutorAsync(u, ct)
        };
    }

    /// <summary>O pedido que a tela mostra: o ativo de quem olha, senão qualquer ativo, senão o último.</summary>
    private async Task<AwsRefresh?> CurrentRefreshAsync(string card, Guid? viewer, CancellationToken ct)
    {
        var recent = await db.AwsRefreshes.AsNoTracking().Where(r => r.CardNumber == card)
            .OrderByDescending(r => r.CreatedAt).Take(20).ToListAsync(ct);
        return recent.FirstOrDefault(r => r.IsActive && r.RequestedByUserId == viewer)
               ?? recent.FirstOrDefault(r => r.IsActive)
               ?? recent.FirstOrDefault();
    }

    private static JsonElement? Parse(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    /// <summary>Texto do <c>prmake_aws_status</c>.</summary>
    public async Task<string> StatusTextAsync(string card, CancellationToken ct)
    {
        var c = AwsRefresh.NormalizeCard(card);
        var snapshot = await db.AwsCardSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.CardNumber == c, ct);
        var active = await db.AwsRefreshes.AsNoTracking()
            .Where(r => r.CardNumber == c && (r.Status == AwsRefreshStatus.Queued || r.Status == AwsRefreshStatus.Claimed))
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        return AwsStatusText.Format(c, snapshot?.DataJson, snapshot?.CollectedAt, snapshot?.CollectedBy, active);
    }

    // ── Pedido de coleta (botão Atualizar) ───────────────────────────────────────────────────────

    /// <summary>
    /// Pede a coleta ao executor de quem pediu (nunca ao de outra pessoa). Um ativo por card + pessoa: repetir devolve o
    /// existente (o acompanhamento que esperava a hora sai já).
    /// </summary>
    public async Task<AwsRefreshResponse> RequestRefreshAsync(string card, Guid userId, string userName, CancellationToken ct)
    {
        var c = AwsRefresh.NormalizeCard(card);
        if (!await HasActiveExecutorAsync(userId, ct))
            throw new AwsNoExecutorException();
        var now = DateTimeOffset.UtcNow;
        await ExpireAsync(db.AwsRefreshes.Where(r => r.CardNumber == c && r.RequestedByUserId == userId), ct);

        var refresh = await db.AwsRefreshes
            .Where(r => r.CardNumber == c && r.RequestedByUserId == userId
                        && (r.Status == AwsRefreshStatus.Queued || r.Status == AwsRefreshStatus.Claimed))
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        if (refresh is null)
        {
            refresh = new AwsRefresh(c, userId, userName, AwsRefreshSource.Button, 0, null, now);
            db.AwsRefreshes.Add(refresh);
        }
        else
        {
            refresh.Expedite(now);
        }
        await db.SaveChangesAsync(ct);

        if (refresh.IsReady(now))
            await WakeAsync(userId, ct);
        await NotifyCardAsync(c, refresh.Status, ct);
        return AwsRefreshResponse.From(refresh);
    }

    private async Task<bool> HasActiveExecutorAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var workers = await db.Workers.AsNoTracking().Where(w => w.OwnerUserId == userId).ToListAsync(ct);
        return workers.Any(w => w.AcceptsWork(now));
    }

    // ── Executor ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Próximo pedido DO DONO da credencial (204 = nada). Regra 78723: nunca o pedido de outra pessoa.</summary>
    public async Task<AwsRefreshClaimResponse?> NextForExecutorAsync(Guid owner, Guid workerId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var open = await db.AwsRefreshes
            .Where(r => r.RequestedByUserId == owner && (r.Status == AwsRefreshStatus.Queued || r.Status == AwsRefreshStatus.Claimed))
            .ToListAsync(ct);
        if (await ExpireLoadedAsync(open, now, ct))
            open = open.Where(r => r.IsActive).ToList();

        var next = AwsRefreshQueue.PickNext(open, owner, now);
        if (next is null)
            return null;
        try
        {
            next.Claim(owner, workerId, now);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // outro executor da mesma pessoa pegou
        }
        await NotifyCardAsync(next.CardNumber, next.Status, ct);
        return new AwsRefreshClaimResponse
        {
            Id = next.Id, Card = next.CardNumber, RequestedBy = next.RequestedByName, Attempt = next.Attempt, Source = next.Source
        };
    }

    public async Task<bool> HeartbeatAsync(Guid id, Guid owner, Guid workerId, CancellationToken ct)
    {
        var refresh = await LoadOwnAsync(id, owner, ct);
        if (!refresh.Heartbeat(workerId, DateTimeOffset.UtcNow))
            return false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AwsRefreshResponse> ReportAsync(Guid id, Guid owner, Guid workerId, AwsRefreshReportRequest report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        var refresh = await LoadOwnAsync(id, owner, ct);
        refresh.Report(owner, workerId, report.Status ?? string.Empty, report.Error, report.DurationMs, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await NotifyCardAsync(refresh.CardNumber, refresh.Status, ct);
        return AwsRefreshResponse.From(refresh);
    }

    private async Task<AwsRefresh> LoadOwnAsync(Guid id, Guid owner, CancellationToken ct)
    {
        var refresh = await db.AwsRefreshes.FirstOrDefaultAsync(r => r.Id == id, ct)
                      ?? throw new DomainException("Pedido de coleta não encontrado.");
        if (refresh.RequestedByUserId != owner)
            throw new solvace.executionplans.application.Contracts.ExecutionForbiddenException(
                "Este pedido de coleta é de outra pessoa — só o executor de quem pediu pode tratá-lo.");
        return refresh;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────────────────────────

    private async Task ExpireAsync(IQueryable<AwsRefresh> scope, CancellationToken ct)
    {
        var open = await scope.Where(r => r.Status == AwsRefreshStatus.Queued || r.Status == AwsRefreshStatus.Claimed).ToListAsync(ct);
        await ExpireLoadedAsync(open, DateTimeOffset.UtcNow, ct);
    }

    private async Task<bool> ExpireLoadedAsync(IEnumerable<AwsRefresh> open, DateTimeOffset now, CancellationToken ct)
    {
        var changed = open.Where(r => r.ExpireIfStale(now)).Select(r => r.CardNumber).Distinct().ToList();
        if (changed.Count == 0) return false;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear(); // alguém mexeu ao mesmo tempo: a próxima leitura vê o estado certo
            return false;
        }
        foreach (var card in changed)
            await NotifyCardAsync(card, AwsRefreshStatus.Expired, ct);
        return true;
    }

    /// <summary>Acorda os executores da pessoa pelo relay (o mesmo sinal da fila e da promoção); o executor também consulta sozinho.</summary>
    private async Task WakeAsync(Guid owner, CancellationToken ct)
    {
        try
        {
            await realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.WorkerGroup(owner), ExecutionPlanRealTimeEvents.EventQueueReady, null, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "AWS: não consegui acordar os executores de {Owner}", owner);
        }
    }

    /// <summary>Sinal para a aba AWS (mesmo evento dos itens de trabalho, <c>action: "aws"</c>).</summary>
    private async Task NotifyCardAsync(string card, string status, CancellationToken ct)
    {
        try
        {
            await realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.Group(card), ExecutionPlanRealTimeEvents.EventWorkItemsUpdated,
                new { cardNumber = card, number = 0, taskNumber = (long?)null, action = ExecutionPlanRealTimeEvents.WorkItemActions.Aws, status }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "AWS: tempo real do card {Card} falhou", card);
        }
    }
}
