namespace solvace.prform.Teams;

/// <summary>
/// Payload de ingestão de uma única mensagem do Teams, enviado pelo frontend após o usuário
/// autenticar no Teams (MSAL) e escolher o grupo. Dedup garantido pelo <see cref="MessageId"/>.
/// </summary>
public class IngestTeamsMessageRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Id único da mensagem no Teams (usado para não duplicar).</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Texto da mensagem (de preferência já convertido de HTML para texto).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Nome de quem enviou a mensagem.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Data/hora da mensagem. Se ausente, usa o momento da ingestão.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}
