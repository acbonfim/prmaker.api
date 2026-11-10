namespace solvace.azure.domain.Requests;

/// <summary>
/// Comentário novo na discussion do card, sem depender do registro do card no PRMake (sub-tarefa <c>comment</c> da
/// Bancada). Cada chamada cria um comentário; as imagens sobem como anexos do DevOps e entram no fim do texto.
/// </summary>
public class PostCommentRequest
{
    /// <summary>Texto em HTML (a discussion do DevOps renderiza HTML).</summary>
    public string Html { get; set; } = string.Empty;

    public List<CommentImage> Images { get; set; } = [];
}

public class CommentImage
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>Conteúdo do arquivo em base64.</summary>
    public string ContentBase64 { get; set; } = string.Empty;
}
