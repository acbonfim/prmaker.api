using solvace.prform.domain.Entities;

namespace solvace.prform.Promotion;

// ── Entrada ──────────────────────────────────────────────────────────────────────────────────────

public class PromotionTargetDto
{
    /// <summary>Modelo (<c>release-{version}</c>, <c>master</c>) na prévia; o nome real da branch na criação.</summary>
    public string Branch { get; set; } = string.Empty;
    public bool Draft { get; set; }
    /// <summary>cherry-pick | direct.</summary>
    public string? Mode { get; set; }
}

public class PromotionPreviewCard
{
    public string Card { get; set; } = string.Empty;
    /// <summary>Repositório informado pelo usuário (quando o card ainda não tem PR registrado ou ele quer outro).</summary>
    public string? Repository { get; set; }
}

public class PromotionPreviewRequest
{
    public string Version { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public List<PromotionPreviewCard> Cards { get; set; } = [];
    /// <summary>Perfis só deste lote (kind do repositório → destinos), sobrepondo os do PRMake — vem da mensagem do Release Manager.</summary>
    public Dictionary<string, List<PromotionTargetDto>>? Profiles { get; set; }
}

public class PromotionCreateItem
{
    public string Card { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Repository { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string? SourceBase { get; set; }
    public List<PromotionTargetDto> Targets { get; set; } = [];
}

public class PromotionCreateRequest
{
    public string Version { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public List<PromotionCreateItem> Items { get; set; } = [];
}

public class PromotionRetryRequest
{
    /// <summary>Itens a reenviar; vazio = todos os que podem ser reenviados.</summary>
    public List<Guid>? ItemIds { get; set; }
}

public class PromotionParseRequest
{
    public string Text { get; set; } = string.Empty;
}

// ── Saída ────────────────────────────────────────────────────────────────────────────────────────

public class PromotionProfileResponse
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<PromotionTargetDto> Targets { get; set; } = [];
}

public class PromotionCommitResponse
{
    public string Sha { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class PromotionPreviewTarget
{
    /// <summary>Modelo original (release-{version}).</summary>
    public string Template { get; set; } = string.Empty;
    /// <summary>Nome real no remoto (null = não resolvido).</summary>
    public string? Branch { get; set; }
    /// <summary>resolved | missing | ambiguous.</summary>
    public string Status { get; set; } = string.Empty;
    public List<string> Candidates { get; set; } = [];
    public bool Draft { get; set; }
    public string Mode { get; set; } = string.Empty;
    /// <summary>Já existe PR aberto deste card para este destino.</summary>
    public bool AlreadyOpen { get; set; }
    public string? ExistingUrl { get; set; }
    public string? Note { get; set; }
}

public class PromotionPreviewRow
{
    public string Card { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? WorkItemType { get; set; }
    public string? AssignedTo { get; set; }
    public string? State { get; set; }
    public string? Repository { get; set; }
    /// <summary>Repositórios que o card já usa (PRs registrados) — para a troca na tela.</summary>
    public List<string> RepositoryOptions { get; set; } = [];
    public string? Kind { get; set; }
    public string? ProfileLabel { get; set; }
    public string? SourceBranch { get; set; }
    public bool SourceFound { get; set; }
    public string? SourceBase { get; set; }
    public int CommitCount { get; set; }
    public bool CommitsTruncated { get; set; }
    public List<PromotionCommitResponse> Commits { get; set; } = [];
    public List<PromotionPreviewTarget> Targets { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class PromotionPreviewResponse
{
    public string Version { get; set; } = string.Empty;
    /// <summary>O usuário tem executor online (necessário para o cherry-pick).</summary>
    public bool ExecutorOnline { get; set; }
    public List<PromotionProfileResponse> Profiles { get; set; } = [];
    public List<PromotionPreviewRow> Rows { get; set; } = [];
}

public class PromotionItemResponse
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public string Card { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string? SourceBase { get; set; }
    public string Target { get; set; } = string.Empty;
    public bool Draft { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? NewBranch { get; set; }
    public List<string> ConflictFiles { get; set; } = [];
    public int CommitCount { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public class PromotionSummaryResponse
{
    public int Total { get; set; }
    public int Queued { get; set; }
    public int Running { get; set; }
    public int Opened { get; set; }
    public int AlreadyOpen { get; set; }
    public int Conflict { get; set; }
    public int MissingTarget { get; set; }
    public int NoChanges { get; set; }
    public int Error { get; set; }
    public int Cancelled { get; set; }
}

public class PromotionBatchResponse
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Active { get; set; }
    public PromotionSummaryResponse Summary { get; set; } = new();
    public List<PromotionItemResponse> Items { get; set; } = [];
}

public class PromotionParsedCard
{
    public string Card { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
}

public class PromotionParsedRule
{
    /// <summary>O tipo de ticket como o Release Manager escreveu ("Netcore", "API De Integrações").</summary>
    public string TicketType { get; set; } = string.Empty;
    /// <summary>Tipo de repositório (kind do BranchStrategy) a que a regra vale; null = não identificado.</summary>
    public string? Kind { get; set; }
    public List<PromotionTargetDto> Targets { get; set; } = [];
    /// <summary>ai | text.</summary>
    public string Source { get; set; } = string.Empty;
}

public class PromotionParseResponse
{
    public string? Version { get; set; }
    public string? Kind { get; set; }
    public List<PromotionParsedCard> Cards { get; set; } = [];
    public List<PromotionParsedRule> Rules { get; set; } = [];
    /// <summary>Perfis só deste lote (kind → destinos), prontos para o <c>profiles</c> da prévia.</summary>
    public Dictionary<string, List<PromotionTargetDto>> Profiles { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool AiUsed { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
}

// ── Executor ─────────────────────────────────────────────────────────────────────────────────────

/// <summary>O que o executor precisa para rodar um item (cherry-pick local).</summary>
public class PromotionClaimResponse
{
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Card { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string? SourceBase { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    /// <summary>Nome sugerido para a branch do cherry-pick (o executor acrescenta -2, -3… se já existir).</summary>
    public string NewBranch { get; set; } = string.Empty;
}

public class PromotionReportRequest
{
    /// <summary>pushed | conflict | missing_target | no_changes | error.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? Message { get; set; }
    /// <summary>Branch empurrada (outcome = pushed). No modo direct, a própria branch principal.</summary>
    public string? NewBranch { get; set; }
    public int CommitCount { get; set; }
    public List<string>? ConflictFiles { get; set; }
}

public class PromotionHeartbeatResponse
{
    /// <summary>O item foi cancelado ou o executor perdeu a vez — pare.</summary>
    public bool Stop { get; set; }
}

public static class PromotionReportOutcome
{
    public const string Pushed = "pushed";
    public const string Conflict = "conflict";
    public const string MissingTarget = "missing_target";
    public const string NoChanges = "no_changes";
    public const string Error = "error";
}

public static class PromotionMapping
{
    public static PromotionItemResponse ToResponse(this PromotionItem i) => new()
    {
        Id = i.Id,
        Position = i.Position,
        Card = i.CardNumber,
        Title = i.CardTitle,
        Repository = i.RepositoryId,
        SourceBranch = i.SourceBranch,
        SourceBase = i.SourceBase,
        Target = i.TargetBranch,
        Draft = i.Draft,
        Mode = i.Mode,
        Status = i.Status,
        Message = i.Message,
        NewBranch = i.NewBranch,
        ConflictFiles = string.IsNullOrWhiteSpace(i.ConflictFiles) ? [] : i.ConflictFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
        CommitCount = i.CommitCount,
        PullRequestNumber = i.PullRequestNumber,
        PullRequestUrl = i.PullRequestUrl,
        StartedAt = i.StartedAt,
        FinishedAt = i.FinishedAt
    };

    public static PromotionBatchResponse ToResponse(this PromotionBatch b)
    {
        var items = b.Items.OrderBy(i => i.Position).ToList();
        int Count(string status) => items.Count(i => i.Status == status);
        return new PromotionBatchResponse
        {
            Id = b.Id,
            Version = b.Version,
            Kind = b.Kind,
            OwnerUserId = b.OwnerUserId,
            OwnerName = b.OwnerName,
            CreatedAt = b.CreatedAt,
            FinishedAt = b.FinishedAt,
            Active = items.Any(i => i.IsActive),
            Summary = new PromotionSummaryResponse
            {
                Total = items.Count,
                Queued = Count(PromotionItemStatus.Queued),
                Running = Count(PromotionItemStatus.Running),
                Opened = Count(PromotionItemStatus.Opened),
                AlreadyOpen = Count(PromotionItemStatus.AlreadyOpen),
                Conflict = Count(PromotionItemStatus.Conflict),
                MissingTarget = Count(PromotionItemStatus.MissingTarget),
                NoChanges = Count(PromotionItemStatus.NoChanges),
                Error = Count(PromotionItemStatus.Error),
                Cancelled = Count(PromotionItemStatus.Cancelled)
            },
            Items = items.Select(i => i.ToResponse()).ToList()
        };
    }
}
