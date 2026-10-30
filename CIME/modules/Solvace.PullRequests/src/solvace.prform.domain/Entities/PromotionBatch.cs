using solvace.prform.domain.Promotion;

namespace solvace.prform.domain.Entities;

/// <summary>Situação de um item do lote (0074). Compartilhada com o frontend e com o executor — mantenha em sincronia.</summary>
public static class PromotionItemStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    /// <summary>PR aberto.</summary>
    public const string Opened = "opened";
    /// <summary>Já existia PR aberto para este card/repositório/destino (nada duplicado).</summary>
    public const string AlreadyOpen = "already_open";
    /// <summary>O cherry-pick bateu em conflito (arquivos em <see cref="PromotionItem.ConflictFiles"/>).</summary>
    public const string Conflict = "conflict";
    /// <summary>A branch de destino não existe no remoto.</summary>
    public const string MissingTarget = "missing_target";
    /// <summary>Os commits já estão no destino (PR vazio não é aberto).</summary>
    public const string NoChanges = "no_changes";
    public const string Error = "error";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> Active = new HashSet<string> { Queued, Running };
    /// <summary>Itens que o usuário pode reenviar.</summary>
    public static readonly IReadOnlySet<string> Retryable = new HashSet<string> { Conflict, MissingTarget, Error, Cancelled };
    /// <summary>Itens em que há um PR do card para o destino (aberto agora ou antes).</summary>
    public static readonly IReadOnlySet<string> HasPullRequest = new HashSet<string> { Opened, AlreadyOpen };

    public static bool IsActive(string status) => Active.Contains(status);
}

/// <summary>
/// 0074: lote de promoção — vários cards com a branch principal já pronta, cada um com os destinos (release, hotfix,
/// edge, master…) para os quais o Release Manager pediu o PR. Um item por card × repositório × destino.
/// </summary>
public class PromotionBatch
{
    public const int MaxVersionLength = 40;
    public const int MaxKindLength = 20;
    public const int MaxOwnerNameLength = 200;
    public const int MaxCards = 30;
    public const int MaxItems = 200;

    private readonly List<PromotionItem> _items = [];

    public Guid Id { get; private set; }
    /// <summary>Versão da release/hotfix (ex.: 3.274.10).</summary>
    public string Version { get; private set; } = string.Empty;
    /// <summary>hotfix | release (informativo).</summary>
    public string? Kind { get; private set; }
    /// <summary>Dono: só os executores dele pegam os itens que precisam de git local.</summary>
    public Guid OwnerUserId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Quando o último item terminou (null enquanto houver item ativo).</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyList<PromotionItem> Items => _items;

    protected PromotionBatch() { }

    public PromotionBatch(string version, string? kind, Guid ownerUserId, string ownerName, DateTimeOffset now)
    {
        var v = PromotionTargetResolver.NormalizeVersion(version)
                ?? throw new DomainException("Informe a versão do lote (ex.: 3.274.10).");
        if (ownerUserId == Guid.Empty)
            throw new DomainException("Seu usuário não tem identificação no PRMake — gere a api-key de novo.");

        Id = Guid.NewGuid();
        Version = v;
        var k = kind?.Trim().ToLowerInvariant();
        Kind = k is "hotfix" or "release" ? k : null;
        OwnerUserId = ownerUserId;
        var owner = ownerName?.Trim() ?? string.Empty;
        OwnerName = owner.Length == 0 ? "Usuário" : owner[..Math.Min(MaxOwnerNameLength, owner.Length)];
        CreatedAt = now;
    }

    public PromotionItem AddItem(PromotionItem item)
    {
        if (_items.Count >= MaxItems)
            throw new DomainException($"O lote aceita no máximo {MaxItems} PRs.");
        item.Attach(this, _items.Count + 1);
        _items.Add(item);
        return item;
    }

    public bool IsActive => _items.Any(i => PromotionItemStatus.IsActive(i.Status));

