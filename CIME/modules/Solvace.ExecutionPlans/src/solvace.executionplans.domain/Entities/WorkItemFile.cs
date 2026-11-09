using System.Security.Cryptography;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085 r5 (1.2): arquivo do item de trabalho (script, análise, chamado, dados, anexo, texto) — a seção "Arquivos" do item
/// mostra só os dele (mais os do plano ligado, que continuam em <see cref="ExecutionArtifact"/>). Número por item
/// (<c>arquivo #n</c>); mesmo tipo + nome substitui o conteúdo (como no plano). O conteúdo fica em
/// <see cref="WorkItemFileContent"/> para listar sem carregar bytes.
/// </summary>
public class WorkItemFile
{
    /// <summary>Mesmo limite de um arquivo do plano.</summary>
    public const long MaxFileBytes = ExecutionArtifact.MaxFileBytes;

    /// <summary>Soma máxima dos arquivos de um item (igual à do plano).</summary>
    public const long MaxItemBytes = ExecutionArtifact.MaxPlanBytes;

    public Guid Id { get; private set; }
    public Guid WorkItemId { get; private set; }
    /// <summary>Sub-tarefa a que o arquivo pertence (número global da sub-tarefa); null = do item.</summary>
    public long? TaskNumber { get; private set; }
    /// <summary>Número por item (<c>arquivo #n</c>).</summary>
    public int Number { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Kind { get; private set; } = WorkItemFileKind.Attachment;
    public string ContentType { get; private set; } = "application/octet-stream";
    public long Size { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public WorkItemFileContent? Content { get; private set; }

    protected WorkItemFile() { }

    public WorkItemFile(Guid workItemId, int number, string name, string? kind, string createdBy, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        WorkItemId = workItemId;
        Number = number;
        Name = ExecutionArtifact.NormalizeName(name);
        Kind = WorkItemFileKind.Normalize(kind, Name);
        var by = string.IsNullOrWhiteSpace(createdBy) ? "Usuário" : createdBy.Trim();
        CreatedBy = by.Length <= 200 ? by : by[..200];
        CreatedAt = now;
    }

    /// <summary>Grava (ou troca) o conteúdo. <paramref name="taskNumber"/> null mantém a sub-tarefa atual.</summary>
    public void SetContent(byte[] data, string? contentType, long? taskNumber, DateTimeOffset now, bool isNew)
    {
        if (data.LongLength == 0)
            throw new DomainException($"O arquivo '{Name}' está vazio.");
        if (data.LongLength > MaxFileBytes)
            throw new DomainException($"O arquivo passa do limite de {MaxFileBytes / (1024 * 1024)} MB.");

        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        ContentType = type.Length <= 150 ? type : type[..150];
        Size = data.LongLength;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        if (taskNumber is > 0)
            TaskNumber = taskNumber;

        if (Content is null)
            Content = new WorkItemFileContent(Id, data);
        else
            Content.Replace(data);

        if (!isNew)
            UpdatedAt = now;
    }
}

public class WorkItemFileContent
{
    public Guid FileId { get; private set; }
    public byte[] Data { get; private set; } = [];

    protected WorkItemFileContent() { }

    public WorkItemFileContent(Guid fileId, byte[] data)
    {
        FileId = fileId;
        Data = data;
    }

    public void Replace(byte[] data) => Data = data;
}

/// <summary>0085 r5: tipos de arquivo do item (os do plano, sem imagem — imagem entra como anexo — e com texto livre).</summary>
public static class WorkItemFileKind
{
    public const string Script = ExecutionArtifactKind.Script;
    public const string Analysis = ExecutionArtifactKind.Analysis;
    public const string Ticket = ExecutionArtifactKind.Ticket;
    public const string Data = ExecutionArtifactKind.Data;
    public const string Attachment = ExecutionArtifactKind.Attachment;
    /// <summary>Texto livre (texto de PR, comentário, mensagem...).</summary>
    public const string Text = "text";
    /// <summary>0092: prints da validação em ambiente (galeria na sub-tarefa).</summary>
    public const string Evidence = "evidence";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Script, Analysis, Ticket, Data, Attachment, Text, Evidence };

    /// <summary>Tipo informado (validado) ou inferido pelo nome; imagem vira anexo.</summary>
    public static string Normalize(string? kind, string fileName)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            var inferred = ExecutionArtifactKind.Infer(fileName);
            return All.Contains(inferred) ? inferred : Attachment;
        }
        var k = kind.Trim().ToLowerInvariant();
        if (k == ExecutionArtifactKind.Image) return Attachment;
        if (!All.Contains(k))
            throw new DomainException($"Tipo de arquivo inválido: '{kind}' (use script, analysis, ticket, data, attachment, text ou evidence).");
        return k;
    }
}
