using System.Text.Json;
using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.domain.Requests;

/// <summary>Fontes, apelidos e notas de um módulo da engenharia reversa (0052); null mantém.</summary>
public class UpsertReverseModuleRequest
{
    public List<ReverseSource>? Sources { get; set; }
    /// <summary>Valores do campo "Module" dos cards que caem neste módulo ("Kaizen", "Users (Revamp)").</summary>
    public List<string>? Aliases { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Abre (ou retoma) a sessão de um documento: new | improve (parte do publicado) | redo (do zero).</summary>
public class StartReverseSessionRequest
{
    public string? Mode { get; set; }
}

/// <summary>Grava o rascunho; null mantém. Coverage/Session são JSON livres enviados pela skill.</summary>
public class SaveReverseRevisionRequest
{
    public string? Content { get; set; }
    public string? Summary { get; set; }
    public JsonElement? Coverage { get; set; }
    public double? CoverageRatio { get; set; }
    public JsonElement? Session { get; set; }
    /// <summary>0053: o que a sessão fez com cada sugestão do pacote (aplicada + itens / recusada + motivo); null mantém.</summary>
    public List<ReverseSuggestionDecision>? SuggestionDecisions { get; set; }
}

/// <summary>Termo sugerido do glossário: alias (apelido do módulo) | keyword (palavra-chave do projeto) | dismiss.</summary>
public class ResolveReverseTermRequest
{
    public string Term { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

/// <summary>approve | changes (nota obrigatória) | discard.</summary>
public class ReviewReverseRevisionRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class PublishReverseRevisionRequest
{
    /// <summary>Aprova e publica de uma vez (revisão enviada).</summary>
    public bool Approve { get; set; }
    public string? Note { get; set; }
}

/// <summary>Checagem sem gravar (a skill confere antes de enviar).</summary>
public class LintReverseDocumentRequest
{
    public string DocType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double? CoverageRatio { get; set; }
}

public class CreateReverseAssetLinkRequest
{
    public string? Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<string>? Screens { get; set; }
}

/// <summary>A análise do card consultou estes itens (registro para a trava da investigação).</summary>
public class ReverseConsultedRequest
{
    public string Card { get; set; } = string.Empty;
    public List<string> Refs { get; set; } = [];
}

/// <summary>0054: armadilha nova (ligada a itens). Quem não aprova cria "a conferir".</summary>
public class UpsertReverseInfraRequest
{
    public string Account { get; set; } = string.Empty;
    /// <summary>Objeto JSON (resumo, recursos do módulo, esteiras, logs, lacunas).</summary>
    public System.Text.Json.JsonElement Data { get; set; }
}

public class CreateReverseTrapRequest
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<string>? Items { get; set; }
    public List<string>? Cards { get; set; }
    /// <summary>manual | suggestion | migrated | learning</summary>
    public string? Origin { get; set; }
}

public class UpdateReverseTrapRequest
{
    public string? Title { get; set; }
    public string? Text { get; set; }
    public List<string>? Items { get; set; }
    public List<string>? Cards { get; set; }
    /// <summary>true = conferida (aprovador).</summary>
    public bool Confirm { get; set; }
}

/// <summary>0054: liga uma sugestão a um item (e documento) da engenharia reversa.</summary>
public class LinkSuggestionItemRequest
{
    public string? ItemId { get; set; }
    public string? SectionKey { get; set; }
}

/// <summary>Commit de uma fonte do módulo de onde o índice técnico foi extraído (0097).</summary>
public class ReverseTechnicalCommit
{
    public string Repository { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public string? Branch { get; set; }
}

/// <summary>
/// 0097: índice técnico gerado sem IA pela skill (<c>re.sh indice</c>) — publicado direto na seção <c>re-tecnico</c>.
/// </summary>
public class PublishTechnicalIndexRequest
{
    public string Content { get; set; } = string.Empty;
    public List<ReverseTechnicalCommit> Commits { get; set; } = [];
    public DateTimeOffset? GeneratedAt { get; set; }
    /// <summary>Contagem por tipo (END, ROT, TAB, DBO, CHV, MSG) como a skill contou.</summary>
    public Dictionary<string, int>? Counts { get; set; }
}

/// <summary>Um item marcado como desatualizado (0097): o trecho de código que ele cita mudou desde a geração.</summary>
public class ReverseStaleItemRequest
{
    public string DocType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>0097: marca itens do módulo como desatualizados (reancoragem); <c>replace</c> limpa as marcas do módulo antes.</summary>
public class MarkReverseStaleRequest
{
    public string? Commit { get; set; }
    public List<ReverseStaleItemRequest> Items { get; set; } = [];
    public bool Replace { get; set; }
}
