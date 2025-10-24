using solvace.prform.Data.Responses;

namespace solvace.prform.Data.Entities;

public class User : ISoftDeletable, IAuditableEntity, IEntity<int>
{
    private const int MinNameLength = 2;
    private const int MaxNameLength = 100;
    
    public int Id { get; set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Protected constructor for EF Core
    protected User() { }

    public User(string name)
    {
        SetName(name);
        CreatedAt = DateTime.UtcNow;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name cannot be empty");
            
        if (name.Length < MinNameLength)
            throw new DomainException($"Name must be at least {MinNameLength} characters long");
            
        if (name.Length > MaxNameLength)
            throw new DomainException($"Name cannot exceed {MaxNameLength} characters");

        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete(string deletedBy)
    {
        if (IsDeleted)
            throw new DomainException("User is already deleted");

        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
    }

    public void Restore()
    {
        if (!IsDeleted)
            throw new DomainException("User is not deleted");

        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }

    public void UpdateAuditFields(string updatedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public UserResponse ToResponse() =>
        new()
        {
            Id = Id,
            Name = Name,
        };
}

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}