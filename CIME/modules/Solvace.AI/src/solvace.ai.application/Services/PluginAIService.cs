using solvace.ai.application.Contract;
using solvace.ai.domain.Responses;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Extensions;

namespace solvace.ai.application.Services;

/// <summary>
/// IAIService usado pela aplicação: a cada chamada resolve o provedor ("AI Configurations" →
/// Provider) e a configuração EFETIVA do plugin "{Provider} Plugin" — global, ou a do usuário
/// quando o plugin é de uso pessoal (feature 0002: cada pessoa com a própria ApiKey). Sem a
/// integração pessoal configurada → PersonalIntegrationRequiredException (403 no controller).
/// </summary>
public class PluginAIService : IAIService
{
    private const string AIConfigurationsPlugin = "AI Configurations";

    private readonly IPluginConfigurationResolver _resolver;
    private readonly AIServiceFactory _factory;
    private readonly IAIUsageRecorder? _usage;

    public PluginAIService(IPluginConfigurationResolver resolver, AIServiceFactory factory, IAIUsageRecorder? usage = null)
    {
        _resolver = resolver;
        _factory = factory;
        _usage = usage;
    }

    public async Task<AIGenerateResponse?> GenerateContentAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var aiConfiguration = await _resolver.GetEffectiveConfigurationAsync(AIConfigurationsPlugin, cancellationToken);
        var providerName = aiConfiguration.GetConfigurationValue("Provider")?.Trim();
        if (string.IsNullOrWhiteSpace(providerName))
            return new AIGenerateResponse { Error = $"Provider não configurado no plugin {AIConfigurationsPlugin}" };

        // Fora de try: a falta de integração pessoal precisa subir como 403.
        var providerConfiguration = await _resolver.GetEffectiveConfigurationAsync($"{providerName} Plugin", cancellationToken);

        IAIService provider;
        try
        {
            provider = _factory.CreateService(providerName, providerConfiguration);
        }
        catch (InvalidOperationException e)
        {
            // Ex.: provedor desconhecido ou sem ApiKey (construtores de Gemini/OpenAI validam).
            return new AIGenerateResponse { Error = e.Message, Provider = providerName };
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await provider.GenerateContentAsync(prompt, cancellationToken);
        if (result is not null && _usage is not null)
        {
            if (string.IsNullOrEmpty(result.Provider)) result.Provider = providerName;
            try { await _usage.RecordAsync(result, started.Elapsed, CancellationToken.None); }
            catch { /* o registro de consumo nunca derruba a chamada de IA */ }
        }
        return result;
    }
}
