using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using solvace.ai.application.Contract;
using solvace.ai.domain.Responses;
using solvace.prform.application;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.AiUsage;

/// <summary>
/// Registra o consumo de cada chamada de IA (0042) e soma o da requisição no cabeçalho <c>X-AI-Usage</c>, que a tela
/// mostra depois da ação ("IA: 7,3 mil tokens · ≈ US$ 0,012"). Custo pela tabela do plugin "AI Configurations"
/// (<c>AiModelPricesUsdPerMillion</c>). Grava num DbContext próprio para não salvar junto o que a requisição tiver pendente.
/// </summary>
public partial class AiUsageRecorder(IHttpContextAccessor http, IServiceScopeFactory scopes, IPluginCacheManager plugins,
    ILogger<AiUsageRecorder> logger) : IAIUsageRecorder
{
    public const string ActionHeader = "X-AI-Action";
    public const string UsageHeader = "X-AI-Usage";
    public const string PricesKey = "AiModelPricesUsdPerMillion";
    private const string AIConfigurationsPlugin = "AI Configurations";
    private const string ItemsKey = "cime-ai-usage";

    public async Task RecordAsync(AIGenerateResponse response, TimeSpan elapsed, CancellationToken cancellationToken)
    {
        var input = response.InputTokens ?? 0;
        var output = response.OutputTokens ?? (response.InputTokens is null ? response.TokensUsed ?? 0 : 0);
        // Falha antes de chegar ao provedor (sem chave, provedor inválido) não custa nada.
        if (input == 0 && output == 0 && !string.IsNullOrEmpty(response.Error)) return;

        var context = http.HttpContext;
        var cost = AiUsagePricing.Cost(await PricesAsync(cancellationToken), response.Model, input, output);
        var (userId, userName) = User(context?.User);
        var (action, route) = Action(context);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DefaultContext>();
            db.AiUsageRecords.Add(new AiUsageRecord(DateTimeOffset.UtcNow, userId, userName, action, route, response.Provider, response.Model,
                input, output, cost, (int)Math.Min(int.MaxValue, elapsed.TotalMilliseconds), string.IsNullOrEmpty(response.Error), response.Error));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Não registrei o consumo de IA ({Action})", action);
        }
        if (context is not null) Accumulate(context, input, output, cost, response.Model);
    }

    /// <summary>Preços da configuração GLOBAL do plugin (vale mesmo que ele seja pessoal — o preço não é do usuário).</summary>
    private async Task<IReadOnlyDictionary<string, AiUsagePricing.Price>> PricesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plugin = Find();
            if (plugin is null)
            {
                await plugins.RefreshPluginsAsync(cancellationToken);
                plugin = Find();
            }
            return AiUsagePricing.Parse(plugin?.Configurations?.GetConfigurationValue(PricesKey));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return AiUsagePricing.Parse(null);
        }

        Plugin? Find() => plugins.GetCachedPlugins()?
            .FirstOrDefault(p => string.Equals(p.Description, AIConfigurationsPlugin, StringComparison.OrdinalIgnoreCase));
    }

    private static (Guid? Id, string Name) User(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return (null, "anônimo");
        Guid? id = Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var g) && g != Guid.Empty ? g : null;
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value ?? user.FindFirst("unique_name")?.Value;
        return (id, string.IsNullOrWhiteSpace(name) ? "desconhecido" : name);
    }

    /// <summary>Ação: o cabeçalho X-AI-Action (o front diz qual botão) ou a rota conhecida; senão a própria rota.</summary>
    internal static (string Action, string Route) Action(HttpContext? context)
    {
        var route = (context?.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context?.Request.Path.Value ?? string.Empty;
        route = ApiPrefix().Replace(route, string.Empty);
        var header = context?.Request.Headers[ActionHeader].ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            var clean = NotAllowed().Replace(header.Trim().ToLowerInvariant(), "-");
            if (clean.Length > 0) return (clean.Length <= AiUsageRecord.MaxActionLength ? clean : clean[..AiUsageRecord.MaxActionLength], route);
        }
        return (AiUsageActions.FromRoute(route), route);
    }

    private static void Accumulate(HttpContext context, int input, int output, decimal? cost, string? model)
    {
        if (context.Items[ItemsKey] is not Totals totals)
        {
            totals = new Totals();
            context.Items[ItemsKey] = totals;
            if (!context.Response.HasStarted)
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[UsageHeader] = totals.ToHeader();
                    return Task.CompletedTask;
                });
        }
        totals.Calls++;
        totals.Input += input;
        totals.Output += output;
        if (cost is { } c) totals.Cost += c; else totals.Unpriced++;
        if (!string.IsNullOrWhiteSpace(model)) totals.Model = model;
    }

    private sealed class Totals
    {
        public int Calls, Input, Output, Unpriced;
        public decimal Cost;
        public string? Model;
        public string ToHeader() => string.Join(';',
            $"calls={Calls}", $"in={Input}", $"out={Output}",
            $"cost={(Unpriced == Calls ? "" : Cost.ToString("0.######", CultureInfo.InvariantCulture))}",
            $"model={NotAllowedModel().Replace(Model ?? string.Empty, "")}");
    }

    [GeneratedRegex(@"^/?api/v\{?[^/]*\}?/", RegexOptions.IgnoreCase)] private static partial Regex ApiPrefix();
    [GeneratedRegex(@"[^a-z0-9:._-]+")] private static partial Regex NotAllowed();
    [GeneratedRegex(@"[^A-Za-z0-9._:-]")] private static partial Regex NotAllowedModel();
}
