namespace solvace.prform.domain.Entities;

/// <summary>
/// Consumo de uma chamada de IA (0042): quem, qual ação, provedor/modelo, tokens e o custo estimado pela tabela de
/// preços do plugin "AI Configurations" (AiModelPricesUsdPerMillion). A chave do provedor é a do usuário quando o plugin
/// é pessoal — o custo é dele.
/// </summary>
public class AiUsageRecord
{
    public const int MaxUserNameLength = 200;
    public const int MaxActionLength = 80;
    public const int MaxRouteLength = 200;
    public const int MaxProviderLength = 30;
    public const int MaxModelLength = 100;
    public const int MaxErrorLength = 300;

    public long Id { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? UserExternalId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    /// <summary>Ação de negócio (ex.: base-solvace:ask, pr:generate) — vem do cabeçalho X-AI-Action ou da rota.</summary>
    public string Action { get; private set; } = string.Empty;
    public string Route { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string? Model { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    /// <summary>Custo estimado em dólares; null quando o modelo não está na tabela de preços.</summary>
    public decimal? CostUsd { get; private set; }
    public int DurationMs { get; private set; }
    public bool Success { get; private set; }
    public string? Error { get; private set; }

    protected AiUsageRecord() { }

    public AiUsageRecord(DateTimeOffset createdAt, Guid? userExternalId, string? userName, string action, string? route, string? provider,
        string? model, int inputTokens, int outputTokens, decimal? costUsd, int durationMs, bool success, string? error)
    {
        CreatedAt = createdAt;
        UserExternalId = userExternalId;
        UserName = Cut(userName, MaxUserNameLength) ?? string.Empty;
        Action = Cut(action, MaxActionLength) ?? "desconhecida";
        Route = Cut(route, MaxRouteLength) ?? string.Empty;
        Provider = Cut(provider, MaxProviderLength) ?? string.Empty;
        Model = Cut(model, MaxModelLength);
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        CostUsd = costUsd;
        DurationMs = Math.Max(0, durationMs);
        Success = success;
        Error = Cut(error, MaxErrorLength);
    }

    private static string? Cut(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }
}
