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
    /// <summary>Tipo do item (WorkItemTypes: task, pull-request, review, fix...; padrão: o do modelo ou task). bug/user-story → fix/implementation.</summary>
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

/// <summary>0094: <c>POST WorkItems/card/{card}/related</c> — adiciona um relatado à mão (número de item ou sub-tarefa).</summary>
public class AddRelatedRequest
{
    public long Number { get; set; }
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

/// <summary>"Já resolvi": <c>POST WorkItems/tasks/{taskNumber}/resolve</c>.</summary>
public class WorkTaskResolveRequest
{
    /// <summary>O que a pessoa fez (opcional) — vai para o resultado da sub-tarefa, o Claude lê ao retomar.</summary>
    public string? Note { get; set; }
}

/// <summary>Chamado na Bancada: <c>POST WorkItems/tasks/{taskNumber}/ticket</c>.</summary>
public class WorkTaskTicketRequest
{
    /// <summary>Link (ou número) do chamado.</summary>
    public string? Url { get; set; }
    public string? Title { get; set; }
}

/// <summary>Chamado na Bancada: <c>PUT WorkItems/tasks/{taskNumber}/ticket</c>.</summary>
public class WorkTaskTicketStatusRequest
{
    /// <summary>open | resolved | closed.</summary>
    public string? Status { get; set; }
    /// <summary>Link do plano (sub-tarefa espelho com mais de um chamado); vazio = o mais recente.</summary>
    public Guid? LinkId { get; set; }
}

/// <summary>0085: <c>POST WorkItems/{number}/cancel</c> e <c>POST ExecutionPlan/{id}/cancel</c>.</summary>
public class CancelWorkItemRequest
{
    public string? Reason { get; set; }
    public bool Undo { get; set; }
}

/// <summary><c>POST WorkItems/{number}/reopen</c> — reabre item avulso concluído/cancelado/com falha para refazer ou acrescentar.</summary>
public class ReopenWorkItemRequest
{
    /// <summary>Sub-tarefas que voltam a pendente para serem refeitas (vazio = só reabrir, para acrescentar sub-tarefa).</summary>
    public List<long>? Redo { get; set; }
    /// <summary>Motivo (vai para o item, para o resultado das sub-tarefas refeitas e para a Timeline).</summary>
    public string? Reason { get; set; }
    /// <summary>Já executar o item depois de reabrir.</summary>
    public bool Execute { get; set; }
}

/// <summary><c>POST WorkItems/{number}/tasks</c> — sub-tarefas a mais num item avulso aberto.</summary>
public class AddWorkTasksRequest
{
    public List<WorkTaskInput> Tasks { get; set; } = [];
    /// <summary>Número da sub-tarefa pendente antes da qual entram (vazio = no fim).</summary>
    public long? Before { get; set; }
    /// <summary>Já executar o item depois de acrescentar.</summary>
    public bool Execute { get; set; }
}
