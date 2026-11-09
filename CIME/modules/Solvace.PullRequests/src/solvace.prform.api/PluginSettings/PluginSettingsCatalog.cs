using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.ai.domain.Options;
using solvace.azure.application.Services;
using solvace.azure.domain.Options;
using solvace.executionplans.application.Contracts;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;
using solvace.prform.application.Security;
using solvace.prform.application.Teams;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Enums;
using solvace.prform.Execution;
using solvace.prform.Knowledge;
using solvace.prform.Promotion;
using solvace.prform.Skills;

namespace solvace.prform.PluginSettings;

/// <summary>
/// 0086: catálogo das chaves de plugin — fonte única do "o que é cada chave": plugin onde fica, seção, grupo, rótulo,
/// ajuda, tipo de editor, padrão (o MESMO de reserva que o código usa), opções, variáveis, quem lê e se está obsoleta.
/// Só descreve: os valores continuam onde estão (mesmo plugin, mesma chave, mesmo formato). Chave nova no código =
/// entrada nova aqui no mesmo PR (os testes conferem o formato).
/// </summary>
public static class PluginSettingsCatalog
{
    // ------------------------------------------------------------------ plugins
    public static readonly SettingPluginRef Skills = new(SkillsConfigurationKeys.PluginName);
    public static readonly SettingPluginRef Ai = new(AIConfigurationKeys.PluginName);
    public static readonly SettingPluginRef DevOps = new("AzureDevOps Configurations");            // AzureService.PluginName
    public static readonly SettingPluginRef Knowledge = new(KnowledgeCenterConfigurationKeys.PluginName);
    public static readonly SettingPluginRef GitHub = new("Github Configurations");                 // GitHubService.PluginName
    public static readonly SettingPluginRef Teams = new(TeamsApprovalService.PluginName);
    public static readonly SettingPluginRef RealTime = new("Realtime Configurations");             // PluginRealTimeOptionsProvider
    public static readonly SettingPluginRef Claude = new("Claude Plugin");                         // "{Provider} Plugin" (PluginAIService)
    public static readonly SettingPluginRef Gemini = new("Gemini Plugin");
    public static readonly SettingPluginRef OpenAI = new("OpenAI Plugin");
    public static readonly SettingPluginRef Copilot = new("Copilot Plugin");
    /// <summary>Plugin 9 — o front acha pelo id (repositórios/branches da tela de PR).</summary>
    public static readonly SettingPluginRef PullRequest = new("PullRequest", EPlugin.PULLREQUEST);
    /// <summary>Plugin 11 — o ImageController acha pelo id (Cloudinary).</summary>
    public static readonly SettingPluginRef Bucket = new("Cloudinary", EPlugin.BUCKET_IMAGE);

    // Antes das listas abaixo: inicializadores estáticos rodam na ordem do arquivo e os padrões usam estas opções.
    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions CompactCamelJson = new(CompactJson) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions IndentedJson = new(CompactJson) { WriteIndented = true };

    // ------------------------------------------------------------------ seções
    public static IReadOnlyList<SettingSectionDef> Sections { get; } =
    [
        new("devops", "Azure DevOps", "view_kanban", "Conexão com o Azure DevOps, campos do card, estados para onde o PRMake move o card e opções de classificação.",
        [
            new("conexao", "Conexão", "Organização, projeto e o token de acesso (cada pessoa usa o próprio token)."),
            new("campos", "Campos do card", "Nomes dos campos do work item que o PRMake lê e grava. Só mude se o processo do DevOps mudar."),
            new("estados", "Estados e movimentos", "Para onde o card vai em cada ação (Ready for QA, Dev Test in QA, Test in Production) e o que acontece sozinho."),
            new("classificacao", "Classificação", "Opções de classificação oferecidas ao fechar o card.")
        ]),
        new("branches", "Branches e PRs", "account_tree", "Nomes de branch, commit e título do PR, fluxo por área e repositório, a tela de PR e a promoção em lote.",
        [
            new("padroes", "Padrões de nome", "Como as skills e o PRMake montam nomes de branch, commit e título de PR."),
            new("fluxo", "Fluxo por área e repositório", "Qual fluxo (produção ou release) vale para o card e quais PRs cada tipo de repositório recebe."),
            new("tela-pr", "Tela de PR", "Repositórios e branches de destino oferecidos na tela de PR."),
            new("promocao", "Promoção em lote", "Destinos e nomes de branch da promoção de PRs para release/hotfix.")
        ]),
        new("analise", "Análise e correção", "fact_check", "Etapas padrão dos planos, travas que impedem concluir uma etapa sem a prova certa e os testes locais da correção.",
        [
            new("etapas", "Etapas padrão", "Etapas com que o plano de análise nasce (bug e User Story)."),
            new("travas", "Travas", "Etapas que só concluem com a Base Solvace consultada, o código citado fotografado ou as evidências abertas."),
            new("testes", "Testes locais", "Como a correção roda os testes antes de abrir o PR.")
        ]),
        new("executor", "Executor e modelos", "smart_toy", "Modelos do Claude Code usados pelo executor (prmake-agent) e como ele executa.",
        [
            new("modelos", "Modelos", "Apelido (opus, sonnet, haiku = sempre a versão mais nova) ou id do modelo; vazio = o padrão da máquina."),
            new("execucao", "Execução", "Sessões e execuções simultâneas.")
        ]),
        new("itens", "Itens de trabalho", "checklist", "Tipos de sub-tarefa e modelos prontos dos itens de trabalho (Bancada do card).",
        [
            new("tipos", "Tipos de item e de sub-tarefa", "Os tipos de item, o que cada sub-tarefa faz e quem executa."),
            new("modelos", "Modelos de item", "Itens prontos para criar com um clique."),
            new("validacao", "Validação em ambiente", "Ambientes onde o Claude valida a demanda com prints (URL, usuário padrão, banco, somente leitura).")
        ]),
        new("assistente", "Assistente e horas", "schedule", "Sugestões do assistente do card (estimativa inicial, lançar horas, publicar resumos) e as médias de horas do time.",
        [
            new("assistente", "Assistente do card", "Liga o assistente e define quando ele sugere lançar horas."),
            new("estimativa", "Estimativa inicial", "Horas gravadas no card quando a estimativa inicial é aplicada.")
        ]),
        new("ia", "IA do servidor", "psychology", "Qual provedor de IA o PRMake usa nas ações do servidor e a configuração de cada um. Cada pessoa usa a própria chave.",
        [
            new("provedor", "Provedor", "O provedor ativo."),
            new("claude", "Claude (Anthropic)", "Configuração do provedor Claude."),
            new("gemini", "Gemini (Google)", "Configuração do provedor Gemini."),
            new("openai", "OpenAI", "Configuração do provedor OpenAI."),
            new("copilot", "Copilot", "Configuração do provedor Copilot."),
            new("custos", "Custos", "Preços usados para estimar o custo de cada ação de IA.")
        ]),
        new("prompts", "Prompts e modelos de texto", "edit_note", "Textos que a IA segue para gerar PR, resumo e passagem de conhecimento, e as mensagens do Teams.",
        [
            new("pr", "Pull Request", "Prompts da descrição do PR e do RCA."),
            new("devops", "Resumo no DevOps", "Resumo não técnico postado na discussion do card."),
            new("handover", "Passagem de conhecimento", "Modelo do formulário de handover."),
            new("provedor", "Instruções do provedor", "Instruções fixas enviadas em toda chamada de IA (system prompt)."),
            new("teams", "Mensagem do Teams", "Título e corpo do pedido de aprovação de PR no Teams.")
        ]),
        new("engenharia-reversa", "Engenharia reversa", "manage_search", "Aprovação, cobertura, geração e fontes da engenharia reversa (Base Solvace).",
        [
            new("aprovacao", "Aprovação e cobertura", "Quem aprova, o que é exigido para o módulo contar como completo e o que a engenharia reversa substitui."),
            new("geracao", "Geração", "Como a skill gera os documentos."),
            new("fontes", "Fontes", "Banco de referência, traduções, infraestrutura e palavras ignoradas no glossário.")
        ]),
        new("knowledge", "Knowledge Center", "menu_book", "De onde as skills leem as regras de negócio do Knowledge Center e o filtro dos artigos.",
        [
            new("ambiente", "Ambiente", "Qual ambiente do Knowledge Center vale e onde fica cada um."),
            new("filtro", "Filtro", "Regras extras para deixar artigos de teste de fora (somadas ao filtro fixo do código)."),
            new("sync", "Sincronização", "Quando a skill faz a carga completa.")
        ]),
        new("integracoes", "Integrações", "hub", "GitHub, Teams, tempo real e o serviço de imagens.",
        [
            new("github", "GitHub", "Organização, repositório e token do GitHub."),
            new("teams", "Teams", "Pedido de aprovação de PR num grupo do Teams."),
            new("tempo-real", "Tempo real", "Origens aceitas pelo canal de tempo real."),
            new("imagens", "Imagens", "Credenciais do Cloudinary (fotos de perfil).")
        ])
    ];

