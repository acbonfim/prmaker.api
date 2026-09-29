namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Um "pedaço" de andamento enviado pela skill (o que está fazendo, o que achou, o que decidiu).
/// O <see cref="Id"/> é sequencial no banco inteiro: serve de cursor para o front buscar só o que é novo.
/// </summary>
public class ExecutionLog
{
    public const int MaxMessageLength = 20_000;
    public const int MaxClientIdLength = 100;

    public long Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string? StepKey { get; private set; }
    public string Kind { get; private set; } = ExecutionLogKind.Info;
    public string Message { get; private set; } = string.Empty;
    public string? ClientId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected ExecutionLog() { }

    public ExecutionLog(Guid planId, string? stepKey, string? kind, string message, string? clientId, DateTimeOffset now)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new DomainException("A mensagem do registro de andamento é obrigatória.");
        if (trimmed.Length > MaxMessageLength)
            throw new DomainException($"Cada registro de andamento pode ter no máximo {MaxMessageLength} caracteres; envie em pedaços menores.");

        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? ExecutionLogKind.Info : kind.Trim().ToLowerInvariant();
        if (!ExecutionLogKind.All.Contains(normalizedKind))
            throw new DomainException($"Tipo de registro inválido: '{kind}'.");

        var normalizedClientId = clientId?.Trim();
        if (normalizedClientId is { Length: > MaxClientIdLength })
            throw new DomainException($"clientId pode ter no máximo {MaxClientIdLength} caracteres.");

        PlanId = planId;
        StepKey = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        Kind = normalizedKind;
        Message = trimmed;
        ClientId = string.IsNullOrEmpty(normalizedClientId) ? null : normalizedClientId;
        CreatedAt = now;
    }
}
