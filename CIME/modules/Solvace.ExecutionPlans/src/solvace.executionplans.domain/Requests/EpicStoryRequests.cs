using System.Text.Json;

namespace solvace.executionplans.domain.Requests;

/// <summary>
/// 0099 (ajustes 2): corpo do <c>POST Epics/{n}/stories</c> / <c>PUT EpicStories/{number}</c> — o formulário inteiro (no PUT, campos
/// null limpam; <c>type</c>/<c>status</c> null não mexem). Título obrigatório. Texto em markdown.
/// </summary>
public class SaveEpicStoryRequest
{
    /// <summary>user-story | bug (padrão user-story).</summary>
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>Critérios de aceite (User Story).</summary>
    public string? AcceptanceCriteria { get; set; }
    /// <summary>Passos de reprodução (Bug).</summary>
    public string? ReproSteps { get; set; }
    /// <summary>1 = mais alta (como no DevOps).</summary>
    public int? Priority { get; set; }
    /// <summary>Severidade do Bug, no texto do picklist do DevOps (ex.: "2 - High").</summary>
    public string? Severity { get; set; }
    public List<string>? Tags { get; set; }
    /// <summary>Campos extras do DevOps <c>{referenceName: valor}</c> (área, iteração, responsável…).</summary>
    public Dictionary<string, JsonElement>? Fields { get; set; }
    /// <summary>draft | ready | discarded (converted só pela conversão).</summary>
    public string? Status { get; set; }
    /// <summary>0104: card de origem (só o MCP do Especialista do caso preenche; o JSON da REST não chega aqui).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourceCard { get; set; }
}

/// <summary>0099 (ajustes 2): <c>POST EpicStories/{number}/convert</c> — onde o card nasce (padrão: o time/board do épico, coluna inicial).</summary>
public class ConvertEpicStoryRequest
{
    public string? Team { get; set; }
    public string? Board { get; set; }
    public string? Column { get; set; }
    public string? Lane { get; set; }
    /// <summary>
    /// Campos do DevOps preenchidos na hora de converter (<c>{referenceName: valor}</c>), por cima dos da história — os obrigatórios
    /// que faltavam (ex.: <c>Custom.Module</c>).
    /// </summary>
    public Dictionary<string, JsonElement>? Fields { get; set; }
}