    // ------------------------------------------------------------------ chaves
    private static readonly string[] Api = [SettingReaders.Api];
    private static readonly string[] Sk = [SettingReaders.Skills];
    private static readonly string[] Fe = [SettingReaders.Front];
    private static readonly string[] ApiSk = [SettingReaders.Api, SettingReaders.Skills];
    private static readonly string[] ApiFe = [SettingReaders.Api, SettingReaders.Front];
    private static readonly string[] ApiEx = [SettingReaders.Api, SettingReaders.Executor];
    private static readonly string[] ApiSkFe = [SettingReaders.Api, SettingReaders.Skills, SettingReaders.Front];
    private static readonly string[] FeSk = [SettingReaders.Front, SettingReaders.Skills];
    private static readonly string[] ApiSkEx = [SettingReaders.Api, SettingReaders.Skills, SettingReaders.Executor];

    private static readonly string[] AnalysisStepSources =
    [
        $"{SkillsConfigurationKeys.PluginName}:{SkillsConfigurationKeys.AnalysisDefaultSteps}",
        $"{SkillsConfigurationKeys.PluginName}:{SkillsConfigurationKeys.UsAnalysisDefaultSteps}"
    ];

    private static readonly SettingOption[] ClaudeCodeModels =
    [
        new("opus", "Opus (o mais capaz)"),
        new("sonnet", "Sonnet (equilíbrio)"),
        new("haiku", "Haiku (o mais rápido)")
    ];

    private static readonly SettingColumn[] StepColumns =
    [
        new("key", "Chave", SettingTypes.Text, Required: true, Width: "sm"),
        new("title", "Título", SettingTypes.Text, Required: true, Width: "md"),
        new("description", "Descrição", SettingTypes.Textarea, Width: "lg"),
        new("kind", "Tipo", SettingTypes.Enum, Options: [new("", "Etapa normal"), new("question", "Pergunta ao usuário")], Width: "sm",
            AllowCustom: true)
    ];

    private static readonly SettingVariable[] TeamsVariables =
    [
        new("cardNumber", "número do card", "77434"),
        new("prNumber", "número do PR no GitHub", "1523"),
        new("prTitle", "título do PR", "AB#77434 QA"),
        new("prUrl", "link do PR", "https://github.com/solvace/edv-solvace/pull/1523"),
        new("repository", "repositório", "edv-solvace"),
        new("branch", "branch do PR", "hotfix/77434-qa"),
        new("targetBranch", "branch de destino", "qa"),
        new("author", "quem pediu a aprovação", "Maria Silva"),
        new("status", "situação do PR", "open")
    ];

    public static IReadOnlyList<SettingDef> Fields { get; } = Build();

