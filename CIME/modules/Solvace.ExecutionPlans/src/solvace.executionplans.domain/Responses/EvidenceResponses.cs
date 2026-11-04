namespace solvace.executionplans.domain.Responses;

/// <summary>0076: uma evidência do card (importada para os arquivos do plano ou não, com o motivo).</summary>
public class CardEvidenceItem
{
    /// <summary>Número do anexo no card (<c>#n</c> do <c>prmake_attachment</c>); null = não importada.</summary>
    public int? Number { get; set; }
    public Guid? ArtifactId { get; set; }
    public Guid? PlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>anexo | campo | discussion.</summary>
    public string Source { get; set; } = string.Empty;
    public string? SourceDetail { get; set; }
    public DateTimeOffset? At { get; set; }
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    /// <summary>imported (agora) | stored (já estava) | too-large | plan-limit | error.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    /// <summary>A análise consegue ler (imagem, PDF, texto, Office) — e por isso precisa abrir.</summary>
    public bool Readable { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>0076: evidências do card e o estado da importação no plano atual.</summary>
public class CardEvidenceResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public DateTimeOffset? CheckedAt { get; set; }
    public string? Error { get; set; }
    public string? SkipReason { get; set; }
    public List<CardEvidenceItem> Items { get; set; } = [];
    public int Unread => Items.Count(i => i.Readable && i.Number is not null && i.ReadAt is null);
}
