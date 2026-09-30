namespace solvace.prform.Knowledge;

/// <summary>
/// Plugin "Knowledge Center Configurations" (feature 0033), semeado pela migração SeedKnowledgeCenterConfigurations.
/// Trocar o KC de DEV para produção = mudar <see cref="Environment"/> (a credencial de cada ambiente fica só na
/// máquina de quem sincroniza, em ~/.claude/knowledgecenter-credentials.json). O filtro de dados de teste tem um piso
/// fixo no código — aqui o administrador só ACRESCENTA regras.
/// </summary>
public static class KnowledgeCenterConfigurationKeys
{
    public const string PluginName = "Knowledge Center Configurations";

    /// <summary>dev | prod.</summary>
    public const string Environment = "Environment";
    public const string DevHost = "DevHost";
    public const string DevDatabase = "DevDatabase";
    public const string DevSchema = "DevSchema";
    public const string ProdHost = "ProdHost";
    public const string ProdDatabase = "ProdDatabase";
    public const string ProdSchema = "ProdSchema";
    /// <summary>JSON: padrões extras (regex) de teste — somados ao piso.</summary>
    public const string ExcludePatterns = "ExcludePatterns";
    /// <summary>JSON: artigos sempre fora (12 ou "ART-12").</summary>
    public const string ExcludeArticles = "ExcludeArticles";
    /// <summary>JSON: artigos liberados explicitamente (passam pelos padrões, nunca por status/remoção).</summary>
    public const string AllowArticles = "AllowArticles";
    /// <summary>Texto mínimo (só vale acima do piso de 200).</summary>
    public const string MinTextLength = "MinTextLength";
    /// <summary>A cada quantas horas a skill faz a carga completa (pega renomes de categoria/remoções).</summary>
    public const string FullSyncHours = "FullSyncHours";
}
