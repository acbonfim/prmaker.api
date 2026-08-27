using solvace.prform.domain.Responses;

namespace solvace.prform.domain.Entities;

/// <summary>
/// Passagem de conhecimento (handover) de um card, gerada por IA e persistida por
/// número de card para que o turno seguinte dê continuidade ao tratamento.
/// </summary>
public class HandoverRegister : IEntity<int>, IAuditableEntity
{
    public int Id { get; set; }

    public string CardNumber { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }

    /// <summary>Conteúdo do formulário preenchido, em markdown.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected HandoverRegister() { }

    public HandoverRegister(string cardNumber, string content, string? repositoryId)
    {
        CardNumber = cardNumber;
        Content = content;
        RepositoryId = repositoryId;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateContent(string content, string? repositoryId)
    {
        Content = content;
        if (!string.IsNullOrWhiteSpace(repositoryId))
            RepositoryId = repositoryId;
        UpdatedAt = DateTime.UtcNow;
    }

    public HandoverResponse ToResponse() =>
        new()
        {
            Id = Id,
            CardNumber = CardNumber,
            RepositoryId = RepositoryId,
            Content = Content,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
}
