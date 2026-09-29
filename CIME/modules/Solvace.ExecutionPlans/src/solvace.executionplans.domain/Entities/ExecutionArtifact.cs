using System.Security.Cryptography;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Arquivo produzido pela skill (script, análise, evidência, imagem...). Único por (plano, tipo,
/// nome): reenviar o mesmo arquivo substitui o conteúdo. O conteúdo fica em
/// <see cref="ExecutionArtifactContent"/> para listar sem carregar bytes.
/// </summary>
public class ExecutionArtifact
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1_000;

    /// <summary>Tamanho máximo de um arquivo.</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    /// <summary>Soma máxima dos arquivos de um plano (o .zip é montado em memória).</summary>
    public const long MaxPlanBytes = 50L * 1024 * 1024;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string? StepKey { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Kind { get; private set; } = ExecutionArtifactKind.Attachment;
    public string ContentType { get; private set; } = "application/octet-stream";
    public long Size { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Número por card (<c>anexo #n</c>, 0031): o mesmo nas abas Análise e Correção, para ser citado.</summary>
    public int Number { get; private set; }

    /// <summary>Comentário do plano a que o arquivo foi anexado (0031); null = arquivo da skill.</summary>
    public Guid? NoteId { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public ExecutionArtifactContent? Content { get; private set; }

    protected ExecutionArtifact() { }

    public ExecutionArtifact(Guid planId, string name, string? kind, string createdBy, DateTimeOffset now, int number = 0, Guid? noteId = null)
    {
        Id = Guid.NewGuid();
        PlanId = planId;
        Name = NormalizeName(name);
        Kind = NormalizeKind(kind, Name);
        CreatedBy = createdBy;
        CreatedAt = now;
        Number = number;
        NoteId = noteId;
    }

    /// <summary>Nome livre no plano: <c>foto.png</c> já usado vira <c>foto (2).png</c> (anexo de comentário nunca substitui outro).</summary>
    public static string UniqueName(string name, Func<string, bool> taken)
    {
        if (!taken(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (candidate.Length > MaxNameLength)
                candidate = $"{stem[..Math.Max(1, stem.Length - (candidate.Length - MaxNameLength))]} ({i}){ext}";
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>Só o nome do arquivo (sem pastas), sem caracteres inválidos.</summary>
    public static string NormalizeName(string? name)
    {
        var fileName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/').Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        fileName = fileName.Trim();
        if (fileName.Length == 0 || fileName is "." or "..")
            throw new DomainException("O nome do arquivo é obrigatório.");
        if (fileName.Length > MaxNameLength)
            throw new DomainException($"O nome do arquivo pode ter no máximo {MaxNameLength} caracteres.");
        return fileName;
    }

    public static string NormalizeKind(string? kind, string fileName)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return ExecutionArtifactKind.Infer(fileName);
        var normalized = kind.Trim().ToLowerInvariant();
        if (!ExecutionArtifactKind.All.Contains(normalized))
            throw new DomainException($"Tipo de arquivo inválido: '{kind}'.");
        return normalized;
    }

    public void SetContent(byte[] data, string contentType, string? stepKey, string? description, DateTimeOffset now, bool isNew)
    {
        if (data.LongLength > MaxFileBytes)
            throw new DomainException($"O arquivo passa do limite de {MaxFileBytes / (1024 * 1024)} MB.");

        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        Size = data.LongLength;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.IsNullOrWhiteSpace(stepKey))
            StepKey = ExecutionStep.NormalizeKey(stepKey);
        if (description is not null)
        {
            var trimmed = description.Trim();
            Description = trimmed.Length == 0 ? null : trimmed.Length <= MaxDescriptionLength ? trimmed : trimmed[..MaxDescriptionLength];
        }

        if (Content is null)
            Content = new ExecutionArtifactContent(Id, data);
        else
            Content.Replace(data);

        if (!isNew)
            UpdatedAt = now;
    }
}

public class ExecutionArtifactContent
{
    public Guid ArtifactId { get; private set; }
    public byte[] Data { get; private set; } = [];

    protected ExecutionArtifactContent() { }

    public ExecutionArtifactContent(Guid artifactId, byte[] data)
    {
        ArtifactId = artifactId;
        Data = data;
    }

    public void Replace(byte[] data) => Data = data;
}
