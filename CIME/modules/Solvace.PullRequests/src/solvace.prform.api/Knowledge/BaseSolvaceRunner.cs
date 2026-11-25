using solvace.prform.application.UserIntegrations;

namespace solvace.prform.Knowledge;

/// <summary>
/// 0102: quem responde as perguntas da Base Solvace (Pergunte e "Analisar a fundo") — chave pessoal <c>BaseSolvaceRunner</c> do
/// "AI Configurations" em "Minhas integrações": o provedor de IA do servidor (a chave de API da pessoa) ou o executor (o Claude
/// Code da máquina dela, pela assinatura). Sem a chave (ou sem a integração preenchida), o provedor — como sempre foi.
/// </summary>
public static class BaseSolvaceRunner
{
    public const string Key = "BaseSolvaceRunner";
    public const string Provider = "provider";
    public const string Executor = "executor";

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "executor" or "claude code" or "claude-code" or "maquina" or "máquina" => Executor,
            _ => Provider
        };

    public static async Task<string> ResolveAsync(IPluginConfigurationResolver settings, CancellationToken ct)
    {
        try
        {
            var config = await settings.GetEffectiveConfigurationAsync(solvace.azure.domain.Options.AIConfigurationKeys.PluginName, ct);
            return Normalize(solvace.prform.domain.Extensions.PluginConfigurationExtensions.GetConfigurationValueOrDefault(config, Key, Provider));
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            return Provider;
        }
    }
}
