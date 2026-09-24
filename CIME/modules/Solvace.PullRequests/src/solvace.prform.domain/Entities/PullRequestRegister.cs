using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.domain.Entities;

/// <summary>
/// Registro do tratamento de um card: um por card, com Description e Root Cause únicos
/// para todos os repositórios envolvidos. Os PRs abertos no GitHub ficam em <see cref="PullRequestGithub"/>.
/// </summary>
public class PullRequestRegister : IEntity<int>, IDescribable, IAuditableEntity
{
    public const int MaxCardNumberLength = 50;
    private const int MinDescriptionLength = 3;
    
    public int Id { get; set; }
    
    public string CardNumber { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;

    // Legado: branch/repositório passaram a ser do PR do GitHub (PullRequestGithub).
    // Mantidos apenas por compatibilidade até a remoção das colunas.
    public string? BranchPrefix { get; set; }
    public string? BranchName { get; set; }
    public string? RepositoryId { get; private set; }
    
    private string _description = string.Empty;
    public string Description 
    {
        get => _description;
        set => SetDescription(value);
    }
    
    public Guid UserId { get; private set; }

    public Form? Form { get; private set; }
    public int FormId { get; private set; }

    private readonly List<PullRequestGithub> _githubPullRequests = new();
    public IReadOnlyCollection<PullRequestGithub> GithubPullRequests => _githubPullRequests;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; } 
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected PullRequestRegister() { }

    public PullRequestRegister(PullRequestRegisterRequest request)
    {
        SetCardNumber(request.CardNumber);
        SetDescription(request.Description);
        SetRootCause(request.RootCause);
        SetUser(request.UserId);
        SetForm(request.FormId);
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Aplica Description/RootCause do request: null mantém o valor atual, vazio limpa.</summary>
    public void UpdateContent(string? description, string? rootCause)
    {
        if (description is not null)
            SetDescription(description);
        if (rootCause is not null)
            SetRootCause(rootCause);
    }

    private void SetCardNumber(string cardNumber)
    {
        var trimmed = cardNumber?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new DomainException("CardNumber cannot be empty");
        if (trimmed.Length > MaxCardNumberLength)
            throw new DomainException($"CardNumber cannot exceed {MaxCardNumberLength} characters");

        CardNumber = trimmed;
    }

    /// <summary>Opcional: o card pode ser salvo sem descrição; se informada, precisa ter o tamanho mínimo.</summary>
    public void SetDescription(string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) && description.Length < MinDescriptionLength)
            throw new DomainException($"Description must be at least {MinDescriptionLength} characters long");
        
        _description = description ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>Opcional: o card pode ser salvo sem root cause; se informado, precisa ter o tamanho mínimo.</summary>
    public void SetRootCause(string? rootCause)
    {
        if (!string.IsNullOrWhiteSpace(rootCause) && rootCause.Length < MinDescriptionLength)
            throw new DomainException($"rootCause must be at least {MinDescriptionLength} characters long");
        
        RootCause = rootCause ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetUser(Guid userId)
    {
        UserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetForm(int formId)
    {
        Form = null;
        FormId = formId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateAuditFields(string updatedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
    
    public PullRequestRegisterResponse ToResponse()
    {
        // Branch/repositório vêm do PR do GitHub mais recente (se carregado); o legado da
        // própria linha fica como fallback para clientes que ainda leem esses campos.
        var githubPrs = _githubPullRequests
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.ToResponse())
            .ToList();
        var latest = githubPrs.FirstOrDefault();

        return new()
        {
            Id = Id,
            CardNumber = CardNumber,
            RootCause = RootCause,
            Description = Description,
            BranchName = latest?.BranchName ?? (string.IsNullOrEmpty(BranchName) ? CardNumber : BranchName),
            BranchPrefix = latest?.BranchPrefix ?? (string.IsNullOrEmpty(BranchPrefix) ? "hotfix/" : BranchPrefix),
            RepositoryId = latest?.RepositoryId ?? RepositoryId,
            UserId = UserId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatedBy = CreatedBy,
            GithubPullRequests = githubPrs
        };
    }
}