    private static List<SettingDef> Build() =>
    [
        // ============================================================== Azure DevOps — Conexão
        new() { Section = "devops", Group = "conexao", Plugin = DevOps, Key = "Organization", Type = SettingTypes.Text, ReadBy = ApiFe,
            Label = "Organização", Help = "Nome da organização no Azure DevOps (o que vem depois de dev.azure.com/)." },
        new() { Section = "devops", Group = "conexao", Plugin = DevOps, Key = "Project", Type = SettingTypes.Text, ReadBy = ApiFe,
            Label = "Projeto", Help = "Projeto do Azure DevOps onde ficam os cards." },
        new() { Section = "devops", Group = "conexao", Plugin = DevOps, Key = "ApiVersion", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Versão da API", Help = "Versão da API REST do Azure DevOps (ex.: 7.1). Só mude com orientação técnica.",
            Options = [new("7.1", "7.1")], AllowCustom = true },
        new() { Section = "devops", Group = "conexao", Plugin = DevOps, Key = "PersonalAccessToken", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "Personal Access Token (PAT)",
            Help = "Token do Azure DevOps (User settings → Personal access tokens, escopo Work Items: Read & write). Em uso pessoal, cada pessoa preenche o seu em Minhas integrações." },

        // ============================================================== Azure DevOps — Campos do card
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.ResolutionType, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: Resolution Type", Help = "Nome de referência do campo no work item.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.ResolutionType] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.GeneralClassification, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: General Classification", Help = "Nome de referência do campo no work item.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.GeneralClassification] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.Classification, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: Classification", Help = "Nome de referência do campo no work item.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.Classification] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.RemainingWork, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: Remaining Work", Help = "Horas restantes do card.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.RemainingWork] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.OriginalEstimate, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: Original Estimate", Help = "Estimativa original do card.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.OriginalEstimate] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.CompletedWork, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo: Completed Work", Help = "Horas já lançadas no card.", Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.CompletedWork] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = AzureDevOpsFieldKeys.RootCauseFieldPath, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Campo do Root Cause (caminho)", Help = "Onde o RCA é gravado no card, no formato /fields/<campo>.",
            Default = AzureDevOpsFieldKeys.Defaults[AzureDevOpsFieldKeys.RootCauseFieldPath] },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = "TitleFieldName", Type = SettingTypes.Text, ReadBy = Fe,
            Label = "Campo do título", Help = "Campo do work item com o título do card (lido pela tela de PR). Vazio = o título padrão do DevOps." },
        new() { Section = "devops", Group = "campos", Plugin = DevOps, Key = "RetroStepsFieldName", Type = SettingTypes.Text, ReadBy = Fe,
            Label = "Campo dos passos para reproduzir", Help = "Campo do work item com os Repro Steps (lido pela tela de PR).",
            Default = "Microsoft.VSTS.TCM.ReproSteps" },

        // ============================================================== Azure DevOps — Estados e movimentos
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugReadyForQaState, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Ready for QA: estado do card", Help = "Estado para onde o bug vai ao ser liberado para o QA (ex.: In Development (done)). Vazio = a ação fica indisponível." },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugDevTestInQaState, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Dev Test in QA: estado do card", Help = "Estado do card enquanto o dev valida a correção em QA (ex.: In Development (doing))." },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugDevTestInQaColumn, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Dev Test in QA: coluna do board", Help = "Coluna do board junto com o estado (ex.: Dev Test in QA). Vazio = só muda o estado." },
        new() { Section = "devops", Group = "estados", Plugin = Skills, Key = SkillsConfigurationKeys.AutoDevTestInQa, Type = SettingTypes.Bool, ReadBy = Api,
            Label = "Mover para Dev Test in QA sozinho", Help = "Quando o PR da branch de QA é mesclado, o card vai sozinho para Dev Test in QA.",
            Default = Bool(ExecutionQaSettings.Default.AutoDevTestInQa) },
        new() { Section = "devops", Group = "estados", Plugin = Skills, Key = SkillsConfigurationKeys.QaBranchPattern, Type = SettingTypes.List, ListFormat = "csv", ReadBy = Api,
            Label = "Branches de QA", Help = "Branches de destino que contam como QA. Termine com * para valer como prefixo (qa* = qa, qa2, qa-edge…).",
            Default = string.Join(",", ExecutionQaSettings.Default.QaBranches) },
        new() { Section = "devops", Group = "estados", Plugin = Skills, Key = SkillsConfigurationKeys.SituationPostToDevOps, Type = SettingTypes.Bool, ReadBy = Api,
            Label = "Publicar a situação do card no DevOps", Help = "O resumo da situação (chamado, PR, QA) vai sozinho para a discussion do card, sempre no mesmo comentário. Cada pessoa pode desligar para si.",
            Default = Bool(ExecutionQaSettings.Default.PostSituationToDevOps) },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugTestInProductionRequiredArea, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Test in Production: área exigida", Help = "O card só pode ir para Test in Production se estiver nesta área. Vazio = qualquer área." },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugTestInProductionArea, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Test in Production: área de destino", Help = "Área para onde o card vai. Área ou estado vazios = a ação fica indisponível." },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugTestInProductionState, Type = SettingTypes.Text, ReadBy = Api,
            Label = "Test in Production: estado de destino", Help = "Estado para onde o card vai." },
        new() { Section = "devops", Group = "estados", Plugin = Ai, Key = AIConfigurationKeys.BugTestInProductionComment, Type = SettingTypes.Textarea, ReadBy = Api,
            Label = "Test in Production: comentário", Help = "Comentário postado no card ao mover. Vazio = sem comentário." },

        // ============================================================== Azure DevOps — Classificação
        new() { Section = "devops", Group = "classificacao", Plugin = Ai, Key = AIConfigurationKeys.BugClassificationPresets, Type = SettingTypes.Table, ReadBy = Api,
            Label = "Opções de classificação",
            Help = "Combinações oferecidas ao classificar o card. Resolution Type, General Classification e Classification precisam ser valores que existem no DevOps; o padrão (A–H) é o da skill analisar-bug. Vazio = as opções do código.",
            Columns =
            [
                new("key", "Chave", SettingTypes.Text, Required: true, Width: "sm"),
                new("label", "Rótulo", SettingTypes.Text, Width: "md"),
                new("resolutionType", "Resolution Type", SettingTypes.Text, Required: true, Width: "md"),
                new("generalClassification", "General Classification", SettingTypes.Text, Required: true, Width: "md"),
                new("classification", "Classification", SettingTypes.Text, Required: true, Width: "lg"),
                new("pattern", "Padrão", SettingTypes.Text, Width: "sm")
            ],
            Default = Compact(DevOpsActionsService.DefaultClassificationPresets, camelCase: true) },

        // ============================================================== Branches e PRs — Padrões de nome
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.BranchNamePattern, Type = SettingTypes.Pattern, ReadBy = ApiSk,
            Label = "Nome da branch de correção (Bug)", Help = "Branch principal de um card do tipo Bug.", Default = "hotfix/{card}",
            Variables = [new("card", "número do card", "77434")] },
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.BranchNamePatternUserStory, Type = SettingTypes.Pattern, ReadBy = ApiSk,
            Label = "Nome da branch da User Story", Help = "Branch principal de um card do tipo User Story (feature/, nunca hotfix/).", Default = "feature/{card}",
            Variables = [new("card", "número do card", "77988")] },
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.BranchSuffixByTarget, Type = SettingTypes.Map, ReadBy = ApiSk,
            Label = "Sufixo da branch por destino",
            Help = "Branch de trabalho de cada destino = branch principal + sufixo (release-version → -rv = hotfix/77434-rv). Destino sem sufixo vira -<destino>.",
            MapKeyLabel = "Destino", MapValueLabel = "Sufixo", MapValueType = SettingTypes.Text, Default = Compact(BranchRules.DefaultSuffixesJson) },
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.CommitMessagePattern, Type = SettingTypes.Pattern, ReadBy = ApiSk,
            Label = "Mensagem de commit", Default = "AB#{card} {summary}",
            Variables = [new("card", "número do card", "77434"), new("summary", "resumo da alteração", "corrige o filtro de datas")] },
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.PrTitlePattern, Type = SettingTypes.Pattern, ReadBy = ApiSk,
            Label = "Título do PR", Default = "AB#{card} {TARGET}",
            Variables = [new("card", "número do card", "77434"), new("TARGET", "destino em maiúsculas", "QA"), new("target", "destino como está", "qa")] },
        new() { Section = "branches", Group = "padroes", Plugin = Skills, Key = SkillsConfigurationKeys.DefaultRepository, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "Repositório padrão", Help = "Usado quando a pasta não tem git remote (ex.: edv-solvace). Vazio = a skill pede o repositório." },

        // ============================================================== Branches e PRs — Fluxo
        new() { Section = "branches", Group = "fluxo", Plugin = Skills, Key = SkillsConfigurationKeys.BranchFlowByArea, Type = SettingTypes.Table, ReadBy = ApiSk,
            Label = "Fluxo pela área do card",
            Help = "A primeira linha cuja área está contida na área do card decide o fluxo. Nenhuma casou = a skill pergunta.",
            Columns =
            [
                new("areaContains", "Área contém", SettingTypes.Text, Required: true, Width: "lg"),
                new("flow", "Fluxo", SettingTypes.Enum, Required: true, Options: [new("producao", "Produção"), new("release", "Release")], Width: "sm",
                    AllowCustom: true) // fluxo novo = chave nova em BranchStrategy.flows
            ] },
        new() { Section = "branches", Group = "fluxo", Plugin = Skills, Key = SkillsConfigurationKeys.BranchStrategy, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSkEx,
            Label = "Estratégia de branches por tipo de repositório",
            Help = "repositories: [{match, kind}] diz o tipo de cada repositório; flows: {fluxo: {tipo: {base | baseOptions, askBase, prs: [{suffix, from?, target}]}}} diz de onde a branch sai e para onde vão os PRs. {base} = a base escolhida.",
            MultilineHint = true },

        // ============================================================== Branches e PRs — Tela de PR (plugin 9)
        new() { Section = "branches", Group = "tela-pr", Plugin = PullRequest, Key = "ActiveRepositories", Type = SettingTypes.Table, ReadBy = Fe,
            Label = "Repositórios da tela de PR", Help = "Repositórios oferecidos ao registrar um PR.",
            Columns = [new("label", "Rótulo", SettingTypes.Text, Required: true, Width: "md"), new("value", "Repositório", SettingTypes.Text, Required: true, Width: "md")] },
        new() { Section = "branches", Group = "tela-pr", Plugin = PullRequest, Key = "ActiveBranchs", Type = SettingTypes.Table, ReadBy = Fe,
            Label = "Branches de destino da tela de PR", Help = "Destinos oferecidos ao registrar um PR.",
            Columns = [new("label", "Rótulo", SettingTypes.Text, Required: true, Width: "md"), new("branchName", "Branch", SettingTypes.Text, Required: true, Width: "md")] },

        // ============================================================== Branches e PRs — Promoção em lote
        new() { Section = "branches", Group = "promocao", Plugin = Skills, Key = SkillsConfigurationKeys.PromotionProfiles, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = Api,
            Label = "Destinos da promoção por tipo de repositório",
            Help = "{profiles: {tipo: {label, targets: [{branch, draft?, mode?}]}}}. {version} na branch = a versão do lote.",
            MultilineHint = true },
        new() { Section = "branches", Group = "promocao", Plugin = Skills, Key = SkillsConfigurationKeys.PromotionBranchPattern, Type = SettingTypes.Pattern, ReadBy = Api,
            Label = "Nome da branch do cherry-pick", Default = "{branch}-{target}",
            Variables = [new("branch", "branch principal do card", "hotfix/77434"), new("suffix", "sufixo do destino (Sufixo da branch por destino)", "-rv"),
                new("target", "destino", "release-version")] },
        new() { Section = "branches", Group = "promocao", Plugin = Ai, Key = PromotionParser.PromptKey, Type = SettingTypes.Prompt, ReadBy = Api, Advanced = true,
            Label = "Prompt da leitura da mensagem do Release Manager",
            Help = "Como a IA lê a mensagem colada na promoção em lote. Vazio = o prompt do código.",
            Default = PromotionParser.DefaultPrompt,
            Variables = [new("kinds", "tipos de repositório (chave — rótulo)", "- legacy — Legado"), new("text", "mensagem colada", "Branch para ticket Netcore: hotfix-version")] },

        // ============================================================== Análise e correção — Etapas padrão
        new() { Section = "analise", Group = "etapas", Plugin = Skills, Key = SkillsConfigurationKeys.AnalysisDefaultSteps, Type = SettingTypes.Table, ReadBy = Sk,
            Label = "Etapas da análise de bug", Help = "Etapas com que o plano da analisar-bug nasce. Tipo \"pergunta\" = a etapa espera a resposta do usuário. Vazio = as etapas da skill.",
            Columns = StepColumns },
        new() { Section = "analise", Group = "etapas", Plugin = Skills, Key = SkillsConfigurationKeys.UsAnalysisDefaultSteps, Type = SettingTypes.Table, ReadBy = Sk,
            Label = "Etapas da análise de User Story", Help = "Etapas com que o plano da analisar-us nasce. Vazio = as etapas da skill.",
            Columns = StepColumns },
        new() { Section = "analise", Group = "etapas", Plugin = Skills, Key = SkillsConfigurationKeys.TicketSystem, Type = SettingTypes.Text, ReadBy = Sk,
            Label = "Sistema de chamados", Help = "Nome do sistema onde os chamados de script são abertos (ex.: Freshservice), citado no texto das etapas." },

        // ============================================================== Análise e correção — Travas
        new() { Section = "analise", Group = "travas", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringGateStep, Type = SettingTypes.Steps, ListFormat = "csv",
            StepSources = AnalysisStepSources, ReadBy = Api,
            Label = "Etapas que exigem a Base Solvace",
            Help = "Só concluem consultando/citando a engenharia reversa quando o módulo está completo. Vazio desliga.",
            Default = ReverseSettings.Default.GateStep },
        new() { Section = "analise", Group = "travas", Plugin = Skills, Key = SkillsConfigurationKeys.CodeRefGateSteps, Type = SettingTypes.Steps, ListFormat = "csv",
            StepSources = AnalysisStepSources, ReadBy = Api,
            Label = "Etapas que exigem o código fotografado",
            Help = "Só concluem com toda citação caminho:linha fotografada (coderef). Vazio desliga.",
            Default = string.Join(",", CodeCitationGate.DefaultSteps) },
        new() { Section = "analise", Group = "travas", Plugin = Skills, Key = SkillsConfigurationKeys.EvidenceGateSteps, Type = SettingTypes.Steps, ListFormat = "csv",
            StepSources = AnalysisStepSources, ReadBy = Api,
            Label = "Etapas que exigem as evidências do card",
            Help = "Só concluem com os anexos e prints do card no DevOps importados e abertos. Vazio desliga.",
            Default = string.Join(",", EvidenceGate.DefaultSteps) },
        new() { Section = "analise", Group = "travas", Plugin = Skills, Key = SkillsConfigurationKeys.CodeRefContextLines, Type = SettingTypes.Number, ReadBy = Sk, Advanced = true,
            Label = "Linhas de contexto do código citado", Help = "Linhas antes e depois do trecho citado na fotografia do código (máx. 600 linhas por fotografia).",
            Default = "30", Min = 0, Max = 300, Unit = "linhas" }, // reserva da skill (prmake-plan.sh coderef)

        // ============================================================== Análise e correção — Testes locais
        new() { Section = "analise", Group = "testes", Plugin = Skills, Key = SkillsConfigurationKeys.CorrectionLocalTests, Type = SettingTypes.Enum, ReadBy = Sk,
            Label = "Testes locais da correção",
            Options = [new("changed", "Só os testes do que mudou"), new("off", "Desligados (só o CI do PR)")], Default = "changed" },
        new() { Section = "analise", Group = "testes", Plugin = Skills, Key = SkillsConfigurationKeys.CorrectionTestMaxSeconds, Type = SettingTypes.Number, ReadBy = Sk,
            Label = "Limite dos testes locais", Help = "Passou do limite = segue para o PR e o CI valida.", Default = "420", Min = 30, Max = 3600, Unit = "s" },

        // ============================================================== Executor e modelos
        new() { Section = "executor", Group = "modelos", Plugin = Skills, Key = SkillsConfigurationKeys.ExecutorAnalysisModel, Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Modelo da análise", Help = "Modelo do Claude Code na análise pelo executor. Vazio = o padrão da máquina.",
            Options = ClaudeCodeModels, AllowCustom = true },
        new() { Section = "executor", Group = "modelos", Plugin = Skills, Key = SkillsConfigurationKeys.ExecutorCorrectionModel, Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Modelo da correção", Help = "Modelo do Claude Code na correção e nos itens de trabalho. Vazio = o padrão da máquina.",
            Options = ClaudeCodeModels, AllowCustom = true },
        new() { Section = "executor", Group = "modelos", Plugin = Skills, Key = SkillsConfigurationKeys.SpecialistModel, Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Especialista do caso: respostas rápidas", Options = ClaudeCodeModels, AllowCustom = true, Default = "sonnet" },
        new() { Section = "executor", Group = "modelos", Plugin = Skills, Key = SkillsConfigurationKeys.SpecialistDeepModel, Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Especialista do caso: investigar a fundo", Options = ClaudeCodeModels, AllowCustom = true, Default = "opus" },
        new() { Section = "executor", Group = "execucao", Plugin = Skills, Key = SkillsConfigurationKeys.ExecutorCorrectionNewSession, Type = SettingTypes.Bool, ReadBy = Api,
            Label = "Correção numa sessão nova", Help = "A correção abre uma sessão nova do Claude, só com o resumo da análise, em vez de continuar a sessão da análise.",
            Default = Bool(ExecutionQueueOptions.Default.CorrectionInNewSession) },
        new() { Section = "executor", Group = "execucao", Plugin = Skills, Key = SkillsConfigurationKeys.WorkItemSlots, Type = SettingTypes.Number, ReadBy = ApiEx,
            Label = "Itens de trabalho simultâneos por máquina", Help = "Quantos itens de trabalho rodam ao mesmo tempo em cada máquina (fora do limite das análises/correções).",
            Default = ExecutionQueueOptions.DefaultWorkItemSlots.ToString(), Min = 1, Max = 10 },
        new() { Section = "executor", Group = "execucao", Plugin = Skills, Key = SkillsConfigurationKeys.ExecutorNoteDelaySeconds, Type = SettingTypes.Number, ReadBy = [], Obsolete = true,
            Label = "Espera depois de um comentário (obsoleta)", Help = "Não é mais lida (0080): o plano retoma na hora. Pode remover.", Unit = "s" },

        // ============================================================== Itens de trabalho
        new() { Section = "itens", Group = "tipos", Plugin = Skills, Key = SkillsConfigurationKeys.WorkItemTypes, Type = SettingTypes.Table, ReadBy = Api,
            Label = "Tipos de item de trabalho",
            Help = "Tipos que um item pode ter na Bancada (0085 r4). A chave é o que as skills e o MCP usam — não renomeie uma chave em uso.",
            Columns = [new("key", "Chave", SettingTypes.Text, Required: true, Width: "sm"), new("label", "Nome", SettingTypes.Text, Required: true, Width: "md"),
                new("icon", "Ícone (Material)", SettingTypes.Text, Width: "sm"), new("color", "Cor", SettingTypes.Text, Width: "sm"),
                new("description", "Descrição", SettingTypes.Textarea)] },
        new() { Section = "itens", Group = "tipos", Plugin = Skills, Key = SkillsConfigurationKeys.WorkItemTaskKinds, Type = SettingTypes.Json, JsonRoot = "array", ReadBy = Api,
            Label = "Tipos de sub-tarefa",
            Help = "[{kind, label, icon, executor, description, params: [{key, label, required?}], undo?}] — o que cada sub-tarefa faz, quem executa (claude/user) e os parâmetros que pede.",
            MultilineHint = true },
        new() { Section = "itens", Group = "modelos", Plugin = Skills, Key = SkillsConfigurationKeys.WorkItemTemplates, Type = SettingTypes.Json, JsonRoot = "array", ReadBy = Api,
            Label = "Modelos de item de trabalho",
            Help = "[{key, title, description, type?, tasks: [{title, kind, executor?, params, repeat?}]}]. Marcadores: {card}, {branch}, {suffix:destino}, {repository}, {flowTargets}; repeat = uma sub-tarefa por valor ({each}, {eachLabel}).",
            MultilineHint = true },
        new() { Section = "itens", Group = "validacao", Plugin = Skills, Key = SkillsConfigurationKeys.ValidationEnvironments, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSk,
            Label = "Ambientes de validação",
            Help = "environments: [{key, label, url, user?, kind?, readOnly?, client?, note?, database: {host = alias das credenciais da máquina, global}}]; " +
                   "plantAdminPattern (admin da planta: admin{plant}@solvacelabs.com); corporate (banco com todos os ambientes); productionReadOnly; " +
                   "defaults por tipo do card (bug, user-story). readOnly = o navegador da validação bloqueia gravações (DEMO, produção); " +
                   "readOnlyAllow (geral) e allow (por ambiente) = trechos de rota que podem gravar mesmo assim porque só guardam preferência/filtro do usuário (ex.: user-report-preferences). Senha nunca aqui: só na máquina de quem valida.",
            Default = Indented(ValidationEnvironments.DefaultJson), MultilineHint = true },

        // ============================================================== Assistente e horas
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantEnabled, Type = SettingTypes.Bool, ReadBy = Api,
            Label = "Assistente do card ligado", Help = "Sugere estimativa inicial, lançar horas e publicar resumos (sugestões que não travam).",
            Default = Bool(ExecutionAssistantSettings.Default.Enabled) },
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantHoursSteps, Type = SettingTypes.Steps, ListFormat = "csv",
            StepSources = AnalysisStepSources, ReadBy = Api,
            Label = "Etapas que sugerem lançar horas", Help = "Ao concluir estas etapas o assistente sugere lançar horas. Termine com * para prefixo (corrigir-*).",
            Default = string.Join(",", ExecutionAssistantSettings.Default.HoursSteps) },
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantHoursPerStep, Type = SettingTypes.Number, ReadBy = ApiFe,
            Label = "Horas sugeridas por etapa", Default = Number(ExecutionAssistantSettings.Default.HoursPerStep), Min = 0.5m, Max = 8, Unit = "h" },
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantAverageHours, Type = SettingTypes.Number, ReadBy = ApiFe,
            Label = "Média de horas do time por card", Help = "O assistente avisa quando o card passa da média.",
            Default = Number(ExecutionAssistantSettings.Default.AverageHours), Min = 0.5m, Max = 80, Unit = "h" },
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantAverageHoursBug, Type = SettingTypes.Number, ReadBy = Fe,
            Label = "Média de horas por bug", Help = "Vazio = a média por card.", Min = 0.5m, Max = 80, Unit = "h" },
        new() { Section = "assistente", Group = "assistente", Plugin = Skills, Key = SkillsConfigurationKeys.AssistantAverageHoursUserStory, Type = SettingTypes.Number, ReadBy = Fe,
            Label = "Média de horas por User Story", Help = "Vazio = a média por card.", Min = 0.5m, Max = 80, Unit = "h" },
        new() { Section = "assistente", Group = "estimativa", Plugin = Ai, Key = AIConfigurationKeys.BugInitialOriginalEstimate, Type = SettingTypes.Number, ReadBy = Api,
            Label = "Estimativa original", Help = "As três horas precisam estar preenchidas para a estimativa inicial ficar disponível.", Min = 0, Unit = "h" },
        new() { Section = "assistente", Group = "estimativa", Plugin = Ai, Key = AIConfigurationKeys.BugInitialRemainingWork, Type = SettingTypes.Number, ReadBy = Api,
            Label = "Horas restantes", Min = 0, Unit = "h" },
        new() { Section = "assistente", Group = "estimativa", Plugin = Ai, Key = AIConfigurationKeys.BugInitialCompletedWork, Type = SettingTypes.Number, ReadBy = Api,
            Label = "Horas lançadas", Min = 0, Unit = "h" },

        // ============================================================== IA do servidor
        new() { Section = "ia", Group = "provedor", Plugin = Ai, Key = "Provider", Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Provedor de IA", Help = "Provedor usado nas ações de IA do servidor (PR, resumo, Pergunte, chat). A configuração dele fica no grupo do provedor.",
            Options = [new("Claude", "Claude"), new("Gemini", "Gemini"), new("OpenAI", "OpenAI"), new("Copilot", "Copilot")] },

        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "ApiKey", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "Chave da API da Anthropic", Help = "platform.claude.com → API keys (sk-ant-…). As ações de IA são cobradas nesta chave; em uso pessoal, cada pessoa usa a sua." },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "Model", Type = SettingTypes.Text, ReadBy = Api,
            Label = "Modelo", Help = "Id do modelo, sem data (ex.: claude-haiku-4-5).", Default = ClaudeOptions.DefaultModel,
            Options = [new("claude-haiku-4-5", "Haiku 4.5"), new("claude-sonnet-5", "Sonnet 5"), new("claude-opus-5", "Opus 5")], AllowCustom = true },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "Effort", Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Esforço", Help = "Quanto o modelo pensa antes de responder. Não use com o Haiku 4.5. Vazio = o padrão do modelo.",
            Options = [new("low", "Baixo"), new("medium", "Médio"), new("high", "Alto"), new("max", "Máximo")] },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "MaxOutputTokens", Type = SettingTypes.Number, ReadBy = Api,
            Label = "Tamanho máximo da resposta", Help = "Atingido o teto, a resposta vem cortada e o PRMake avisa.",
            Default = ClaudeOptions.DefaultMaxOutputTokens.ToString(), Min = 1, Unit = "tokens" },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "TimeoutSeconds", Type = SettingTypes.Number, ReadBy = Api, Advanced = true,
            Label = "Tempo limite de cada tentativa", Default = ClaudeOptions.DefaultTimeoutSeconds.ToString(), Min = 1, Unit = "s" },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "MaxRetries", Type = SettingTypes.Number, ReadBy = Api, Advanced = true,
            Label = "Novas tentativas automáticas", Help = "Em sobrecarga, limite de uso ou falha de conexão.", Default = ClaudeOptions.DefaultMaxRetries.ToString(), Min = 0, Max = 10 },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "BaseUrl", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Endereço da API", Default = ClaudeOptions.DefaultBaseUrl },
        new() { Section = "ia", Group = "claude", Plugin = Claude, Key = "WorkspaceId", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Workspace da Anthropic", Help = "Só para chave de organização, fora de um workspace (wrkspc_…)." },

        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "ApiKey", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "Chave da API do Gemini", Help = "aistudio.google.com → Get API key. As ações de IA são cobradas nesta chave." },
        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "Model", Type = SettingTypes.Text, ReadBy = Api,
            Label = "Modelo", Default = new GeminiOptions().Model },
        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "ThinkingLevel", Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Nível de raciocínio", Help = "Vazio = o padrão do modelo.",
            Options = [new("minimal", "Mínimo"), new("low", "Baixo"), new("medium", "Médio"), new("high", "Alto")] },
        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "MaxOutputTokens", Type = SettingTypes.Number, ReadBy = Api,
            Label = "Tamanho máximo da resposta", Help = "Vazio = o limite do modelo.", Min = 1, Unit = "tokens" },
        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "TimeoutSeconds", Type = SettingTypes.Number, ReadBy = Api, Advanced = true,
            Label = "Tempo limite da requisição", Help = "Vazio = 100 s.", Min = 1, Unit = "s" },
        new() { Section = "ia", Group = "gemini", Plugin = Gemini, Key = "BaseUrl", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Endereço da API", Default = new GeminiOptions().BaseUrl },

        new() { Section = "ia", Group = "openai", Plugin = OpenAI, Key = "ApiKey", Type = SettingTypes.Secret, ReadBy = Api, Label = "Chave da API da OpenAI" },
        new() { Section = "ia", Group = "openai", Plugin = OpenAI, Key = "Model", Type = SettingTypes.Text, ReadBy = Api, Label = "Modelo", Default = new OpenAIOptions().Model },
        new() { Section = "ia", Group = "openai", Plugin = OpenAI, Key = "BaseUrl", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Endereço da API", Default = new OpenAIOptions().BaseUrl },

        new() { Section = "ia", Group = "copilot", Plugin = Copilot, Key = "ApiKey", Type = SettingTypes.Secret, ReadBy = Api, Label = "Chave da API do Copilot" },
        new() { Section = "ia", Group = "copilot", Plugin = Copilot, Key = "Model", Type = SettingTypes.Text, ReadBy = Api, Label = "Modelo" },
        new() { Section = "ia", Group = "copilot", Plugin = Copilot, Key = "BaseUrl", Type = SettingTypes.Text, ReadBy = Api, Advanced = true, Label = "Endereço da API" },

        new() { Section = "ia", Group = "custos", Plugin = Ai, Key = AiUsage.AiUsageRecorder.PricesKey, Type = SettingTypes.Table, RowKey = "model", ReadBy = Api,
            Label = "Preços da IA (US$ por milhão de tokens)",
            Help = "Usado só para estimar o custo de cada ação (Meu consumo de IA). O modelo casa pelo prefixo mais longo; leitura/escrita de cache vazias = 10% e 125% da entrada.",
            Columns =
            [
                new("model", "Modelo (prefixo)", SettingTypes.Text, Required: true, Width: "md"),
                new("input", "Entrada", SettingTypes.Number, Required: true, Width: "sm"),
                new("output", "Saída", SettingTypes.Number, Required: true, Width: "sm"),
                new("cacheRead", "Leitura de cache", SettingTypes.Number, Width: "sm"),
                new("cacheWrite", "Escrita de cache", SettingTypes.Number, Width: "sm")
            ] },

        // ============================================================== Prompts e modelos de texto
        new() { Section = "prompts", Group = "pr", Plugin = Ai, Key = "PromptBug", Type = SettingTypes.Prompt, ReadBy = ApiSkFe,
            Label = "Prompt do PR de bug", Help = "Como a IA escreve a descrição do PR e o RCA (entre <RCA> e </RCA>) de um bug.",
            Variables = [new("cardNumber", "número do card", "77434"), new("description", "repro steps / descrição do card", "Ao filtrar por data…"),
                new("githubCommitDiff", "commits e diff da branch", "diff --git a/…")] },
        new() { Section = "prompts", Group = "pr", Plugin = Ai, Key = "PromptUS", Type = SettingTypes.Prompt, ReadBy = ApiSkFe,
            Label = "Prompt do PR de User Story", Help = "Como a IA escreve a descrição do PR de uma User Story.",
            Variables = [new("cardNumber", "número do card", "77434"), new("description", "descrição da US", "Como gestor, quero…"),
                new("githubCommitDiff", "commits e diff da branch", "diff --git a/…")] },
        new() { Section = "prompts", Group = "devops", Plugin = Ai, Key = AIConfigurationKeys.BugSummaryPrompt, Type = SettingTypes.Prompt, ReadBy = ApiSkFe,
            Label = "Prompt do resumo no DevOps", Help = "Resumo não técnico (PT/EN) postado na discussion do card. Vazio = as regras da skill gerar-prmake.",
            Variables =
            [
                new("cardNumber", "número do card", "77434"), new("title", "título do card", "Filtro de datas não respeita o fuso"),
                new("reproSteps", "passos para reproduzir", "1. Abrir o relatório…"), new("description", "descrição do PR", "Corrige o filtro…"),
                new("rootCause", "root cause (RCA)", "A data era comparada em UTC…"), new("context", "contexto completo do card (situação, PR, RCA, alterações)", "## Situação do card…")
            ] },
        new() { Section = "prompts", Group = "handover", Plugin = Ai, Key = "TemplatePassagemConhecimento", Type = SettingTypes.Prompt, ReadBy = FeSk,
            Label = "Modelo da passagem de conhecimento", Help = "Formulário (markdown) que a tela e a skill gerar-handover preenchem com os dados do card." },
        new() { Section = "prompts", Group = "provedor", Plugin = Claude, Key = "SystemInstruction", Type = SettingTypes.Prompt, ReadBy = Api,
            Label = "Instruções fixas do Claude", Help = "Enviadas como system prompt em toda chamada. Vazio = nenhuma." },
        new() { Section = "prompts", Group = "provedor", Plugin = Gemini, Key = "SystemInstruction", Type = SettingTypes.Prompt, ReadBy = Api,
            Label = "Instruções fixas do Gemini", Help = "Diretrizes de precisão e estilo enviadas em toda chamada. Vazio = nenhuma." },
        new() { Section = "prompts", Group = "teams", Plugin = Teams, Key = "MessageTitle", Type = SettingTypes.Prompt, ReadBy = Api,
            Label = "Título da mensagem", Default = TeamsApprovalService.DefaultTitle, Variables = TeamsVariables },
        new() { Section = "prompts", Group = "teams", Plugin = Teams, Key = "MessageBody", Type = SettingTypes.Prompt, ReadBy = Api,
            Label = "Corpo da mensagem", Help = "Aceita markdown simples (negrito, quebras de linha).", Default = TeamsApprovalService.DefaultBody, Variables = TeamsVariables },

        // ============================================================== Engenharia reversa — Aprovação e cobertura
        new() { Section = "engenharia-reversa", Group = "aprovacao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringApproverRoles, Type = SettingTypes.List, ListFormat = "csv", ReadBy = Api,
            Label = "Papéis que aprovam e publicam", Help = "Papéis do PRMake (admin, gestor, support, user).",
            Options = [new("admin", "admin"), new("gestor", "gestor"), new("support", "support"), new("user", "user")],
            Default = string.Join(",", ReverseSettings.Default.ApproverRoles) },
        new() { Section = "engenharia-reversa", Group = "aprovacao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringRequiredDocs, Type = SettingTypes.List, ListFormat = "csv", ReadBy = Api,
            Label = "Documentos exigidos", Help = "Documentos que o módulo precisa ter publicados para contar como completo.",
            Options = ReverseDocTypes.All.Select(d => new SettingOption(d.Key, d.Title)).ToList(),
            Default = string.Join(",", ReverseDocTypes.DefaultRequired) },
        new() { Section = "engenharia-reversa", Group = "aprovacao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringMinCoverage, Type = SettingTypes.Number, ReadBy = Api,
            Label = "Cobertura mínima do inventário", Help = "De 0 a 1 (0.9 = 90%). Abaixo disso o revisor vê o aviso.",
            Default = Number((decimal)ReverseSettings.Default.MinCoverage), Min = 0, Max = 1 },
        new() { Section = "engenharia-reversa", Group = "aprovacao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringSupersedes, Type = SettingTypes.Map, ReadBy = Api, Advanced = true,
            Label = "Seções antigas substituídas",
            Help = "Seção antiga da Base Solvace → documentos que a substituem; com todos publicados, a antiga sai do espelho, da busca e do MCP. guia-* = seções do Guia; @armadilhas = armadilhas migradas.",
            MapKeyLabel = "Seção antiga", MapValueLabel = "Documentos", MapValueType = SettingTypes.List,
            Default = Compact(ReverseSettings.DefaultSupersedes) },

        // ============================================================== Engenharia reversa — Geração
        new() { Section = "engenharia-reversa", Group = "geracao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringGeneration, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSk, Advanced = true,
            Label = "Como a skill gera",
            Help = "subagentModel (sonnet|haiku|opus), modelByDoc {documento: modelo}, maxParallel (subagentes ao mesmo tempo), areaBudgetKb, checkpointEvery, snapshotMaxAgeDays, smallFileLines, blockMaxLines.",
            Default = Indented(ReverseSettings.DefaultGeneration), MultilineHint = true },
        new() { Section = "engenharia-reversa", Group = "geracao", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringTemplates, Type = SettingTypes.Map, ReadBy = Api, Advanced = true,
            Label = "Modelos dos documentos", Help = "Documento → modelo em markdown que substitui o do código. Sem linha = o modelo do código.",
            MapKeyLabel = "Documento", MapValueLabel = "Modelo (markdown)", MapValueType = SettingTypes.Textarea, Default = "{}" },

        // ============================================================== Engenharia reversa — Fontes
        new() { Section = "engenharia-reversa", Group = "fontes", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringReferenceDatabase, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSk, Advanced = true,
            Label = "Banco de referência",
            Help = "Banco lido direto pela skill: {environment, host = alias das credenciais da máquina, global, locals: []} — a DEMO, que reflete produção.",
            Default = Indented(Compact(new
            {
                environment = ReverseReferenceDatabase.Demo.Environment,
                host = ReverseReferenceDatabase.Demo.Host,
                global = ReverseReferenceDatabase.Demo.Global,
                locals = ReverseReferenceDatabase.Demo.Locals
            })) },
        new() { Section = "engenharia-reversa", Group = "fontes", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringTranslations, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSk, Advanced = true,
            Label = "Fonte das traduções do glossário",
            Help = "O Multilingual do revamp: source, environment (bloco do arquivo de credenciais da máquina), schema, credentials, secretId, languages. Sem segredo.",
            Default = Indented(ReverseSettings.DefaultTranslations) },
        new() { Section = "engenharia-reversa", Group = "fontes", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringInfra, Type = SettingTypes.Json, JsonRoot = "object", ReadBy = ApiSk, Advanced = true,
            Label = "Infraestrutura (AWS)",
            Help = "Etapa opcional de infra: accounts [{id, label}] (o perfil do AWS CLI é achado pela conta), regions, readOnly. Nada de segredo.",
            Default = Indented(ReverseSettings.DefaultInfra) },
        new() { Section = "engenharia-reversa", Group = "fontes", Plugin = Skills, Key = SkillsConfigurationKeys.ReverseEngineeringGlossaryExclusions, Type = SettingTypes.List, ListFormat = "json", ReadBy = Api, Advanced = true,
            Label = "Palavras ignoradas no glossário", Help = "Palavras genéricas de interface (Salvar, Cancelar…) que não contam na cobertura de termos.",
            Default = Compact(ReverseSettings.DefaultGlossaryExclusions) },

        // ============================================================== Knowledge Center
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.Environment, Type = SettingTypes.Enum, ReadBy = ApiSk,
            Label = "Ambiente ativo",
            Help = "Trocar para prod: a credencial 'prod' precisa estar no ~/.claude/knowledgecenter-credentials.json de quem sincroniza; a próxima sincronização faz a carga completa.",
            Options = [new(KnowledgeEnvironment.Dev, "Desenvolvimento"), new(KnowledgeEnvironment.Prod, "Produção")], Default = KnowledgeEnvironment.Dev },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.DevHost, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "DEV: host do banco", Help = "Somente leitura." },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.DevDatabase, Type = SettingTypes.Text, ReadBy = ApiSk, Label = "DEV: database" },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.DevSchema, Type = SettingTypes.Text, ReadBy = ApiSk, Label = "DEV: schema" },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.ProdHost, Type = SettingTypes.Text, ReadBy = ApiSk,
            Label = "PROD: host do banco", Help = "Somente leitura." },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.ProdDatabase, Type = SettingTypes.Text, ReadBy = ApiSk, Label = "PROD: database" },
        new() { Section = "knowledge", Group = "ambiente", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.ProdSchema, Type = SettingTypes.Text, ReadBy = ApiSk, Label = "PROD: schema" },
        new() { Section = "knowledge", Group = "filtro", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.ExcludePatterns, Type = SettingTypes.List, ListFormat = "json", ReadBy = ApiSk,
            Label = "Padrões extras de teste", Help = "Expressões regulares; artigo que casa fica de fora. Somadas ao filtro fixo do código.", Default = "[]" },
        new() { Section = "knowledge", Group = "filtro", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.ExcludeArticles, Type = SettingTypes.List, ListFormat = "json", ReadBy = ApiSk,
            Label = "Artigos sempre fora", Help = "Número do artigo (12 ou ART-12).", Default = "[]" },
        new() { Section = "knowledge", Group = "filtro", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.AllowArticles, Type = SettingTypes.List, ListFormat = "json", ReadBy = ApiSk,
            Label = "Artigos liberados apesar do filtro", Help = "Passam pelos padrões de teste, nunca por rascunho/arquivado/removido.", Default = "[]" },
        new() { Section = "knowledge", Group = "filtro", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.MinTextLength, Type = SettingTypes.Number, ReadBy = ApiSk,
            Label = "Texto mínimo do artigo", Help = $"Artigos com menos texto ficam de fora. Só vale acima do piso fixo de {solvace.knowledge.domain.Filtering.KnowledgeNoiseFilter.FloorMinTextLength}.",
            Min = 0, Unit = "caracteres" },
        new() { Section = "knowledge", Group = "sync", Plugin = Knowledge, Key = KnowledgeCenterConfigurationKeys.FullSyncHours, Type = SettingTypes.Number, ReadBy = ApiSk,
            Label = "Carga completa a cada", Help = "A carga completa pega renomes de categoria e remoções.", Default = "24", Min = 1, Unit = "h" },

        // ============================================================== Integrações — GitHub
        new() { Section = "integracoes", Group = "github", Plugin = GitHub, Key = "Owner", Type = SettingTypes.Text, ReadBy = Api,
            Label = "Organização (owner)", Help = "Dono dos repositórios no GitHub (ex.: solvace)." },
        new() { Section = "integracoes", Group = "github", Plugin = GitHub, Key = "Repo", Type = SettingTypes.Text, ReadBy = Api,
            Label = "Repositório padrão", Help = "Usado quando a ação não informa o repositório." },
        new() { Section = "integracoes", Group = "github", Plugin = GitHub, Key = "Token", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "Token do GitHub", Help = "GitHub → Settings → Developer settings → Tokens (classic), escopo repo; autorize o SSO da organização. Em uso pessoal, cada pessoa usa o seu." },
        new() { Section = "integracoes", Group = "github", Plugin = GitHub, Key = "ApiBaseUrl", Type = SettingTypes.Text, ReadBy = Api, Advanced = true,
            Label = "Endereço da API", Help = "Só para GitHub Enterprise. Vazio = https://api.github.com/." },
        new() { Section = "integracoes", Group = "github", Plugin = GitHub, Key = "Branch", Type = SettingTypes.Text, ReadBy = [], Obsolete = true,
            Label = "Branch (obsoleta)", Help = "Não é lida pelo código. Pode remover." },

        // ============================================================== Integrações — Teams
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "WebhookUrl", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "URL do Workflow do Teams", Help = "No grupo do Teams: ⋯ → Workflows → \"Enviar alertas de webhook para um chat\". Cada pessoa cola a sua em Minhas integrações." },
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "GroupName", Type = SettingTypes.Text, ReadBy = ApiFe,
            Label = "Nome do grupo", Help = "Exibido na tela ao pedir a aprovação." },
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "ButtonText", Type = SettingTypes.Pattern, ReadBy = Api,
            Label = "Texto do botão", Default = TeamsApprovalService.DefaultButton, Variables = TeamsVariables },
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "TitleColor", Type = SettingTypes.Enum, ReadBy = Api,
            Label = "Cor do título", Default = TeamsApprovalService.DefaultTitleColor,
            Options = [new("Default", "Padrão"), new("Dark", "Escura"), new("Light", "Clara"), new("Accent", "Destaque"), new("Good", "Verde"),
                new("Warning", "Amarela"), new("Attention", "Vermelha")] },
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "IncludeDescription", Type = SettingTypes.Bool, ReadBy = Api,
            Label = "Incluir a descrição do PR", Default = "false" },
        new() { Section = "integracoes", Group = "teams", Plugin = Teams, Key = "DescriptionMaxLength", Type = SettingTypes.Number, ReadBy = Api,
            Label = "Tamanho máximo da descrição", Help = "Passou, a descrição é cortada.", Default = TeamsApprovalService.DefaultDescriptionMaxLength.ToString(), Min = 1, Unit = "caracteres" },

        // ============================================================== Integrações — Tempo real e imagens
        new() { Section = "integracoes", Group = "tempo-real", Plugin = RealTime, Key = "AllowedOrigins", Type = SettingTypes.List, ListFormat = "csv", ReadBy = Api, Advanced = true,
            Label = "Origens permitidas", Help = "Endereços do front aceitos pelo canal de tempo real (ex.: https://prmake.solvace.com). Sem a chave = a configuração do servidor." },
        new() { Section = "integracoes", Group = "imagens", Plugin = Bucket, Key = "ApiKey", Type = SettingTypes.Secret, ReadBy = Api, Label = "Cloudinary: API key" },
        new() { Section = "integracoes", Group = "imagens", Plugin = Bucket, Key = "ApiSecret", Type = SettingTypes.Secret, ReadBy = Api, Label = "Cloudinary: API secret" },
        new() { Section = "integracoes", Group = "imagens", Plugin = Bucket, Key = "CloudinaryUrl", Type = SettingTypes.Secret, ReadBy = Api,
            Label = "Cloudinary: URL", Help = "cloudinary://<api_key>:<api_secret>@<cloud_name> — contém o segredo, por isso nunca é exibida." },
    ];

    // ------------------------------------------------------------------ consultas

    /// <summary>Plugins citados pelo catálogo (sem repetir).</summary>
    public static IReadOnlyList<SettingPluginRef> PluginRefs { get; } = Fields.Select(f => f.Plugin).Distinct().ToList();

    /// <summary>Entrada do catálogo da chave neste plugin (chave com maiúsculas exatas, como os leitores).</summary>
    public static SettingDef? Find(Plugin plugin, string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal) && f.Plugin.Matches(plugin));

    /// <summary>Segredo: pelo tipo do catálogo ou, fora dele, pelo nome (SensitiveFieldPolicy). Nunca sai em valor.</summary>
    public static bool IsSecret(Plugin plugin, string key) =>
        Find(plugin, key)?.Type == SettingTypes.Secret || SensitiveFieldPolicy.IsSensitive(key);

    // ------------------------------------------------------------------ formato dos padrões

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(decimal value) => value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>JSON compacto (texto JSON é normalizado; objeto é serializado).</summary>
    private static string Compact(object value, bool camelCase = false) => value is string json
        ? JsonNode.Parse(json)!.ToJsonString(CompactJson)
        : JsonSerializer.Serialize(value, camelCase ? CompactCamelJson : CompactJson);

    private static string Indented(string json) => JsonNode.Parse(json)!.ToJsonString(IndentedJson);
}
