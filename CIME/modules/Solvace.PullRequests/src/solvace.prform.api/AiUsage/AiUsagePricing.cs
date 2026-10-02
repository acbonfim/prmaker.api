using System.Text.Json;

namespace solvace.prform.AiUsage;

/// <summary>
/// Tabela de preços (dólares por milhão de tokens) do plugin "AI Configurations" → <c>AiModelPricesUsdPerMillion</c>:
/// <c>{"claude-haiku-4-5": {"input": 1, "output": 5}, ...}</c>. O modelo casa pelo prefixo mais longo
/// (<c>claude-haiku-4-5-20251001</c> → <c>claude-haiku-4-5</c>). Modelo fora da tabela → custo null (só os tokens).
/// 0044: <c>cacheRead</c>/<c>cacheWrite</c> opcionais por modelo (padrão: 10% e 125% da entrada — a regra da Anthropic).
/// </summary>
public static class AiUsagePricing
{
    public const decimal DefaultCacheReadFactor = 0.1m;
    public const decimal DefaultCacheWriteFactor = 1.25m;

    public readonly record struct Price(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
    {
        public Price(decimal input, decimal output) : this(input, output, input * DefaultCacheReadFactor, input * DefaultCacheWriteFactor) { }
    }

    public static IReadOnlyDictionary<string, Price> Parse(string? json)
    {
        var prices = new Dictionary<string, Price>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return prices;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return prices;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                if (Number(p.Value, "input") is { } i && Number(p.Value, "output") is { } o)
                    prices[p.Name.Trim()] = new Price(i, o, Number(p.Value, "cacheRead") ?? i * DefaultCacheReadFactor,
                        Number(p.Value, "cacheWrite") ?? i * DefaultCacheWriteFactor);
            }
        }
        catch (JsonException) { }
        return prices;
    }

    public static decimal? Cost(IReadOnlyDictionary<string, Price> prices, string? model, int inputTokens, int outputTokens) =>
        Find(prices, model) is { } price ? Math.Round((inputTokens * price.Input + outputTokens * price.Output) / 1_000_000m, 6) : null;

    /// <summary>Preço do modelo pelo prefixo mais longo (<c>claude-opus-5-5[1m]</c> → <c>claude-opus-5-5</c>).</summary>
    public static Price? Find(IReadOnlyDictionary<string, Price> prices, string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || prices.Count == 0) return null;
        var m = model.Trim().ToLowerInvariant();
        var match = prices.Keys.Where(k => m.StartsWith(k.ToLowerInvariant(), StringComparison.Ordinal)).OrderByDescending(k => k.Length).FirstOrDefault();
        return match is null ? null : prices[match];
    }

    private static decimal? Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
}
