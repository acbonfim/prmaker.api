using System.Text.Json;

namespace solvace.azure.domain.Models;

// 0099 (Mesa de gestão): metadados, board e cards do Azure DevOps lidos e gravados pelo PRMake com a integração pessoal
// de quem age. Nomes das propriedades = contrato REST api/v1/DevOps (camelCase na resposta).

/// <summary>Tipo de work item do projeto (Bug, User Story, Task...).</summary>
public sealed record DevOpsTypeInfo(string Name, string? Color, string? Icon, string? Description, bool IsDisabled);

/// <summary>
/// Campo de um tipo de work item. <c>Type</c>: string | integer | double | dateTime | html | plainText | boolean | treePath |
/// identity | picklistString | picklistInteger.
/// </summary>
public sealed record DevOpsFieldInfo(string ReferenceName, string Name, string Type, bool Required, bool ReadOnly,
    IReadOnlyList<string> AllowedValues, string? DefaultValue, string? HelpText);

/// <summary>Área ou iteração (árvore achatada): <c>Path</c> no formato do campo (<c>Projeto\Time\Sub</c>).</summary>
public sealed record DevOpsNodeInfo(string Path, string Name, int Depth);

/// <summary>
/// Pessoa da organização (ajustes 1 da 0099: membros dos times, usuários da organização, busca de identidades e quem aparece em
/// cards recentes — sem repetir pelo <c>UniqueName</c>).
/// </summary>
public sealed record DevOpsPersonInfo(string DisplayName, string UniqueName, string Id, string? ImageUrl);

/// <summary>Time ou board (id + nome).</summary>
public sealed record DevOpsNamedRef(string Id, string Name);

