using solvace.timeline.domain.Entities.Base;
using solvace.timeline.domain.Responses;

namespace solvace.timeline.domain.Entities;

/// <summary>
/// Representa um registro na linha do tempo de um card. Cada entrada guarda o que foi
/// feito (descrição), quem fez (usuário logado ou nome informado externamente) e a data/hora
/// da ocorrência.
/// </summary>
public class TimelineEntry : IEntity<int>, IDescribable, IAuditableEntity
{
    private const int MinDescriptionLength = 3;

    public int Id { get; set; }

    /// <summary>Número do card ao qual o registro está vinculado.</summary>
    public string CardNumber { get; private set; } = string.Empty;

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => SetDescription(value);
    }

    /// <summary>Id do usuário logado que criou o registro. Nulo quando criado por um chamador externo.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Nome de quem realizou o registro. Sempre preenchido.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>
    /// Id da mensagem de origem quando o registro foi importado de uma fonte externa
    /// (ex.: mensagem de um grupo do Teams). Nulo para registros criados diretamente.
    /// Usado para impedir importação duplicada.
    /// </summary>
    public string? SourceMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    protected TimelineEntry() { }

    public TimelineEntry(string cardNumber, string description, Guid? userId, string userName)
    {
        SetCardNumber(cardNumber);
        SetUser(userId, userName);
        SetDescription(description);
        CreatedAt = DateTimeOffset.UtcNow;
        CreatedBy = userId?.ToString() ?? userName;
    }

    /// <summary>
    /// Cria um registro importado de uma fonte externa (ex.: Teams), preservando o
    /// id da mensagem de origem (para dedup) e a data/hora original da ocorrência.
    /// </summary>
    public TimelineEntry(string cardNumber, string description, string userName, string sourceMessageId, DateTimeOffset occurredAt)
        : this(cardNumber, description, null, userName)
    {
        if (string.IsNullOrWhiteSpace(sourceMessageId))
            throw new DomainException("O id da mensagem de origem é obrigatório para registros importados.");

        SourceMessageId = sourceMessageId;
        CreatedAt = occurredAt;
    }

    private void SetCardNumber(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new DomainException("O número do card é obrigatório.");

        CardNumber = cardNumber.Trim();
    }

    private void SetUser(Guid? userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("O nome de quem realizou o registro é obrigatório.");

        UserId = userId;
        UserName = userName.Trim();
    }

    private void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A descrição é obrigatória.");

        var trimmed = description.Trim();

        if (trimmed.Length < MinDescriptionLength)
            throw new DomainException($"A descrição deve ter ao menos {MinDescriptionLength} caracteres.");

        _description = trimmed;
    }

    /// <summary>Atualiza a descrição do registro e marca a auditoria de alteração.</summary>
    public void UpdateDescription(string description, string? updatedBy)
    {
        SetDescription(description);
        UpdatedBy = updatedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Indica se o registro foi criado por um chamador externo (sem usuário logado).</summary>
    public bool IsExternal => !UserId.HasValue;

    public bool IsOwnedByUser(Guid userId) => UserId.HasValue && UserId.Value == userId;

    public TimelineEntryResponse ToResponse() => new()
    {
        Id = Id,
        CardNumber = CardNumber,
        Description = Description,
        UserId = UserId,
        UserName = UserName,
        SourceMessageId = SourceMessageId,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}
