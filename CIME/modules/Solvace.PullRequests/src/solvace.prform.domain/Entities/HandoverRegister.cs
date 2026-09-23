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

    /// <summary>
    /// Quando true, a passagem de conhecimento pode ser acessada pelo link público
    /// (sem autenticação). Quando false, apenas usuários autenticados têm acesso.
    /// Novos handovers nascem públicos.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected HandoverRegister() { }

    public HandoverRegister(string cardNumber, string content, string? repositoryId, bool isPublic = true)
    {
        CardNumber = cardNumber;
        Content = content;
        RepositoryId = repositoryId;
        IsPublic = isPublic;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateContent(string content, string? repositoryId)
    {
        Content = content;
        if (!string.IsNullOrWhiteSpace(repositoryId))
            RepositoryId = repositoryId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Define se o handover pode ser acessado pelo link público.</summary>
    public void SetVisibility(bool isPublic)
    {
        IsPublic = isPublic;
        UpdatedAt = DateTime.UtcNow;
    }

    public HandoverResponse ToResponse() =>
        new()
        {
            Id = Id,
            CardNumber = CardNumber,
            RepositoryId = RepositoryId,
            Content = Content,
            IsPublic = IsPublic,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
}
