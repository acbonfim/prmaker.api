using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application.Contracts;

public interface IKnowledgeRepository
{
    // Knowledge Center (cópia filtrada)
    Task<List<KnowledgeArticle>> GetArticlesAsync(string environment, bool tracked, CancellationToken cancellationToken);
    Task<KnowledgeArticle?> GetArticleAsync(string environment, int articleNumber, CancellationToken cancellationToken);
    Task<List<KnowledgeArticle>> SearchArticlesAsync(string environment, string? term, int limit, CancellationToken cancellationToken);
    void AddArticle(KnowledgeArticle article);
    void RemoveArticle(KnowledgeArticle article);
    Task<KnowledgeSyncState?> GetStateAsync(string environment, CancellationToken cancellationToken);
    void AddState(KnowledgeSyncState state);

    // Engenharia reversa
    /// <summary>Projetos não removidos com as seções (conteúdo incluso), sem rastreamento.</summary>
    Task<List<ArchitectureProject>> GetProjectsAsync(CancellationToken cancellationToken);
    /// <summary>Projeto pela chave com as seções, rastreado (para alterar). Inclui removidos.</summary>
    Task<ArchitectureProject?> GetProjectForUpdateAsync(string key, CancellationToken cancellationToken);
    void AddProject(ArchitectureProject project);
    void AddSection(ArchitectureSection section);
    void AddVersion(ArchitectureSectionVersion version);
    Task<List<ArchitectureSectionVersion>> GetVersionsAsync(Guid sectionId, CancellationToken cancellationToken);
    Task<ArchitectureSectionVersion?> GetVersionAsync(Guid sectionId, int version, CancellationToken cancellationToken);

    // Sugestões (fila do admin)
    void AddSuggestion(ArchitectureSuggestion suggestion);
    Task<ArchitectureSuggestion?> GetSuggestionAsync(Guid id, CancellationToken cancellationToken);
    Task<List<ArchitectureSuggestion>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken);

    // Perguntas do "Pergunte" (0040)
    void AddQuestion(ArchitectureQuestion question);
    Task<ArchitectureQuestion?> GetQuestionByNormalizedAsync(string normalized, CancellationToken cancellationToken);
    Task<ArchitectureQuestion?> GetQuestionAsync(Guid id, CancellationToken cancellationToken);
    Task<List<ArchitectureQuestion>> GetQuestionsAsync(string? status, CancellationToken cancellationToken);

    // Engenharia reversa por módulo (0052)
    Task<List<ReverseModule>> GetReverseModulesAsync(CancellationToken cancellationToken);
    Task<ReverseModule?> GetReverseModuleForUpdateAsync(string key, CancellationToken cancellationToken);
    void AddReverseModule(ReverseModule module);
    /// <summary>Cabeças das revisões (sem conteúdo), filtros opcionais; mais recentes primeiro.</summary>
    Task<List<domain.Responses.ReverseRevisionHead>> GetRevisionHeadsAsync(string? moduleKey, string? docType, IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken);
    Task<ReverseRevision?> GetRevisionAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<ReverseRevision?> GetOpenRevisionForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    Task<List<ReverseRevision>> GetPublishedRevisionsForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    Task<int> GetMaxRevisionNumberAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    void AddRevision(ReverseRevision revision);
    /// <summary>Anexos do módulo sem o conteúdo dos arquivos.</summary>
    Task<List<domain.Responses.ReverseAssetResponse>> GetAssetHeadsAsync(string moduleKey, CancellationToken cancellationToken);
    Task<ReverseAsset?> GetAssetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    void AddAsset(ReverseAsset asset);
    /// <summary>Itens publicados (todos, ou de um módulo), sem rastreamento.</summary>
    Task<List<ReverseIndexEntry>> GetIndexEntriesAsync(string? moduleKey, CancellationToken cancellationToken);
    /// <summary>Marca barata para saber se o índice mudou (quantidade + última atualização).</summary>
    Task<(int Count, DateTimeOffset? LastUpdate)> GetIndexStampAsync(CancellationToken cancellationToken);
    Task<List<ReverseIndexEntry>> GetIndexEntriesForUpdateAsync(string moduleKey, string docType, CancellationToken cancellationToken);
    void AddIndexEntry(ReverseIndexEntry entry);
    void RemoveIndexEntries(IEnumerable<ReverseIndexEntry> entries);
    /// <summary>0054: armadilhas (todas ou de um módulo), sem as removidas.</summary>
    Task<ReverseInfraSnapshot?> GetInfraAsync(string moduleKey, CancellationToken cancellationToken);
    Task<ReverseInfraSnapshot?> GetInfraForUpdateAsync(string moduleKey, CancellationToken cancellationToken);
    void AddInfra(ReverseInfraSnapshot snapshot);
    Task<List<ReverseTrap>> GetTrapsAsync(string? moduleKey, CancellationToken cancellationToken);
    Task<ReverseTrap?> GetTrapForUpdateAsync(Guid id, CancellationToken cancellationToken);
    void AddTrap(ReverseTrap trap);
    Task<ReverseCardContext?> GetCardContextAsync(string cardNumber, bool tracked, CancellationToken cancellationToken);
    void AddCardContext(ReverseCardContext context);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Configuração da engenharia reversa (0052) — plugin "Skills Configurations".</summary>
