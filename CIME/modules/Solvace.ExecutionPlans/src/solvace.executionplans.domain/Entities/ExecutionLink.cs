using System.Text.Json;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Link anexado a uma etapa (0024): chamado (status marcado à mão pelo usuário — o PRMake não lê o
/// Freshservice), PR (status espelhado do GitHub) ou documento. Nada é apagado ao mudar de status: uma
/// etapa acumula o histórico (ex.: chamado fechado sem resolução → novo chamado na mesma etapa).
/// </summary>
public class ExecutionLink
{
    public const int MaxUrlLength = 1000;
    public const int MaxTitleLength = 300;
    public const int MaxChecksFailedLength = 4000;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string StepKey { get; private set; } = string.Empty;
    public string Kind { get; private set; } = ExecutionLinkKind.Other;
    public string Url { get; private set; } = string.Empty;
    public string? Title { get; private set; }

    /// <summary>Chamado: open | resolved | closed. PR: open | merged | closed. Outros: null.</summary>
    public string? Status { get; private set; }

    /// <summary>A etapa fica "aguardando" enquanto este chamado estiver aberto.</summary>
    public bool BlocksStep { get; private set; }

    /// <summary>PR: número no GitHub, repositório e branch de destino.</summary>
    public int? PullRequestNumber { get; private set; }
    public string? Repository { get; private set; }
    public string? TargetBranch { get; private set; }

    /// <summary>0069: CI do PR aberto — pending | success | failure (null = sem CI ou ainda não lido).</summary>
    public string? ChecksStatus { get; private set; }
    /// <summary>0069: checks que falharam (JSON: [{"name","url","preexisting"}]) — também com CI verde/pendente, se só a base falha.</summary>
    public string? ChecksFailed { get; private set; }
    /// <summary>0069: commit de cabeça do PR que o CI avaliou (push novo = nova rodada).</summary>
    public string? ChecksHeadSha { get; private set; }
    public DateTimeOffset? ChecksChangedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? StatusChangedBy { get; private set; }
    public DateTimeOffset? StatusChangedAt { get; private set; }

    protected ExecutionLink() { }

    public ExecutionLink(Guid planId, string stepKey, string url, string? title, string? kind, bool blocksStep,
        int? pullRequestNumber, string? repository, string? targetBranch, string createdBy, DateTimeOffset now)
    {
        var trimmedUrl = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new DomainException("Informe um link http(s) válido.");
        if (trimmedUrl.Length > MaxUrlLength)
            throw new DomainException($"O link pode ter no máximo {MaxUrlLength} caracteres.");

        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? ExecutionLinkKind.Infer(trimmedUrl) : kind.Trim().ToLowerInvariant();
        if (!ExecutionLinkKind.All.Contains(normalizedKind))
            throw new DomainException($"Tipo de link inválido: '{kind}'.");

        Id = Guid.NewGuid();
        PlanId = planId;
        StepKey = ExecutionStep.NormalizeKey(stepKey);
        Url = trimmedUrl;
        Kind = normalizedKind;
        SetTitle(title);
        // Só chamado bloqueia a etapa; PR conclui a etapa pelo merge (regra da etapa de PR).
        BlocksStep = blocksStep && normalizedKind == ExecutionLinkKind.Ticket;
        Status = normalizedKind is ExecutionLinkKind.Ticket or ExecutionLinkKind.PullRequest ? ExecutionLinkStatus.Open : null;
        PullRequestNumber = pullRequestNumber;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();
        TargetBranch = string.IsNullOrWhiteSpace(targetBranch) ? null : targetBranch.Trim();
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    /// <summary>Nome curto para mensagens ("#123", título ou a URL).</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Title) ? Title!
        : PullRequestNumber is { } n ? $"#{n}"
        : Url;

    public void SetTitle(string? title)
    {
        var t = title?.Trim();
        Title = string.IsNullOrEmpty(t) ? null : t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
    }

    /// <summary>
    /// 0069: CI do PR (pela sincronização do GitHub). <paramref name="state"/> null = o commit não tem CI. True quando
    /// mudou o estado, o commit avaliado ou a lista do que falhou.
    /// </summary>
    public bool ChangeChecks(string? state, string? headSha, IReadOnlyList<ExecutionCheckItem> failed, DateTimeOffset now)
    {
        if (Kind != ExecutionLinkKind.PullRequest)
            throw new DomainException("Só PR tem CI.");
        var normalized = string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToLowerInvariant();
        if (normalized is not null && !ExecutionLinkChecks.All.Contains(normalized))
            throw new DomainException($"Estado de CI inválido: '{state}'.");
        string? json = null;
        if (failed.Count > 0)
        {
            var items = failed.Take(20).ToList();
            json = JsonSerializer.Serialize(items, JsonOptions);
            while (json.Length > MaxChecksFailedLength && items.Count > 1)
                json = JsonSerializer.Serialize(items = items.Take(items.Count - 1).ToList(), JsonOptions);
            if (json.Length > MaxChecksFailedLength)
                json = null;
        }
        var sha = string.IsNullOrWhiteSpace(headSha) ? null : headSha.Trim();
        if (ChecksStatus == normalized && ChecksFailed == json && ChecksHeadSha == sha)
            return false;
        ChecksStatus = normalized;
        ChecksFailed = json;
        ChecksHeadSha = sha;
        ChecksChangedAt = now;
        return true;
    }

    /// <summary>0069: checks que falharam, lidos do JSON gravado.</summary>
    public IReadOnlyList<ExecutionCheckItem> FailedChecks()
    {
        if (string.IsNullOrEmpty(ChecksFailed))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<ExecutionCheckItem>>(ChecksFailed, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Muda o status. Chamado: pelo usuário (open/resolved/closed). PR: pela sincronização do GitHub.</summary>
    public bool ChangeStatus(string status, string actor, DateTimeOffset now)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        var allowed = Kind switch
        {
            ExecutionLinkKind.Ticket => ExecutionLinkStatus.TicketStatuses,
            ExecutionLinkKind.PullRequest => ExecutionLinkStatus.PullRequestStatuses,
            _ => throw new DomainException("Este link não tem status.")
        };
        if (!allowed.Contains(normalized))
            throw new DomainException($"Status inválido para {Kind}: '{status}'.");
        if (Status == normalized)
            return false;

        Status = normalized;
        StatusChangedBy = actor;
        StatusChangedAt = now;
        return true;
    }
}

/// <summary>0069: um check do CI que falhou (nome, link do log e se a base já falha nele).</summary>
public record ExecutionCheckItem(string Name, string? Url, bool Preexisting = false);
