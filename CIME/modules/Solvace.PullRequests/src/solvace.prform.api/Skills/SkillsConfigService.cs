using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.azure.application.Contract;
using solvace.azure.domain.Options;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.Knowledge;

namespace solvace.prform.Skills;

/// <summary>Prompts do "AI Configurations" que as skills usam (null = não configurado).</summary>
public record SkillsConfigPrompts(string? Bug, string? UserStory, string? Summary);

/// <summary>Configuração efetiva das skills — o mesmo conteúdo de <c>GET Skills/config</c> e da ferramenta MCP <c>prmake_config</c> (0041).</summary>
public record SkillsConfigResult(bool Available, JsonObject Settings, object Knowledge, SkillsConfigPrompts Prompts, AzureDevOpsFieldNames? Fields);

/// <summary>Monta a configuração das skills para o usuário da requisição (REST e MCP usam a mesma).</summary>
public class SkillsConfigService(IPluginConfigurationResolver configurationResolver, IAzureService azureService)
{
    /// <summary>
    /// Configuração que as skills seguem (feature 0030), efetiva para o usuário: "Skills Configurations"
    /// (fluxo de branches, padrões), prompts do "AI Configurations" e nomes dos campos do DevOps. Valores
    /// JSON voltam como objeto; o resto como texto. Parte indisponível vem vazia (a skill avisa).
    /// </summary>
    public async Task<SkillsConfigResult> BuildAsync(CancellationToken cancellationToken)
    {
        var settings = new JsonObject();
        var skills = await TryConfigAsync(SkillsConfigurationKeys.PluginName, cancellationToken);
        foreach (var (key, value) in (string.IsNullOrWhiteSpace(skills?.Options) ? null : skills.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>())
            settings[key] = ParseValue(value);

        var ai = await TryConfigAsync(AIConfigurationKeys.PluginName, cancellationToken);
        string? Prompt(string key) => ai?.GetConfigurationValueOrDefault(key, string.Empty) is { Length: > 0 } v ? v : null;

        AzureDevOpsFieldNames? fields = null;
        try { fields = await azureService.GetFieldNamesAsync(cancellationToken); }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException) { }

        // Knowledge Center (0033): ambiente ativo, onde fica cada ambiente e as regras EXTRAS do filtro; o piso fixo vai
        // junto para a skill conferir que aplica a mesma regra.
        var kc = await TryConfigAsync(KnowledgeCenterConfigurationKeys.PluginName, cancellationToken);
        var kcValues = (string.IsNullOrWhiteSpace(kc?.Options) ? null : kc.Options.JsonToListOfDictionaries().FirstOrDefault()) ?? new Dictionary<string, string>();
        string Kc(string key, string fallback = "") => kcValues.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
        var knowledge = new
        {
            available = kc is not null,
            environment = Kc(KnowledgeCenterConfigurationKeys.Environment, "dev").ToLowerInvariant(),
            dev = new { host = Kc(KnowledgeCenterConfigurationKeys.DevHost), database = Kc(KnowledgeCenterConfigurationKeys.DevDatabase), schema = Kc(KnowledgeCenterConfigurationKeys.DevSchema) },
            prod = new { host = Kc(KnowledgeCenterConfigurationKeys.ProdHost), database = Kc(KnowledgeCenterConfigurationKeys.ProdDatabase), schema = Kc(KnowledgeCenterConfigurationKeys.ProdSchema) },
            filter = new
            {
                excludePatterns = ParseValue(Kc(KnowledgeCenterConfigurationKeys.ExcludePatterns, "[]")),
                excludeArticles = ParseValue(Kc(KnowledgeCenterConfigurationKeys.ExcludeArticles, "[]")),
                allowArticles = ParseValue(Kc(KnowledgeCenterConfigurationKeys.AllowArticles, "[]")),
                minTextLength = int.TryParse(Kc(KnowledgeCenterConfigurationKeys.MinTextLength), out var min) ? min : 0,
                floor = new
                {
                    patterns = solvace.knowledge.domain.Filtering.KnowledgeNoiseFilter.FloorPatterns,
                    minTextLength = solvace.knowledge.domain.Filtering.KnowledgeNoiseFilter.FloorMinTextLength,
                    publishedStatusId = solvace.knowledge.domain.Filtering.KnowledgeNoiseFilter.PublishedStatusId
                }
            },
            fullSyncHours = int.TryParse(Kc(KnowledgeCenterConfigurationKeys.FullSyncHours), out var hours) && hours > 0 ? hours : 24
        };

        return new SkillsConfigResult(skills is not null, settings, knowledge,
            new SkillsConfigPrompts(Prompt("PromptBug"), Prompt("PromptUS"), Prompt(AIConfigurationKeys.BugSummaryPrompt)), fields);
    }

    private async Task<PluginConfiguration?> TryConfigAsync(string plugin, CancellationToken cancellationToken)
    {
        try { return await configurationResolver.GetEffectiveConfigurationAsync(plugin, cancellationToken); }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException) { return null; }
    }

    private static JsonNode? ParseValue(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return JsonNode.Parse(trimmed); }
            catch (JsonException) { }
        }
        return JsonValue.Create(value ?? string.Empty);
    }
}
