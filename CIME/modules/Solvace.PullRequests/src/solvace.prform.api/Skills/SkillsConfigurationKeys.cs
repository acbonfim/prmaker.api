namespace solvace.prform.Skills;

/// <summary>
/// Plugin "Skills Configurations" (feature 0030): regras que as skills do Claude seguem e que mudam sem
/// deploy — fluxo de branches por área, estratégia por tipo de repositório, padrões de nome/commit/título.
/// Semeado pela migração SeedSkillsConfigurations; o admin edita na tela de plugins.
/// </summary>
public static class SkillsConfigurationKeys
{
    public const string PluginName = "Skills Configurations";

    public const string BranchFlowByArea = "BranchFlowByArea";
    public const string BranchStrategy = "BranchStrategy";
    public const string BranchNamePattern = "BranchNamePattern";
    public const string CommitMessagePattern = "CommitMessagePattern";
    public const string PrTitlePattern = "PrTitlePattern";
    public const string DefaultRepository = "DefaultRepository";
    public const string TicketSystem = "TicketSystem";
    /// <summary>0047: modelo do Claude Code na análise pelo executor (apelido = sempre a versão mais nova; vazio = padrão da máquina).</summary>
    public const string ExecutorAnalysisModel = "ExecutorAnalysisModel";
    /// <summary>0047: modelo do Claude Code na correção pelo executor.</summary>
    public const string ExecutorCorrectionModel = "ExecutorCorrectionModel";
}