    /// <summary>Recalcula o fim do lote depois que um item mudou.</summary>
    public void Refresh(DateTimeOffset now)
    {
        FinishedAt = IsActive ? null : (_items.Count == 0 ? now : _items.Max(i => i.FinishedAt ?? now));
    }
}

/// <summary>Um PR a abrir: card × repositório × destino.</summary>
public class PromotionItem
{
    public const int MaxCardLength = 100;
    public const int MaxTitleLength = 500;
    public const int MaxRepositoryLength = 200;
    public const int MaxBranchLength = 200;
    public const int MaxMessageLength = 2_000;
    public const int MaxFilesLength = 4_000;
    public const int MaxUrlLength = 500;
    public const int MaxAttempts = 2;
    /// <summary>Sem sinal do executor por esse tempo, o item volta para a fila.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public PromotionBatch? Batch { get; private set; }
    public int Position { get; private set; }

    public string CardNumber { get; private set; } = string.Empty;
    public string CardTitle { get; private set; } = string.Empty;
    public string RepositoryId { get; private set; } = string.Empty;
    /// <summary>Branch principal do card (hotfix/&lt;card&gt;).</summary>
    public string SourceBranch { get; private set; } = string.Empty;
    /// <summary>Branch de onde a principal foi cortada (master ou uma release) — o ponto de corte do cherry-pick.</summary>
    public string? SourceBase { get; private set; }
    /// <summary>Modelo do destino (release-{version}).</summary>
    public string TargetTemplate { get; private set; } = string.Empty;
    public string TargetBranch { get; private set; } = string.Empty;
    public bool Draft { get; private set; }
    public string Mode { get; private set; } = PromotionMode.CherryPick;

    public string Status { get; private set; } = PromotionItemStatus.Queued;
    public string? Message { get; private set; }
    /// <summary>Branch criada pelo cherry-pick (de onde sai o PR).</summary>
    public string? NewBranch { get; private set; }
    /// <summary>Arquivos em conflito, um por linha.</summary>
    public string? ConflictFiles { get; private set; }
    public int CommitCount { get; private set; }

    public int? PullRequestNumber { get; private set; }
    public string? PullRequestUrl { get; private set; }
    /// <summary>Id do registro em PullRequestsGithub.</summary>
    public int? PullRequestGithubId { get; private set; }

