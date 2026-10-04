using System.Text.Json;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;

namespace solvace.knowledge.domain.Responses;

public class ReverseDocTypeResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool Required { get; set; }
    public List<string> Kinds { get; set; } = [];
    public List<string> Headings { get; set; } = [];
    public string Purpose { get; set; } = string.Empty;
    /// <summary>Modelo efetivo (o do código ou o da configuração) + as regras de como escrever um item.</summary>
    public string Template { get; set; } = string.Empty;
}

public class ReverseItemKindResponse
{
    public string Prefix { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Plural { get; set; } = string.Empty;
}

/// <summary>Cabeça de uma revisão (sem o conteúdo).</summary>
public class ReverseRevisionHead
{
    public Guid Id { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string? ModuleName { get; set; }
    public string DocType { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Mode { get; set; } = "new";
    public string Status { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public double? CoverageRatio { get; set; }
    public int Length { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? ReviewNote { get; set; }
    /// <summary>Andamento da sessão do Claude (etapas, atividade, registro) — ao vivo na tela.</summary>
    public ReverseProgress? Progress { get; set; }
    public DateTimeOffset? ProgressAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProgressRaw { get; set; }
}

public class ReversePublishedInfo
{
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public int Length { get; set; }
    public int Items { get; set; }
}

/// <summary>Situação de um documento do módulo: publicado (seção re-*) e revisão aberta.</summary>
public class ReverseDocStatusResponse
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Required { get; set; }
    /// <summary>none | draft | review | changes | approved | published (o mais "adiantado" para a tela).</summary>
    public string State { get; set; } = "none";
    public ReversePublishedInfo? Published { get; set; }
    public ReverseRevisionHead? Open { get; set; }
    public int PendingSuggestions { get; set; }
}

public class ReverseModuleSummaryResponse
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? BusinessArea { get; set; }
    public string ProjectKind { get; set; } = string.Empty;
    /// <summary>legado | revamp | front | integração | login</summary>
    public string World { get; set; } = string.Empty;
    public List<ReverseDocStatusResponse> Docs { get; set; } = [];
    public int PublishedRequired { get; set; }
    public int RequiredCount { get; set; }
    public bool Complete { get; set; }
    public int InReview { get; set; }
    public int Items { get; set; }
    public List<string> Aliases { get; set; } = [];
}

public class ReverseAssetResponse
{
    public Guid Id { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Notes { get; set; }
    public List<string> Screens { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Caminho relativo à API para baixar o arquivo (null em link).</summary>
    public string? Download { get; set; }
}

public class ReverseModuleResponse : ReverseModuleSummaryResponse
{
    public string? Repository { get; set; }
    public string? Summary { get; set; }
    public List<ReverseSource> Sources { get; set; } = [];
    public string? Notes { get; set; }
    public bool Configured { get; set; }
    public List<ReverseAssetResponse> Assets { get; set; } = [];
    public Dictionary<string, int> ItemsByKind { get; set; } = [];
    public List<ArchitectureRelation> Relations { get; set; } = [];
    public List<ArchitectureIncomingRelation> UsedBy { get; set; } = [];
    /// <summary>Outros módulos da mesma área (o par legado × revamp).</summary>
    public List<string> Siblings { get; set; } = [];
    public bool CanApprove { get; set; }
}

public class ReverseItemHead
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Removed { get; set; }
}

public class ReverseDocResponse
{
    public string ModuleKey { get; set; } = string.Empty;
    public ReverseDocTypeResponse Type { get; set; } = new();
    public string? Content { get; set; }
    public ReversePublishedInfo? Published { get; set; }
    public List<ReverseItemHead> Items { get; set; } = [];
    public List<ReverseRevisionHead> Revisions { get; set; } = [];
    public List<ArchitectureSuggestionResponse> Suggestions { get; set; } = [];
}

public class ReverseItemDiff
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>added | removed | changed</summary>
    public string Change { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
}

public class ReverseRevisionDiff
{
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Changed { get; set; }
    public int Unchanged { get; set; }
    /// <summary>Texto fora dos itens (resumos, diagramas, tabelas) mudou.</summary>
    public bool OtherTextChanged { get; set; }
    public List<ReverseItemDiff> Items { get; set; } = [];
}

public class ReverseRevisionResponse : ReverseRevisionHead
{
    public string Content { get; set; } = string.Empty;
    public ReverseLintResult? Lint { get; set; }
    public JsonElement? Coverage { get; set; }
    public JsonElement? Session { get; set; }
    public int? BaseVersion { get; set; }
    public int? PublishedVersion { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>A seção publicada mudou depois que o rascunho começou (outra revisão publicada no meio).</summary>
    public bool PublishedChangedSinceBase { get; set; }
    public int? CurrentPublishedVersion { get; set; }
    public ReverseRevisionDiff? Diff { get; set; }
    public bool CanApprove { get; set; }
}

/// <summary>Pacote da sessão do Claude: tudo o que a skill precisa para escrever o documento sem perguntar.</summary>
public class ReverseSessionResponse
{
    public ReverseRevisionResponse Revision { get; set; } = new();
    public bool Resumed { get; set; }
    public ReverseModuleResponse Module { get; set; } = new();
    public ReverseDocTypeResponse DocType { get; set; } = new();
    /// <summary>Conteúdo publicado (o ponto de partida do modo melhorar; referência dos IDs no refazer).</summary>
    public string? Published { get; set; }
    public int? PublishedVersion { get; set; }
    public List<ArchitectureSuggestionResponse> Suggestions { get; set; } = [];
    /// <summary>Nota do revisor da última revisão com ajustes pedidos (o que precisa mudar).</summary>
    public string? ReviewNote { get; set; }
    /// <summary>IDs definidos nos outros documentos do módulo (ID → documento) — para referenciar sem redefinir.</summary>
    public Dictionary<string, string> OtherDocIds { get; set; } = [];
    /// <summary>Itens publicados dos módulos relacionados (INT/API/EVT/DB) e itens de outros módulos que citam este.</summary>
    public string Related { get; set; } = string.Empty;
    /// <summary>Seções antigas da Base Solvace do projeto (ponto de partida): chave, título, tamanho.</summary>
    public List<ArchitectureSectionSummaryResponse> ExistingSections { get; set; } = [];
    public double MinCoverage { get; set; }
}

public class ReverseIndexHit
{
    public string Ref { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public string? ModuleName { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string KindLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> Tables { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    public bool Removed { get; set; }
    public double Score { get; set; }
}

public class ReverseItemResponse : ReverseIndexHit
{
    public string Body { get; set; } = string.Empty;
    public List<string> Refs { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
    /// <summary>Itens (de qualquer módulo) que referenciam este.</summary>
    public List<string> ReferencedBy { get; set; } = [];
    public int SectionVersion { get; set; }
}

/// <summary>Configuração efetiva (Skills Configurations) que a tela e a skill usam.</summary>
public class ReverseSettingsResponse
{
    public List<string> ApproverRoles { get; set; } = [];
    public List<string> RequiredDocs { get; set; } = [];
    public string? GateStep { get; set; }
    public double MinCoverage { get; set; }
    public bool CanApprove { get; set; }
    public List<ReverseItemKindResponse> Kinds { get; set; } = [];
}
