using solvace.prform.domain.Responses;

namespace solvace.prform.domain.Entities;

public class Form : IEntity<int>, IDescribable
{
    private const int MinDescriptionLength = 3;
    private const int MinEnvironmentNameLength = 2;
    private const int MaxEnvironmentNameLength = 100;

    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string EnvironmentName { get; private set; } = string.Empty;

    protected Form() { }

    public Form(string description, string environmentName)
    {
        SetDescription(description);
        SetEnvironmentName(environmentName);
    }

    public void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description cannot be empty");

        if (description.Length < MinDescriptionLength)
            throw new DomainException($"Description must be at least {MinDescriptionLength} characters long");
        

        Description = description;
    }

    public void SetEnvironmentName(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
            throw new DomainException("Environment name cannot be empty");

        if (environmentName.Length < MinEnvironmentNameLength)
            throw new DomainException($"Environment name must be at least {MinEnvironmentNameLength} characters long");

        if (environmentName.Length > MaxEnvironmentNameLength) 
            throw new DomainException($"Environment name cannot exceed {MaxEnvironmentNameLength} characters");

        EnvironmentName = environmentName;
    }
    
    public FormResponse ToResponse() =>
        new()
        {
            Id = Id,
            Description = Description,
            EnvironmentName = EnvironmentName,       
        };
}

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}