namespace solvace.github.domain.Responses;

/// <summary>
/// 0106: o review de um PR — revisões (aprovou / pediu mudanças / comentou), comentários nas linhas (com a indicação se a
/// linha é código NOVO do PR) e comentários da conversa. Base do fluxo do pedido de mudanças.
/// </summary>
public class PullRequestReviewsResponse
{
    public string Repository { get; set; } = string.Empty;
    public int Number { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Head { get; set; }
    public string? Base { get; set; }
    /// <summary>open | closed | merged.</summary>
    public string? State { get; set; }
    public List<PullRequestReviewItem> Reviews { get; set; } = [];
    public List<PullRequestLineComment> Comments { get; set; } = [];
    public List<PullRequestConversationComment> Conversation { get; set; } = [];
    /// <summary>Quem está com "changes requested" valendo (última revisão de cada pessoa).</summary>
    public List<string> ChangesRequestedBy { get; set; } = [];
    public string? Error { get; set; }
}

public class PullRequestReviewItem
{
    public long Id { get; set; }
    public string User { get; set; } = string.Empty;
    /// <summary>approved | changes_requested | commented | dismissed | pending.</summary>
    public string State { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? Url { get; set; }
}

public class PullRequestLineComment
{
    public long Id { get; set; }
    public long? ReviewId { get; set; }
    public long? InReplyTo { get; set; }
    public string User { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? Line { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>A linha comentada foi adicionada/alterada pelo PR (última linha do trecho começa com '+').</summary>
    public bool OnNewCode { get; set; }
    /// <summary>O código mudou depois do comentário (a linha não está mais no diff atual).</summary>
    public bool Outdated { get; set; }
    /// <summary>Trecho do diff que o comentário cita (últimas linhas).</summary>
    public string? Snippet { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Url { get; set; }
}

public class PullRequestConversationComment
{
    public long Id { get; set; }
    public string User { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? Url { get; set; }
}
