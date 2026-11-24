using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.executionplans.infra.Contexts;
using solvace.prform.domain.Enums;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): acompanhamento das entregas de um épico (<c>GET Epics/{n}/monitor</c> e MCP <c>prmake_epic_monitor</c>)
/// — por card: estado/coluna do DevOps, PRs do registro do PRMake e ambientes do retrato da AWS. Tudo em lote (uma consulta
/// por fonte, DevOps em blocos de 200), sem chamar o GitHub: o status do PR é o último que o PRMake gravou
/// (<c>statusSyncedAt</c>).
/// </summary>
public sealed class EpicMonitorService(
    IEpicApplication epics,
    DefaultContext db,
    ExecutionPlanContext plans,
    IAzureService azure,
    ILogger<EpicMonitorService> logger)
{
    /// <summary>Teto do <c>workitemsbatch</c> do DevOps por chamada.</summary>
    public const int DevOpsBatch = 200;

    public async Task<IReadOnlyList<EpicMonitorCard>> GetAsync(long number, CancellationToken ct)
    {
        var epic = await epics.GetAsync(number, ct);
        var cards = epic.Cards.Select(c => c.Card).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (cards.Count == 0) return [];

        var summaries = await SummariesAsync(cards, ct);

        var prs = await db.PullRequestsGithub.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber) && x.Status != PullRequestGithubStatus.Legacy)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new EpicMonitorPrRow(x.CardNumber, x.RepositoryId, x.GithubPrNumber, x.Url, x.Title, x.Status, x.IsDraft,
                x.TargetBranch, x.BranchName, x.StatusSyncedAt, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);

        var aws = await AwsAsync(cards, ct);
        return EpicMonitorMapping.Build(epic.Cards, summaries, prs, aws);
    }

    /// <summary>Estado/coluna/responsável em blocos; DevOps fora ou sem integração → sem esses campos (não derruba a tela).</summary>
    private async Task<IReadOnlyList<AzureCardSummaryResponse>> SummariesAsync(List<string> cards, CancellationToken ct)
    {
        var ids = solvace.prform.Controllers.DevOpsController.ParseIds(cards);
        var result = new List<AzureCardSummaryResponse>();
        try
        {
            foreach (var chunk in ids.Chunk(DevOpsBatch))
                result.AddRange(await azure.GetCardsSummaryAsync(chunk, ct));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Acompanhamento do épico: resumo dos cards no DevOps indisponível");
        }
        return result;
    }

    /// <summary>
    /// Retratos da AWS só com o que o acompanhamento usa (ambientes: chave, nome, veredito, mensagem e datas das pipelines) —
    /// o retrato inteiro tem logs e chega a 2 MB por card; o corte é feito no PostgreSQL (jsonb).
    /// </summary>
    private async Task<IReadOnlyList<EpicMonitorAwsRow>> AwsAsync(List<string> cards, CancellationToken ct)
    {
        var keys = cards.ToArray();
        return await plans.Database.SqlQuery<EpicMonitorAwsRow>($"""
            SELECT s."CardNumber" AS "Card", s."CollectedAt", s."CollectedBy",
              COALESCE((SELECT jsonb_agg(jsonb_build_object(
                  'key', e->'key', 'label', e->'label', 'verdict', e->'verdict', 'message', e->'message',
                  'pipelines', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                      'primary', p->'primary', 'verdict', p->'verdict', 'deployedAt', p->'deployedAt',
                      'lastRunAt', p->'lastRunAt', 'lastRunEndedAt', p->'lastRunEndedAt'))
                    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(e->'pipelines') = 'array' THEN e->'pipelines' ELSE '[]'::jsonb END) p),
                    '[]'::jsonb)) ORDER BY x.ord)
                FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s."Data"->'environments') = 'array' THEN s."Data"->'environments' ELSE '[]'::jsonb END)
                  WITH ORDINALITY AS x(e, ord)
                WHERE jsonb_typeof(x.e) = 'object'), '[]'::jsonb)::text AS "Environments"
            FROM execution."AwsCardSnapshots" s
            WHERE s."CardNumber" = ANY({keys})
            """).ToListAsync(ct);
    }
}

