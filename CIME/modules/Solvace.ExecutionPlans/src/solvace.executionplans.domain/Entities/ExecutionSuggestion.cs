namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0078: o PRMake conversa com quem trata o card sem travar nada — uma sugestão com ações (ex.: "Registrar a estimativa
/// inicial?", "Lançar 1h em Completed?", "Publicar os resumos de espera na discussion?"). Ninguém espera a resposta: o
/// plano segue; a sugestão só fica disponível até ser respondida ou dispensada. Uma por card e <see cref="DedupKey"/>.
/// </summary>
public class ExecutionSuggestion
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 2_000;
    public const int MaxResultLength = 500;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    /// <summary><see cref="ExecutionSuggestionKind"/>.</summary>
    public string Kind { get; private set; } = string.Empty;
    /// <summary>Não repetir a mesma sugestão no card (ex.: <c>log-hours:montar-analise</c>).</summary>
    public string DedupKey { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Text { get; private set; }
    public List<ExecutionSuggestionAction> Actions { get; private set; } = [];

    /// <summary>open | done | dismissed | failed.</summary>
    public string Status { get; private set; } = ExecutionSuggestionStatus.Open;
    public string? ChosenAction { get; private set; }
    public string? Result { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    protected ExecutionSuggestion() { }

    public ExecutionSuggestion(Guid planId, string cardNumber, string kind, string dedupKey, string title, string? text,
        IEnumerable<ExecutionSuggestionAction> actions, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PlanId = planId;
        CardNumber = cardNumber;
        Kind = kind;
        DedupKey = dedupKey.Length <= 120 ? dedupKey : dedupKey[..120];
        Title = title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];
        Text = text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;
        Actions = actions.ToList();
        if (Actions.Count == 0)
            throw new DomainException("Sugestão sem ações.");
        CreatedAt = now;
    }

    public bool IsOpen => Status == ExecutionSuggestionStatus.Open;

    public void Resolve(string action, bool ok, string? result, string by, DateTimeOffset now)
    {
        if (!IsOpen)
            throw new DomainException("Esta sugestão já foi respondida.");
        if (Actions.All(a => a.Key != action))
            throw new DomainException($"Ação inválida: '{action}'.");
        ChosenAction = action;
        Status = ok ? ExecutionSuggestionStatus.Done : ExecutionSuggestionStatus.Failed;
        Result = result is { Length: > MaxResultLength } ? result[..MaxResultLength] : result;
        ResolvedBy = by.Length <= 200 ? by : by[..200];
        ResolvedAt = now;
    }

    /// <summary>Falhou (ex.: DevOps recusou): pode tentar de novo.</summary>
    public void Reopen()
    {
        if (Status == ExecutionSuggestionStatus.Failed)
            Status = ExecutionSuggestionStatus.Open;
    }

    public void Dismiss(string by, DateTimeOffset now)
    {
        if (!IsOpen) return;
        Status = ExecutionSuggestionStatus.Dismissed;
        ResolvedBy = by.Length <= 200 ? by : by[..200];
        ResolvedAt = now;
    }
}

public class ExecutionSuggestionAction
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Primary { get; set; }
}

public static class ExecutionSuggestionStatus
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Dismissed = "dismissed";
    public const string Failed = "failed";
}

public static class ExecutionSuggestionKind
{
    /// <summary>Registrar a estimativa inicial (Original/Remaining/Completed do AI Configurations).</summary>
    public const string InitialEstimate = "initial-estimate";
    /// <summary>Lançar horas no Completed (e tirar do Remaining) ao avançar uma etapa.</summary>
    public const string LogHours = "log-hours";
    /// <summary>Publicar sozinho os resumos de espera (chamado, replicar no ambiente, PR, QA) na discussion.</summary>
    public const string SituationAutoPost = "situation-auto-post";
}
