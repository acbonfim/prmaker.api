using solvace.knowledge.domain.Entities;

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
    /// <summary><c>article_unique_id</c> no KC, como texto.</summary>
    public string SourceId { get; set; } = string.Empty;
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
    /// <summary>Interdependências (0034); null = mantém as atuais.</summary>
    public List<ArchitectureRelation>? Relations { get; set; }
    /// <summary>Nome para pessoas (0038); null mantém, "" limpa. Não vai para o espelho das skills.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Uma frase em linguagem simples (0038); null mantém, "" limpa.</summary>
    public string? Tagline { get; set; }
    /// <summary>Área de negócio (0038); null mantém, "" limpa.</summary>
    public string? BusinessArea { get; set; }
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
    /// <summary>llm (técnica, vai para as skills) | human (Guia, só na tela) — 0038. Null mantém; seção nova sem
    /// público: human se a chave começa com "guia-", senão llm.</summary>
    public string? Audience { get; set; }
}

/// <summary>Sugestão para a engenharia reversa (análise ou pessoa) — vai para a fila do admin.</summary>
public class CreateArchitectureSuggestionRequest
{
    public string ProjectKey { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    /// <summary>learning | divergence | other.</summary>
    public string? Kind { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? CardNumber { get; set; }
}

/// <summary>Resolve uma pergunta da fila (0040): answered (com a seção que responde) | dismissed | open.</summary>
public class ResolveArchitectureQuestionRequest
{
    public string Status { get; set; } = string.Empty;
    public string? ProjectKey { get; set; }
    public string? SectionKey { get; set; }
    public string? Note { get; set; }
}

public class ResolveArchitectureSuggestionRequest
{
    /// <summary>applied | dismissed.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}
