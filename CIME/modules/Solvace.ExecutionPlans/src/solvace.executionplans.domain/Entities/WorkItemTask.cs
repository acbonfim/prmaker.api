using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085: sub-tarefa do item de trabalho — roda na ordem dentro do item. O <see cref="Number"/> vem da mesma sequência
/// global do item. Parâmetros, saídas e "como desfazer" ficam em JSON (jsonb).
/// </summary>
public partial class WorkItemTask
{
    public const int MaxTitleLength = 300;
    public const int MaxTextLength = 20_000;
    public const int MaxResultLength = 50_000;
    public const int MaxKindLength = 40;
    public const int MaxParams = 30;
    public const int MaxParamValueLength = 2_000;
    public const int MaxOutputLength = 20_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public Guid Id { get; private set; }
    public long Number { get; private set; }
    public Guid WorkItemId { get; private set; }
    public int Order { get; internal set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Kind { get; private set; } = WorkTaskKind.Manual;
    public string Executor { get; private set; } = ExecutionExecutor.Claude;
    /// <summary>JSON (jsonb) <c>Record&lt;string,string&gt;</c>.</summary>
    public string ParamsJson { get; private set; } = "{}";
    public string Status { get; private set; } = WorkTaskStatus.Pending;
    public string? StepKey { get; private set; }
    public string? Result { get; private set; }
    /// <summary>JSON (jsonb) livre com as saídas (PR, branch, commit...).</summary>
    public string OutputJson { get; private set; } = "{}";
    /// <summary>JSON (jsonb) <see cref="WorkTaskUndo"/> (null = nada a desfazer).</summary>
    public string? UndoJson { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    protected WorkItemTask() { }

    internal WorkItemTask(Guid workItemId, int order, WorkTaskInput input, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        WorkItemId = workItemId;
        Order = order;
        StepKey = string.IsNullOrWhiteSpace(input.StepKey) ? null : ExecutionStep.NormalizeKey(input.StepKey);
        UpdatedAt = now;
        Reshape(order, input);
    }

    public Dictionary<string, string> Params
    {
        get
        {
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(ParamsJson, JsonOptions) ?? []; }
            catch (JsonException) { return []; }
        }
    }

    public JsonElement OutputElement
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputJson)) return EmptyObject;
            try { return JsonDocument.Parse(OutputJson).RootElement.Clone(); }
            catch (JsonException) { return EmptyObject; }
        }
    }

    public WorkTaskUndo? Undo
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UndoJson)) return null;
            try { return JsonSerializer.Deserialize<WorkTaskUndo>(UndoJson, JsonOptions); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>Título, descrição, tipo, executor e parâmetros (criação/edição do item pendente).</summary>
    internal void Reshape(int order, WorkTaskInput input)
    {
        Order = order;
        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            throw new DomainException("O título da sub-tarefa é obrigatório.");
        Title = title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];

        var description = input.Description?.Trim();
        if (description is { Length: > MaxTextLength })
            throw new DomainException($"A descrição da sub-tarefa pode ter no máximo {MaxTextLength} caracteres.");
        Description = string.IsNullOrEmpty(description) ? null : description;

        Kind = NormalizeKind(input.Kind);

        var executor = string.IsNullOrWhiteSpace(input.Executor) ? ExecutionExecutor.Claude : input.Executor.Trim().ToLowerInvariant();
        if (!ExecutionExecutor.All.Contains(executor))
            throw new DomainException($"Executor inválido: '{input.Executor}' (use claude ou user).");
        Executor = executor;

        ParamsJson = SerializeParams(input.Params);
    }

    public static string NormalizeKind(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (k.Length == 0) return WorkTaskKind.Manual;
        if (k.Length > MaxKindLength || !KindPattern().IsMatch(k))
            throw new DomainException($"Tipo de sub-tarefa inválido: '{kind}'. Use letras minúsculas, números e '-' (até {MaxKindLength}).");
        return k;
    }

    private static string SerializeParams(Dictionary<string, string>? values)
    {
        if (values is null || values.Count == 0) return "{}";
        if (values.Count > MaxParams)
            throw new DomainException($"Uma sub-tarefa pode ter no máximo {MaxParams} parâmetros.");
        var clean = new Dictionary<string, string>();
        foreach (var (key, value) in values)
        {
            var k = key?.Trim();
            if (string.IsNullOrEmpty(k) || k.Length > 80) continue;
            var v = (value ?? string.Empty).Trim();
            if (v.Length > MaxParamValueLength)
                throw new DomainException($"O parâmetro '{k}' passa de {MaxParamValueLength} caracteres.");
            clean[k] = v;
        }
        return JsonSerializer.Serialize(clean, JsonOptions);
    }

    /// <summary>Transição pedida por ação (valida a origem).</summary>
    internal void Apply(string action, string? result, string? outputJson, WorkTaskUndo? undo, DateTimeOffset now)
    {
        switch (action)
        {
            case WorkTaskAction.Start:
                Require(action, WorkTaskStatus.Pending, WorkTaskStatus.Waiting, WorkTaskStatus.Failed, WorkTaskStatus.Running);
                Set(WorkTaskStatus.Running, now);
                break;
            case WorkTaskAction.Wait:
                Require(action, WorkTaskStatus.Running, WorkTaskStatus.Pending, WorkTaskStatus.Waiting);
                Set(WorkTaskStatus.Waiting, now);
                break;
            case WorkTaskAction.Complete:
                Require(action, WorkTaskStatus.Pending, WorkTaskStatus.Running, WorkTaskStatus.Waiting, WorkTaskStatus.Failed);
                Set(WorkTaskStatus.Completed, now);
                break;
            case WorkTaskAction.Fail:
                Require(action, WorkTaskStatus.Pending, WorkTaskStatus.Running, WorkTaskStatus.Waiting);
                Set(WorkTaskStatus.Failed, now);
                break;
            case WorkTaskAction.Skip:
                Require(action, WorkTaskStatus.Pending, WorkTaskStatus.Waiting, WorkTaskStatus.Failed, WorkTaskStatus.Running);
                Set(WorkTaskStatus.Skipped, now);
                break;
            case WorkTaskAction.Cancel:
                Require(action, WorkTaskStatus.Pending, WorkTaskStatus.Running, WorkTaskStatus.Waiting);
                Set(WorkTaskStatus.Cancelled, now);
                break;
            case WorkTaskAction.Revert:
                Require(action, WorkTaskStatus.Completed);
                Set(WorkTaskStatus.Reverted, now);
                break;
            default:
                throw new DomainException($"Ação inválida: '{action}'.");
        }
        SetResult(result);
        if (outputJson is not null) SetOutput(outputJson);
        if (undo is not null) SetUndo(undo);
    }

    /// <summary>Espelha o status da etapa do plano (sem validar a transição).</summary>
    internal void Force(string status, string? reason, DateTimeOffset now)
    {
        Set(status, now);
        if (!string.IsNullOrWhiteSpace(reason) && status is WorkTaskStatus.Cancelled or WorkTaskStatus.Skipped or WorkTaskStatus.Failed)
            SetResult(reason);
    }

    /// <summary>O desfazer desta sub-tarefa falhou: ela continua concluída; o motivo vai para o resultado.</summary>
    internal void NoteUndoFailure(string? reason, DateTimeOffset now)
    {
        var r = string.IsNullOrWhiteSpace(reason) ? "Não foi possível desfazer." : reason.Trim();
        SetResult(string.IsNullOrEmpty(Result) ? $"Desfazer falhou: {r}" : $"{Result}\n\nDesfazer falhou: {r}");
        UpdatedAt = now;
    }

    private void Set(string status, DateTimeOffset now)
    {
        if (status is WorkTaskStatus.Running or WorkTaskStatus.Waiting)
        {
            StartedAt ??= now;
            CompletedAt = null;
        }
        else if (status != WorkTaskStatus.Pending)
        {
            CompletedAt = now;
        }
        Status = status;
        UpdatedAt = now;
    }

    private void Require(string action, params string[] from)
    {
        if (!from.Contains(Status))
            throw new DomainException($"A sub-tarefa #{Number} está {Status} — não dá para {action}.");
    }

    private void SetResult(string? result)
    {
        var r = result?.Trim();
        if (string.IsNullOrEmpty(r)) return;
        Result = r.Length <= MaxResultLength ? r : r[..MaxResultLength];
    }

    private void SetOutput(string json)
    {
        if (json.Length > MaxOutputLength)
            throw new DomainException($"As saídas da sub-tarefa passam de {MaxOutputLength} caracteres.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new DomainException("As saídas da sub-tarefa precisam ser um objeto JSON.");
            // Mescla com as saídas que já existem (start pode mandar a branch, complete o PR).
            var merged = new Dictionary<string, JsonElement>();
            try
            {
                using var current = JsonDocument.Parse(OutputJson);
                if (current.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in current.RootElement.EnumerateObject()) merged[p.Name] = p.Value.Clone();
            }
            catch (JsonException) { }
            foreach (var p in doc.RootElement.EnumerateObject()) merged[p.Name] = p.Value.Clone();
            OutputJson = JsonSerializer.Serialize(merged, JsonOptions);
        }
        catch (JsonException)
        {
            throw new DomainException("As saídas da sub-tarefa não são um JSON válido.");
        }
    }

    private void SetUndo(WorkTaskUndo undo)
    {
        var action = undo.Action?.Trim();
        if (string.IsNullOrEmpty(action)) return;
        UndoJson = JsonSerializer.Serialize(new WorkTaskUndo
        {
            Action = action.Length <= 80 ? action : action[..80],
            Params = undo.Params ?? [],
        }, JsonOptions);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex KindPattern();
}
