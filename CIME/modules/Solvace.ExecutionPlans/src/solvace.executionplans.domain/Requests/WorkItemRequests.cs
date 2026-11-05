using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Requests;

/// <summary>0085: sub-tarefa ao criar/editar um item. <see cref="Number"/> (só na edição) mantém a sub-tarefa existente.</summary>
public class WorkTaskInput
{
    public long? Number { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Kind { get; set; }
    /// <summary>claude | user (padrão claude).</summary>
    public string? Executor { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    /// <summary>Etapa do plano que a sub-tarefa espelha (itens gerados do plano).</summary>
    public string? StepKey { get; set; }
}

/// <summary>0085: <c>POST WorkItems/card/{card}</c>.</summary>
public class CreateWorkItemRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>bug | user-story (padrão: o tipo do card).</summary>
    public string? Type { get; set; }
    public string? TemplateKey { get; set; }
    public List<WorkTaskInput> Tasks { get; set; } = [];
    /// <summary>Cria mesmo com outro item igual (mesmo fingerprint) no card — exige <see cref="DuplicateReason"/>.</summary>
    public bool AllowDuplicate { get; set; }
    public string? DuplicateReason { get; set; }
    /// <summary>MCP: fase do plano que criou (analysis|correction) — define a origem quando não é o especialista.</summary>
    public string? PlanPhase { get; set; }
    /// <summary>Plano ao qual o item fica ligado (opcional).</summary>
    public Guid? PlanId { get; set; }
}

/// <summary>0085: <c>PUT WorkItems/{number}</c> (só item pendente).</summary>
public class UpdateWorkItemRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>null = mantém as sub-tarefas.</summary>
    public List<WorkTaskInput>? Tasks { get; set; }
}

/// <summary>0085: <c>POST WorkItems/tasks/{taskNumber}/{action}</c>.</summary>
public class WorkTaskActionRequest
{
    /// <summary>Resultado (markdown) — em fail, o motivo.</summary>
    public string? Result { get; set; }
    /// <summary>Saídas (objeto JSON livre: pr, branch, commit...).</summary>
    public JsonElement? Output { get; set; }
    /// <summary>Como desfazer o que a sub-tarefa fez (gravado ao concluir).</summary>
    public WorkTaskUndo? Undo { get; set; }
    /// <summary>Links a acrescentar ao item (PR aberto, branch criada...).</summary>
    public List<WorkItemLink>? Links { get; set; }
}

/// <summary>0085: <c>POST WorkItems/{number}/cancel</c> e <c>POST ExecutionPlan/{id}/cancel</c>.</summary>
public class CancelWorkItemRequest
{
    public string? Reason { get; set; }
    public bool Undo { get; set; }
}
