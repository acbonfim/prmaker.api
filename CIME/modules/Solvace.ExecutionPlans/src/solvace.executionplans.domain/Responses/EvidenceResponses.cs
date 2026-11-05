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

/// <summary>0089: evidência do DevOps guardada nos arquivos do item de trabalho (card sem plano também).</summary>
public class WorkItemEvidenceItem
{
    /// <summary>Número do arquivo no item (<c>arquivo #n</c> do <c>prmake_work_item_files</c>); null = não importada.</summary>
    public int? File { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>anexo | campo | discussion.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>De onde veio, em texto (campo, comentário e data).</summary>
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? At { get; set; }
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    /// <summary>imported (agora) | stored (já estava no item) | too-large | item-limit | error.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    /// <summary>Dá para ler (imagem, PDF, texto, Office).</summary>
    public bool Readable { get; set; }
}

/// <summary>0089: evidências do card importadas para um item de trabalho.</summary>
public class WorkItemEvidenceResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public long Number { get; set; }
    /// <summary>O DevOps não respondeu (sem integração, sem acesso) — nada importado.</summary>
    public string? Error { get; set; }
    public List<WorkItemEvidenceItem> Items { get; set; } = [];
}
