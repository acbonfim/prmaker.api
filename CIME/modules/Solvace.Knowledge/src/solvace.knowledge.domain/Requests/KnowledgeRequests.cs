namespace solvace.knowledge.domain.Requests;

/// <summary>
/// Lote da sincronização do Knowledge Center (0033), enviado pela skill (quem tem a credencial do banco do KC). O
/// backend reaplica o piso de filtro: o que não passa é descartado (e removido da cópia, se estava lá).
/// </summary>
public class KnowledgeSyncRequest
{
    /// <summary>dev | prod — o ambiente de onde os artigos vieram.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Carga completa: o que não veio no lote sai da cópia deste ambiente.</summary>
    public bool Full { get; set; }

    public List<KnowledgeSyncArticle> Articles { get; set; } = [];
}

public class KnowledgeSyncArticle
{
    public Guid SourceId { get; set; }
    public int ArticleNumber { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public bool CategoryActive { get; set; } = true;
    public bool SubcategoryActive { get; set; } = true;
    public int StatusId { get; set; }
    public bool IsDeleted { get; set; }
    public List<string>? Tags { get; set; }
    /// <summary>Maior data de alteração do artigo no KC (criação, edição, remoção).</summary>
    public DateTimeOffset? SourceUpdatedAt { get; set; }
}

/// <summary>Cria/atualiza um projeto da engenharia reversa (chave na rota).</summary>
public class UpsertArchitectureProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public string? Repository { get; set; }
    /// <summary>Resumo curto que vai inteiro para o índice (responsabilidade, pastas-chave, tabelas, integrações).</summary>
    public string? Summary { get; set; }
    public List<string>? Keywords { get; set; }
    public string? SourceCommit { get; set; }
    public string? SourceBranch { get; set; }
    public int? Order { get; set; }
}

/// <summary>Grava uma seção (chaves do projeto e da seção na rota). Conteúdo igual ao atual não cria versão.</summary>
public class WriteArchitectureSectionRequest
{
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? Order { get; set; }
    /// <summary>skill | admin | ai.</summary>
    public string? Source { get; set; }
    /// <summary>O que mudou (aparece no histórico).</summary>
    public string? Note { get; set; }
}