    public int Attempts { get; private set; }
    public Guid? WorkerId { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>xmin: dois executores pegando o mesmo item não se atropelam.</summary>
    public uint RowVersion { get; private set; }

    protected PromotionItem() { }

    public PromotionItem(string cardNumber, string? cardTitle, string repositoryId, string sourceBranch, string? sourceBase,
        string targetTemplate, string targetBranch, bool draft, string? mode, DateTimeOffset now)
    {
        CardNumber = Required(cardNumber, "card", MaxCardLength);
        var title = (cardTitle ?? string.Empty).Trim();
        CardTitle = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
        RepositoryId = Required(repositoryId, "repositório", MaxRepositoryLength);
        SourceBranch = Required(sourceBranch, "branch principal", MaxBranchLength);
        SourceBase = string.IsNullOrWhiteSpace(sourceBase) ? null : Required(sourceBase, "base", MaxBranchLength);
        TargetTemplate = Required(string.IsNullOrWhiteSpace(targetTemplate) ? targetBranch : targetTemplate, "destino", MaxBranchLength);
        TargetBranch = Required(targetBranch, "destino", MaxBranchLength);
        Draft = draft;
        Mode = PromotionMode.Normalize(mode);
        Id = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    internal void Attach(PromotionBatch batch, int position)
    {
        Batch = batch;
        BatchId = batch.Id;
        Position = position;
    }

    public bool IsActive => PromotionItemStatus.IsActive(Status);
    public bool IsRetryable => PromotionItemStatus.Retryable.Contains(Status);

    /// <summary>O executor pegou o item: roda com trava (<see cref="LeaseUntil"/>) renovada por heartbeat.</summary>
    public void Claim(Guid workerId, DateTimeOffset now)
    {
        if (Status != PromotionItemStatus.Queued)
            throw new DomainException("Este item não está na fila.");
        Status = PromotionItemStatus.Running;
        WorkerId = workerId;
        Attempts++;
        StartedAt = now;
        LeaseUntil = now + LeaseDuration;
        Message = null;
        UpdatedAt = now;
    }

    public void Heartbeat(Guid workerId, DateTimeOffset now)
    {
        if (Status != PromotionItemStatus.Running || WorkerId != workerId)
            throw new DomainException("Este item não está rodando neste executor.");
        LeaseUntil = now + LeaseDuration;
        UpdatedAt = now;
    }

    /// <summary>A trava venceu: volta para a fila (ou falha, passado o máximo de tentativas). Devolve true se mudou.</summary>
    public bool ExpireLease(DateTimeOffset now)
    {
        if (Status != PromotionItemStatus.Running || LeaseUntil is null || LeaseUntil > now)
            return false;
        WorkerId = null;
        LeaseUntil = null;
        UpdatedAt = now;
        if (Attempts >= MaxAttempts)
        {
            Finish(PromotionItemStatus.Error, "O executor parou de responder durante a promoção.", now);
            return true;
        }
        Status = PromotionItemStatus.Queued;
        return true;
    }

    public void RecordCommits(int count, string? newBranch)
    {
        CommitCount = Math.Max(0, count);
        NewBranch = string.IsNullOrWhiteSpace(newBranch) ? null : Required(newBranch, "branch criada", MaxBranchLength);
    }

    public void MarkOpened(int number, string url, int? githubId, bool alreadyExisted, DateTimeOffset now)
    {
        PullRequestNumber = number;
        PullRequestUrl = Truncate(url, MaxUrlLength);
        PullRequestGithubId = githubId;
        Finish(alreadyExisted ? PromotionItemStatus.AlreadyOpen : PromotionItemStatus.Opened, null, now);
    }

    public void MarkConflict(IEnumerable<string>? files, string? message, DateTimeOffset now)
    {
        ConflictFiles = Truncate(string.Join('\n', (files ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())), MaxFilesLength);
        Finish(PromotionItemStatus.Conflict, message ?? "Conflito no cherry-pick.", now);
    }

    public void Finish(string status, string? message, DateTimeOffset now)
    {
        if (PromotionItemStatus.IsActive(status))
            throw new DomainException("Situação final inválida.");
        Status = status;
        Message = Truncate(message, MaxMessageLength);
        WorkerId = null;
        LeaseUntil = null;
        FinishedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != PromotionItemStatus.Queued)
            throw new DomainException("Só dá para cancelar o que ainda está na fila.");
        Finish(PromotionItemStatus.Cancelled, "Cancelado pelo usuário.", now);
    }

    /// <summary>Reenvio: volta para a fila limpando o resultado anterior.</summary>
    public void Retry(DateTimeOffset now)
    {
        if (!IsRetryable)
            throw new DomainException("Só dá para reenviar o que falhou, ficou em conflito ou foi cancelado.");
        Status = PromotionItemStatus.Queued;
        Message = null;
        ConflictFiles = null;
        NewBranch = null;
        CommitCount = 0;
        Attempts = 0;
        WorkerId = null;
        LeaseUntil = null;
        StartedAt = null;
        FinishedAt = null;
        UpdatedAt = now;
    }

    private static string Required(string? value, string name, int max)
    {
        var v = value?.Trim() ?? string.Empty;
        if (v.Length == 0)
            throw new DomainException($"Informe {name}.");
        if (v.Length > max)
            throw new DomainException($"O campo {name} passa de {max} caracteres.");
        return v;
    }

    private static string? Truncate(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
            return null;
        return v.Length <= max ? v : v[..max];
    }
}
