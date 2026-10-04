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
    /// <summary>0049: a correção abre uma sessão nova do Claude (só o resumo da análise) em vez de retomar a da análise.</summary>
    public const string ExecutorCorrectionNewSession = "ExecutorCorrectionNewSession";
    /// <summary>0049: segundos de espera depois de um comentário antes de retomar o card (0 = na hora).</summary>
    public const string ExecutorNoteDelaySeconds = "ExecutorNoteDelaySeconds";
    /// <summary>0052: papéis que aprovam e publicam a engenharia reversa ("admin,gestor").</summary>
    public const string ReverseEngineeringApproverRoles = "ReverseEngineeringApproverRoles";
    /// <summary>0052: documentos exigidos para o módulo contar como completo.</summary>
    public const string ReverseEngineeringRequiredDocs = "ReverseEngineeringRequiredDocs";
    /// <summary>0052: etapa da analisar-bug que só conclui consultando/citando a engenharia reversa (vazio desliga).</summary>
    public const string ReverseEngineeringGateStep = "ReverseEngineeringGateStep";
    /// <summary>0052: cobertura mínima do inventário do código (0–1) — abaixo disso o revisor vê o aviso.</summary>
    public const string ReverseEngineeringMinCoverage = "ReverseEngineeringMinCoverage";
    /// <summary>0052: modelos que substituem os do código ({"funcional": "markdown"}; {} = os do código).</summary>
    public const string ReverseEngineeringTemplates = "ReverseEngineeringTemplates";
}