/// <summary>PR do registro (projeção sem a descrição).</summary>
public sealed record EpicMonitorPrRow(string CardNumber, string RepositoryId, int? Number, string Url, string Title, string Status, bool IsDraft,
    string TargetBranch, string BranchName, DateTimeOffset? StatusSyncedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

/// <summary>Retrato da AWS reduzido no banco (<c>Environments</c> = JSON dos ambientes).</summary>
public sealed class EpicMonitorAwsRow
{
    public string Card { get; set; } = string.Empty;
    public DateTimeOffset CollectedAt { get; set; }
    public string CollectedBy { get; set; } = string.Empty;
    public string Environments { get; set; } = "[]";
}

/// <summary>Uma linha do acompanhamento (contrato da aba Cards → "Acompanhamento").</summary>
public sealed class EpicMonitorCard
{
    public string Card { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>Tipo do DevOps (Bug, User Story…).</summary>
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? Column { get; set; }
    public string? AssignedTo { get; set; }
    public string? AssignedToImageUrl { get; set; }
    /// <summary>Link do card no DevOps (null sem o resumo do DevOps).</summary>
    public string? Url { get; set; }
    /// <summary>Papel do card no épico: created | linked | mentioned.</summary>
    public string Role { get; set; } = EpicCardRole.Linked;
    public List<EpicMonitorPr> Prs { get; set; } = [];
    /// <summary>null = sem retrato da AWS para o card.</summary>
    public List<EpicMonitorEnvironment>? Environments { get; set; }
    public DateTimeOffset? AwsCollectedAt { get; set; }
    public string? AwsCollectedBy { get; set; }
    /// <summary>Última novidade conhecida: DevOps, PR, retrato da AWS ou entrada no épico.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EpicMonitorPr
{
    public string Repository { get; set; } = string.Empty;
    public int? Number { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>open | merged | closed.</summary>
    public string Status { get; set; } = "open";
    public bool IsDraft { get; set; }
    public string? TargetBranch { get; set; }
    public string? Branch { get; set; }
    /// <summary>O registro não guarda a data do merge: null (use <see cref="StatusSyncedAt"/> — quando o PRMake viu o status).</summary>
    public DateTimeOffset? MergedAt { get; set; }
    public DateTimeOffset? StatusSyncedAt { get; set; }
}

public sealed class EpicMonitorEnvironment
{
    /// <summary>Chave do ambiente na configuração (dev, qa, sandboxefeso, sandboxrt, prod).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Nome para a tela (DEV, QA, Produção…).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>deployed | pending | failed | unknown.</summary>
    public string Status { get; set; } = EpicMonitorMapping.EnvUnknown;
    /// <summary>Veredito do retrato (deployed, waiting, deploying, absent, failed, not-applicable, unknown).</summary>
    public string? Verdict { get; set; }
    public string? Message { get; set; }
    /// <summary>Implantado: quando o nosso código entrou; senão a última execução da esteira.</summary>
    public DateTimeOffset? At { get; set; }
    public bool IsProduction { get; set; }
}

/// <summary>Montagem pura do acompanhamento (testável sem banco nem DevOps).</summary>
public static class EpicMonitorMapping
{
    public const string EnvDeployed = "deployed";
    public const string EnvPending = "pending";
    public const string EnvFailed = "failed";
    public const string EnvUnknown = "unknown";

    public static IReadOnlyList<EpicMonitorCard> Build(IReadOnlyList<EpicCardResponse> cards, IReadOnlyList<AzureCardSummaryResponse> summaries,
        IReadOnlyList<EpicMonitorPrRow> prs, IReadOnlyList<EpicMonitorAwsRow> aws)
    {
        var byId = summaries.GroupBy(s => s.Id.ToString(CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.First());
        var prsByCard = prs.GroupBy(p => p.CardNumber).ToDictionary(g => g.Key, g => g.ToList());
        var awsByCard = aws.GroupBy(a => a.Card).ToDictionary(g => g.Key, g => g.First());

        var result = new List<EpicMonitorCard>();
        foreach (var c in cards.GroupBy(c => c.Card).Select(g => g.First()))
        {
            byId.TryGetValue(c.Card, out var s);
            prsByCard.TryGetValue(c.Card, out var cardPrs);
            awsByCard.TryGetValue(c.Card, out var snap);
            var row = new EpicMonitorCard
            {
                Card = c.Card,
                Title = s?.Title ?? c.Title,
                Type = s?.WorkItemType ?? c.WorkItemType,
                State = s?.State,
                Column = s?.BoardColumn,
                AssignedTo = s?.AssignedTo,
                AssignedToImageUrl = s?.AssignedToImageUrl,
                Url = s is null || string.IsNullOrWhiteSpace(s.Url) ? null : s.Url,
                Role = c.Role,
                Prs = (cardPrs ?? []).Select(ToPr).ToList(),
                Environments = snap is null ? null : Environments(snap.Environments),
                AwsCollectedAt = snap?.CollectedAt,
                AwsCollectedBy = snap?.CollectedBy,
            };
            var dates = new List<DateTimeOffset> { c.AddedAt };
            if (s?.ChangedDate is { } changed) dates.Add(new DateTimeOffset(DateTime.SpecifyKind(changed, DateTimeKind.Utc)));
            foreach (var p in cardPrs ?? [])
            {
                dates.Add(p.CreatedAt);
                if (p.UpdatedAt is { } u) dates.Add(u);
                if (p.StatusSyncedAt is { } ss) dates.Add(ss);
            }
            if (snap is not null) dates.Add(snap.CollectedAt);
            row.UpdatedAt = dates.Max();
            result.Add(row);
        }
        return result.OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public static EpicMonitorPr ToPr(EpicMonitorPrRow p) => new()
    {
        Repository = p.RepositoryId,
        Number = p.Number,
        Url = p.Url,
        Title = p.Title,
        Status = PrStatus(p.Status),
        IsDraft = p.IsDraft,
        TargetBranch = string.IsNullOrWhiteSpace(p.TargetBranch) ? null : p.TargetBranch,
        Branch = string.IsNullOrWhiteSpace(p.BranchName) ? null : p.BranchName,
        MergedAt = null,
        StatusSyncedAt = p.StatusSyncedAt,
    };

    /// <summary>OPEN/MERGED/CLOSED do registro → open/merged/closed.</summary>
    public static string PrStatus(string? status) => status switch
    {
        PullRequestGithubStatus.Merged => "merged",
        PullRequestGithubStatus.Closed => "closed",
        _ => "open",
    };

    /// <summary>Veredito do retrato → selo do acompanhamento.</summary>
    public static string EnvStatus(string? verdict) => verdict switch
    {
        AwsVerdict.Deployed => EnvDeployed,
        AwsVerdict.Waiting or AwsVerdict.Deploying or AwsVerdict.Absent => EnvPending,
        AwsVerdict.Failed => EnvFailed,
        _ => EnvUnknown,
    };

    public static bool IsProduction(string? key, string? label)
    {
        static string N(string? v) => (v ?? string.Empty).Trim().ToLowerInvariant();
        var k = N(key);
        var l = N(label);
        return k is "prod" or "production" or "producao" or "produção" || l.StartsWith("produ", StringComparison.Ordinal) || l == "prod";
    }

    /// <summary>Ambientes do retrato reduzido (JSON), ignorando o que não entender. JSON inválido → lista vazia.</summary>
    public static List<EpicMonitorEnvironment> Environments(string? json)
    {
        var result = new List<EpicMonitorEnvironment>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return result; }
        using (doc)
        {
            var root = doc.RootElement;
            // Aceita o retrato inteiro também ({environments:[...]}).
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("environments", out var inner)) root = inner;
            if (root.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in root.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var key = Str(e, "key") ?? string.Empty;
                var label = Str(e, "label") ?? key;
                var verdict = Str(e, "verdict");
                var status = EnvStatus(verdict);
                result.Add(new EpicMonitorEnvironment
                {
                    Key = key,
                    Name = label,
                    Status = status,
                    Verdict = verdict,
                    Message = Str(e, "message"),
                    At = EnvAt(e, status),
                    IsProduction = IsProduction(key, label),
                });
            }
        }
        return result;
    }

    /// <summary>Implantado: o <c>deployedAt</c> mais recente das esteiras principais; senão a última execução.</summary>
    private static DateTimeOffset? EnvAt(JsonElement env, string status)
    {
        if (!env.TryGetProperty("pipelines", out var pipes) || pipes.ValueKind != JsonValueKind.Array) return null;
        var all = pipes.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object).ToList();
        var main = all.Where(p => p.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True).ToList();
        var list = main.Count > 0 ? main : all;
        var dates = status == EnvDeployed
            ? list.Select(p => Date(p, "deployedAt") ?? Date(p, "lastRunEndedAt"))
            : list.Select(p => Date(p, "lastRunEndedAt") ?? Date(p, "lastRunAt"));
        return dates.Max();
    }

    /// <summary>Texto do <c>prmake_epic_monitor</c>: resumo + uma linha por card (estado, PRs, ambientes).</summary>
    public static string Text(long epic, string title, IReadOnlyList<EpicMonitorCard> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Acompanhamento do épico E{epic} — {title}");
        if (cards.Count == 0)
            return sb.AppendLine("Nenhum card no épico (adicione com prmake_epic_cards ou crie pela Mesa).").ToString().TrimEnd();
        var merged = cards.Count(c => c.Prs.Any(p => p.Status == "merged"));
        var prod = cards.Count(c => c.Environments?.Any(e => e.IsProduction && e.Status == EnvDeployed) == true);
        var withoutAws = cards.Count(c => c.Environments is null);
        sb.AppendLine($"{cards.Count} cards · {merged} com PR mesclado · {prod} em produção · {withoutAws} sem retrato da AWS.");
        sb.AppendLine("Legenda: ✓ implantado · ⏳ aguardando · ✕ falhou · ? sem informação. PRs: status gravado no PRMake (sem consultar o GitHub agora).");
        foreach (var c in cards)
        {
            var head = $"- #{c.Card} {c.Type ?? "?"} — {c.Title ?? "(sem título)"} · {c.State ?? "estado ?"}";
            if (!string.IsNullOrWhiteSpace(c.Column)) head += $" / {c.Column}";
            if (!string.IsNullOrWhiteSpace(c.AssignedTo)) head += $" · {c.AssignedTo}";
            sb.AppendLine(head);
            sb.AppendLine(c.Prs.Count == 0
                ? "  - PRs: nenhum registrado no PRMake"
                : "  - PRs: " + string.Join("; ", c.Prs.Select(p =>
                    $"{p.Repository}#{p.Number?.ToString(CultureInfo.InvariantCulture) ?? "?"} {p.Status}{(p.IsDraft ? " (rascunho)" : "")}" +
                    (p.TargetBranch is { Length: > 0 } t ? $" → {t}" : ""))));
            sb.AppendLine(c.Environments is null
                ? "  - Ambientes: sem retrato da AWS (peça a coleta na aba AWS do card — quem tem a CLI da AWS)"
                : c.Environments.Count == 0
                    ? $"  - Ambientes: retrato de {When(c.AwsCollectedAt)} sem ambientes"
                    : $"  - Ambientes (retrato de {When(c.AwsCollectedAt)}): " + string.Join(" · ", c.Environments.Select(e => $"{Icon(e.Status)} {e.Name}")));
        }
        return sb.ToString().TrimEnd();
    }

    private static string Icon(string status) => status switch
    {
        EnvDeployed => "✓",
        EnvPending => "⏳",
        EnvFailed => "✕",
        _ => "?",
    };

    private static string When(DateTimeOffset? d) =>
        d?.ToUniversalTime().ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "?";

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