public interface IReverseSettingsProvider
{
    Task<ReverseSettings> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Banco de referência da engenharia reversa (0053): o ambiente que reflete produção (DEMO), global e locais.</summary>
public sealed record ReverseReferenceDatabase(string Environment, string Host, string Global, IReadOnlyList<string> Locals)
{
    public static ReverseReferenceDatabase Demo { get; } = new("DEMO", "prod", "DB_DEMO_PRD_GLOBAL",
        ["DB_DEMO_PRD_LOCAL_CTB", "DB_DEMO_PRD_LOCAL_GLB", "DB_DEMO_PRD_LOCAL_PAR"]);
}

/// <param name="Templates">Modelos que substituem os do código (tipo → markdown).</param>
/// <param name="GlossaryExclusions">Palavras genéricas de interface fora da cobertura de termos do glossário (0053).</param>
/// <param name="Supersedes">0054: seção antiga da Base Solvace → documentos da engenharia reversa que a substituem (todos
/// publicados = substituída). <c>guia-*</c> vale para as seções do Guia; <c>@armadilhas</c> = armadilhas migradas.</param>
/// <param name="Translations">0056: de onde a skill lê as traduções do glossário — o Multilingual do revamp (PostgreSQL),
/// JSON livre que a skill interpreta (fonte, ambiente, schema, arquivo de credencial, secret, idiomas). Sem segredo.</param>
/// <param name="Infra">0058: etapa OPCIONAL de infra — contas, perfis do AWS CLI e regiões que a skill consulta (somente leitura).
/// JSON livre que a skill interpreta. Sem segredo.</param>
public sealed record ReverseSettings(IReadOnlyList<string> ApproverRoles, IReadOnlyList<string> RequiredDocs, string? GateStep, double MinCoverage,
    IReadOnlyDictionary<string, string> Templates, ReverseReferenceDatabase? ReferenceDatabase = null, IReadOnlyList<string>? GlossaryExclusions = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Supersedes = null, string? Translations = null,
    string? Infra = null, string? Generation = null)
{
    public static ReverseSettings Default { get; } = new(["admin", "gestor"], domain.Reverse.ReverseDocTypes.DefaultRequired, "consultar-base,investigar-codigo", 0.9,
        new Dictionary<string, string>(), ReverseReferenceDatabase.Demo, DefaultGlossaryExclusions, DefaultSupersedes, DefaultTranslations, DefaultInfra,
        DefaultGeneration);

    /// <summary>
    /// 0066: como a skill gera — modelo dos subagentes (padrão e por documento), subagentes simultâneos, orçamento da área
    /// (KB do pacote de leitura), checkpoint (itens), idade máxima do retrato do banco/AWS (dias) e o corte dos trechos.
    /// </summary>
    public const string DefaultGeneration = """
        {"subagentModel": "sonnet", "modelByDoc": {}, "maxParallel": 3, "areaBudgetKb": 90, "checkpointEvery": 10, "snapshotMaxAgeDays": 7, "smallFileLines": 400, "blockMaxLines": 220}
        """;

    /// <summary>0056: traduções do produto no Multilingual do revamp (Aurora PostgreSQL, schema <c>multilingual</c>).</summary>
    public const string DefaultTranslations = """
        {"source": "multilingual", "environment": "prod", "schema": "multilingual", "credentials": "~/.claude/multilingual-credentials.json", "secretId": "multilingual/production", "languages": {"pt": "pt-BR", "en": "en-US", "es": "es-ES"}, "fallbackCredentials": ["~/.claude/postgres-credentials-dev.json"]}
        """;

    /// <summary>0058: a conta (id, não é segredo) manda; a skill acha o perfil do CLI da máquina que entra nela (sts) — sem nome de perfil fixo.</summary>
    public const string DefaultInfra = """
        {"accounts": [{"id": "367983645102", "label": "Solvace (revamp)"}], "regions": ["us-east-1"], "readOnly": true}
        """;

    /// <summary>Documentos exigidos que são técnicos (a visão prática fica de fora: é para pessoas, não para as análises).</summary>
    public IReadOnlyList<string> TechnicalRequired => RequiredDocs.Where(d => d != domain.Reverse.ReverseDocTypes.Practical).ToList();

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultSupersedes { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["visao-geral"] = ["visao", "arquitetura"],
        ["modulos"] = ["funcional", "uiux"],
        ["dados"] = ["arquitetura"],
        ["integracoes"] = ["arquitetura"],
        ["infra"] = ["arquitetura"],
        ["autenticacao"] = ["arquitetura"],
        ["jobs"] = ["arquitetura"],
        ["regras-de-negocio"] = ["funcional"],
        ["operacao"] = ["funcional"],
        ["armadilhas"] = ["@armadilhas"],
        ["guia-*"] = [domain.Reverse.ReverseDocTypes.Practical]
    };

    /// <summary>Palavras de interface que não são conceito do domínio (o plugin pode trocar a lista).</summary>
    public static IReadOnlyList<string> DefaultGlossaryExclusions { get; } =
    [
        "Salvar", "Cancelar", "Filtrar", "Filtro", "Data", "Buscar", "Pesquisar", "Editar", "Excluir", "Remover", "Adicionar", "Novo", "Nova",
        "Voltar", "Fechar", "Sim", "Não", "OK", "Confirmar", "Limpar", "Exportar", "Imprimir", "Detalhes", "Ações", "Opções", "Selecione",
        "Todos", "Todas", "Nenhum", "Carregando", "Erro", "Sucesso", "Atenção", "Aviso", "Enviar", "Anexar", "Visualizar", "Copiar",
        "Save", "Cancel", "Filter", "Date", "Search", "Edit", "Delete", "Remove", "Add", "New", "Back", "Close", "Yes", "No", "Confirm",
        "Clear", "Export", "Print", "Details", "Actions", "Options", "Select", "All", "None", "Loading", "Error", "Success", "Warning", "Send"
    ];
}

/// <summary>Configuração efetiva do KC (plugin "Knowledge Center Configurations"): ambiente ativo + regras extras do filtro.</summary>
public interface IKnowledgeSettingsProvider
{
    Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record KnowledgeSettings(string ActiveEnvironment, domain.Filtering.KnowledgeFilterOptions Filter);
