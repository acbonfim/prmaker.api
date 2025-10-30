using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.domain.Entities;

public class PullRequestRegister : IEntity<int>, IDescribable, IAuditableEntity
{
    private const int MinDescriptionLength = 3;
    
    public int Id { get; set; }
    
    public string CardNumber { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    
    private string _description = string.Empty;
    public string Description 
    {
        get => _description;
        set => SetDescription(value);
    }
    
    public Guid UserId { get; private set; }

    public Form? Form { get; private set; }
    public int FormId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; } 
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected PullRequestRegister() { }

    public PullRequestRegister(PullRequestRegisterRequest request)
    {
        SetDescription(request.Description);
        SetUser(request.UserId);
        SetForm(request.FormId);
        CardNumber = request.CardNumber;
        RootCause = request.RootCause;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description cannot be empty");
            
        if (description.Length < MinDescriptionLength)
            throw new DomainException($"Description must be at least {MinDescriptionLength} characters long");
        
        _description = description;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void SetRootCause(string rootCause)
    {
        if (string.IsNullOrWhiteSpace(rootCause))
            throw new DomainException("rootCause cannot be empty");
            
        if (rootCause.Length < MinDescriptionLength)
            throw new DomainException($"rootCause must be at least {MinDescriptionLength} characters long");
        
        RootCause = rootCause;
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
    
    public PullRequestRegisterResponse ToResponse() =>
        new()
        {
            Id = Id
        };
}