/// <summary>Coluna do board: <c>ColumnType</c> incoming | inProgress | outgoing; <c>StateMappings</c> tipo → estado.</summary>
public sealed class DevOpsBoardColumn
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ItemLimit { get; set; }
    public string ColumnType { get; set; } = "inProgress";
    public bool IsSplit { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string> StateMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Lane (swimlane) do board — a padrão vem sem nome no DevOps (<c>Name</c> vazio, <c>IsDefault</c>).</summary>
public sealed record DevOpsBoardLane(string Id, string Name, bool IsDefault);

/// <summary>Campos do board (cada board tem os seus <c>WEF_&lt;id&gt;_Kanban.Column|Lane|Column.Done</c>).</summary>
public sealed record DevOpsBoardFields(string? Column, string? Lane, string? Done);

/// <summary>Tipo de work item que aparece no board, com a cor.</summary>
public sealed record DevOpsBoardType(string Name, string? Color);

/// <summary>
/// Configuração do board lida do DevOps. <c>ConfigHash</c> = hash estável do JSON normalizado (colunas, lanes, campos,
/// tipos, campo de ordem): mudou ⇒ a tela redesenha.
/// </summary>
public sealed class DevOpsBoardConfig
{
    public string Team { get; set; } = string.Empty;
    public string Board { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string ConfigHash { get; set; } = string.Empty;
    public List<DevOpsBoardColumn> Columns { get; set; } = [];
    public List<DevOpsBoardLane> Lanes { get; set; } = [];
    public DevOpsBoardFields Fields { get; set; } = new(null, null, null);
    public List<DevOpsBoardType> Types { get; set; } = [];
    public string? OrderField { get; set; }
}

/// <summary>Pessoa de um campo identidade (responsável).</summary>
public sealed record DevOpsIdentityRef(string DisplayName, string? UniqueName, string? ImageUrl);

/// <summary>Cartão do board.</summary>
public sealed class DevOpsBoardItem
{
    public int Id { get; set; }
    public int Rev { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DevOpsIdentityRef? AssignedTo { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Column { get; set; }
    public bool Done { get; set; }
    public string? Lane { get; set; }
    public double? Order { get; set; }
    public int? Priority { get; set; }
    public int? ParentId { get; set; }
    public DateTimeOffset? ChangedDate { get; set; }
}

/// <summary><c>POST DevOps/board/items</c>. <c>Scope</c> epic = só os cards do épico (<c>Ids</c> resolvidos pelo host).</summary>
public sealed class DevOpsBoardItemsRequest
{
    public string? Team { get; set; }
    public string? Board { get; set; }
    public long? Epic { get; set; }
    public string? Scope { get; set; }
    /// <summary>0099: ids dos cards (escopo epic) — a tela pode mandar; sem eles o host resolve pelos cards do épico.</summary>
    public List<string>? Ids { get; set; }
    public string? Text { get; set; }
    public List<string>? Types { get; set; }
    public List<string>? AssignedTo { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? States { get; set; }
    public int? Top { get; set; }
    /// <summary>
    /// Ajustes 1 da 0099: com <c>Epic</c>, aplica no servidor os filtros salvos do épico (<see cref="DevOpsBoardFilters"/>) — padrão
    /// false na REST (a tela filtra no navegador), true no MCP <c>prmake_devops_board</c>.
    /// </summary>
    public bool? UseEpicFilters { get; set; }
}

/// <summary>
/// Cartões do board. <c>FiltersApplied</c> (ajustes 1 da 0099): os filtros salvos do épico aplicados no servidor (null = nenhum —
/// <c>useEpicFilters</c> desligado, sem épico ou épico sem filtro).
/// </summary>
public sealed record DevOpsBoardItemsResponse(string ConfigHash, IReadOnlyList<DevOpsBoardItem> Items, DevOpsBoardFilters? FiltersApplied = null);

/// <summary>
/// Ajustes 1 da 0099: filtros do board salvos no épico (a tela e o especialista usam os mesmos). Mesma semântica do filtro da tela
/// (<c>board.logic.ts</c>): valores do mesmo filtro em OU (tags: todas as escolhidas, como o filtro de tags do DevOps), filtros
/// diferentes em E; texto = todas as palavras em id/título/tags/responsável/estado/tipo, sem caixa nem acento. Lane padrão = <c>""</c>
/// (o que <see cref="DevOpsBoardItem.Lane"/> devolve); responsável pelo <c>uniqueName</c> (<c>__unassigned__</c> = sem responsável).
/// <c>HiddenColumns</c>: nomes das colunas que o épico não mostra (vazio = todas; coluna nova do DevOps aparece sozinha) — cards nelas
/// ficam de fora.
/// </summary>
public sealed class DevOpsBoardFilters
{
    public List<string> Lanes { get; set; } = [];
    public List<string> HiddenColumns { get; set; } = [];
    public List<string> Types { get; set; } = [];
    public List<string> States { get; set; } = [];
    public List<string> AssignedTo { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string Text { get; set; } = string.Empty;

    /// <summary>Nenhum filtro ligado.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => (Lanes?.Count ?? 0) == 0 && (HiddenColumns?.Count ?? 0) == 0 && (Types?.Count ?? 0) == 0 && (States?.Count ?? 0) == 0
                           && (AssignedTo?.Count ?? 0) == 0 && (Tags?.Count ?? 0) == 0 && string.IsNullOrWhiteSpace(Text);
}

/// <summary>Filtros da consulta (viram WIQL).</summary>
public sealed class DevOpsQueryFilter
{
    public List<string>? Types { get; set; }
    public List<string>? States { get; set; }
    public string? AreaPath { get; set; }
    public string? IterationPath { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? AssignedTo { get; set; }
    public string? Text { get; set; }
    public DateTimeOffset? CreatedFrom { get; set; }
    public DateTimeOffset? CreatedTo { get; set; }
    public DateTimeOffset? ChangedFrom { get; set; }
}

/// <summary><c>POST DevOps/query</c>: WIQL pronta ou filtros; campos escolhidos; no máximo <c>Top</c> (padrão 500).</summary>
public sealed class DevOpsQueryRequest
{
    public string? Wiql { get; set; }
    public DevOpsQueryFilter? Filter { get; set; }
    public List<string>? Fields { get; set; }
    public int? Top { get; set; }
}

public sealed record DevOpsQueryItem(int Id, Dictionary<string, JsonElement> Fields);

public sealed record DevOpsQueryResponse(int Count, IReadOnlyList<DevOpsQueryItem> Items);

/// <summary>Relação do card (pai, filhos, links, anexos): <c>Id</c> quando aponta para outro work item.</summary>
public sealed record DevOpsCardRelation(string Rel, int? Id, string Url, string? Name);

/// <summary>Card com todos os campos (<c>$expand=relations</c>).</summary>
public sealed class DevOpsCardDetail
{
    public int Id { get; set; }
    public int Rev { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Fields { get; set; } = new();
    public List<string> Tags { get; set; } = [];
    public string Url { get; set; } = string.Empty;
    public List<DevOpsCardRelation> Relations { get; set; } = [];
}

/// <summary><c>POST DevOps/workitems</c>. <c>Epic</c>: o host grava o card no épico (EpicCard created).</summary>
public sealed class DevOpsCreateCardRequest
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Fields { get; set; }
    public List<string>? Tags { get; set; }
    public int? ParentId { get; set; }
    public long? Epic { get; set; }
    public string? Team { get; set; }
    public string? Board { get; set; }
    public string? Column { get; set; }
    public string? Lane { get; set; }
}

/// <summary><c>PATCH DevOps/workitems/{id}</c>: campo com <c>null</c> = limpa; tags somadas/tiradas das atuais.</summary>
public sealed class DevOpsUpdateCardRequest
{
    public Dictionary<string, JsonElement>? Fields { get; set; }
    public List<string>? AddTags { get; set; }
    public List<string>? RemoveTags { get; set; }
    /// <summary>Comentário na discussion (markdown → HTML), depois dos campos.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// <c>POST DevOps/workitems/{id}/move</c>: coluna (nome ou id) + estado mapeado do tipo + done + lane; ordem pelo
/// <c>workitemsorder</c> quando vier um vizinho. Sem time/board: o time padrão do projeto e o board dele com o tipo do card.
/// </summary>
public sealed class DevOpsMoveCardRequest
{
    public string? Team { get; set; }
    public string? Board { get; set; }
    public string Column { get; set; } = string.Empty;
    public string? Lane { get; set; }
    public bool? Done { get; set; }
    public int? PreviousId { get; set; }
    public int? NextId { get; set; }
}

/// <summary><c>POST DevOps/workitems/{id}/comment</c> (markdown → HTML).</summary>
public sealed class DevOpsCommentRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Resultado de uma gravação: o card como ficou, o resumo legível (Timeline, cartão do especialista) e a ação do tempo real
/// quando não é a padrão da operação (ex.: <c>reordered</c> — só mudou a ordem).
/// </summary>
public sealed record DevOpsCardWriteResult(DevOpsCardDetail Card, string Summary, string? Action = null);
