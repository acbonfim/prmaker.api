using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>
/// 0097: engenharia reversa mais barata e aberta — exigidos (funcional, arquitetura, spec-arquitetura), design/visão
/// descontinuados, prática sob demanda pelas perguntas sem resposta do Pergunte.
/// </summary>
public class Reverse0097Tests
{
    internal static readonly string[] Approver = ["gestor"];
    internal static readonly string[] Dev = ["admin"];

    internal sealed class Settings(ReverseSettings value) : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    internal sealed class Kc : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", domain.Filtering.KnowledgeFilterOptions.None));
    }

    internal const string Funcional = """
        # Levantamento funcional — Checklist
        ## Resumo do módulo
        Checklist legado.
        ## Perfis e permissões
        ## Funcionalidades
        ### FN-001 — Responder checklist
        Tela TELA-001.
        ## Casos de uso
        ## Regras de negócio
        ### RN-001 — Checklist só fecha com todas as perguntas respondidas
        - **Onde:** `edv-solvace/checklist/resp.asp:120`
        ### RN-002 — Compliance por checklist considera só itens ativos
        - **Onde:** `edv-solvace/checklist/compliance.asp:40`
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ## Integrações com outros módulos
        ## Glossário
        ## Lacunas e pontos a confirmar
        ### GAP-001 — Tela de compliance fora do pacote desta área
        Não foi lida nesta passada.
        ### GAP-002 — Cálculo de compliance diverge entre front e back
        - **Onde:** `edv-solvace/checklist/compliance.asp:44`
        ### GAP-003 — Não foi possível ler o anexo do Figma
        Sem figma no pacote.
        ### GAP-004 — Não foi possível ler a procedure STP_CHECK
        - **Onde:** `edv-solvace/db/stp_check.sql:10`
        """;

    internal static ArchitectureProject Project(InMemoryKnowledgeRepository repo, string key, string name, string kind = "legacy", string? area = null)
    {
        var now = DateTimeOffset.UtcNow;
        var p = new ArchitectureProject(key, "admin", now);
        p.Update(name, kind, null, "resumo", [name], null, null, null, "admin", now);
        p.SetFriendly(name, area, null);
        repo.AddProject(p);
        return p;
    }

    internal static async Task PublishAsync(ReverseEngineeringApplication app, string key, string doc, string content, string? mode = null)
    {
        var session = await app.StartSessionAsync(key, doc, new StartReverseSessionRequest { Mode = mode }, "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = content, CoverageRatio = 0.95 }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
    }

    internal static (InMemoryKnowledgeRepository Repo, ReverseEngineeringApplication App, Settings Settings) Create(ReverseSettings? settings = null)
    {
        var repo = new InMemoryKnowledgeRepository();
        var s = new Settings(settings ?? ReverseSettings.Default);
        return (repo, new ReverseEngineeringApplication(repo, s), s);
    }

    [Fact]
    public void Required_docs_are_functional_architecture_and_architecture_spec()
    {
        Assert.Equal(["funcional", "arquitetura", "spec-arquitetura"], ReverseDocTypes.DefaultRequired);
        Assert.True(ReverseDocTypes.ByKey["design"].Discontinued);
        Assert.True(ReverseDocTypes.ByKey["visao"].Discontinued);
        Assert.False(ReverseDocTypes.ByKey["funcional"].Discontinued);
        Assert.False(ReverseDocTypes.ByKey[ReverseDocTypes.Practical].Discontinued);
        Assert.Equal(3, ReverseSettings.Default.PracticalMinQuestions);
    }

    [Theory]
    [InlineData("design")]
    [InlineData("visao")]
    public async Task Discontinued_documents_refuse_new_sessions_but_stay_readable_and_searchable(string doc)
    {
        var (repo, app, s) = Create();
        Project(repo, "legado-checklist", "Checklist");
        var error = await Assert.ThrowsAsync<DomainException>(() => app.StartSessionAsync("legado-checklist", doc, new StartReverseSessionRequest(), "dev", Dev, default));
        Assert.Contains("descontinuad", error.Message);

        // o que já foi publicado continua legível e na busca (escrito como antes, pela seção da Base)
        var arch = new ArchitectureApplication(repo, new Kc(), s);
        var type = ReverseDocTypes.ByKey[doc];
        var item = doc == "design" ? "UI-001" : "OBJ-001";
        await arch.WriteSectionAsync("legado-checklist", type.SectionKey,
            new WriteArchitectureSectionRequest { Content = $"# {type.Title}\n## Lacunas\n### {item} — Grade de respostas do checklist\nTexto.", Source = "admin" }, "admin", default);
        var published = await app.GetDocAsync("legado-checklist", doc, default);
        Assert.Contains(item, published.Content);
        Assert.True(published.Type.Discontinued);
        Assert.Contains(await app.SearchAsync("grade respostas", null, null, null, 10, false, default), h => h.ItemId == item);
        var types = await app.GetDocTypesAsync(default, false);
        Assert.True(types.Single(t => t.Key == doc).Discontinued);
        Assert.False(types.Single(t => t.Key == doc).Required);
    }

    [Fact]
    public async Task Module_is_complete_with_the_three_required_docs_and_practical_is_suggested_by_open_questions()
    {
        var (repo, app, _) = Create();
        Project(repo, "legado-checklist", "Checklist");
        await PublishAsync(app, "legado-checklist", "funcional", Funcional);

        // perguntas sem resposta do módulo: abertas e not-found/partial; respondida ou de outro módulo não conta
        void Ask(string text, string coverage, string? project = null)
        {
            var q = new ArchitectureQuestion(text, "ana", DateTimeOffset.UtcNow);
            q.Asked("operacao", coverage, project, null, "ana", DateTimeOffset.UtcNow);
            repo.AddQuestion(q);
        }
        Ask("Como reabrir um checklist fechado?", "not-found");
        Ask("Quem pode excluir resposta do checklist?", "partial");
        Ask("Onde vejo o compliance?", "not-found", "legado-checklist");
        Ask("Como criar um checklist?", "answered");
        Ask("Como criar um kaizen?", "not-found");

        var module = await app.GetModuleAsync("legado-checklist", Approver, default);
        Assert.Equal(3, module.OpenQuestions);
        Assert.True(module.PracticalSuggested);              // sem prática publicada e 3 ≥ 3
        Assert.Equal(3, module.RequiredCount);
        Assert.False(module.Complete);
        Assert.True(module.Docs.Single(d => d.Type == "design").Discontinued);

        var listed = (await app.ListModulesAsync(default)).Single(m => m.Key == "legado-checklist");
        Assert.Equal(3, listed.OpenQuestions);
        Assert.True(listed.PracticalSuggested);

        // com o mínimo acima das perguntas, não sugere
        var (_, strict, _) = (repo, new ReverseEngineeringApplication(repo, new Settings(ReverseSettings.Default with { PracticalMinQuestions = 4 })), 0);
        Assert.False((await strict.GetModuleAsync("legado-checklist", Approver, default)).PracticalSuggested);
    }

    // ── B3: GAP do gerador ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GAP-010 — Tela fora do pacote desta área", false, true)]
    [InlineData("GAP-010 — O pacote não traz o front", false, true)]
    [InlineData("GAP-010 — Pacote NÃO INCLUI a procedure", false, true)]
    [InlineData("GAP-010 — Sem Figma para a tela de cadastro", false, true)]
    [InlineData("GAP-010 — Figma não anexado", false, true)]
    [InlineData("GAP-010 — Arquivo não lido nesta área", false, true)]
    [InlineData("GAP-010 — Serviço fora desta área", false, true)]
    [InlineData("GAP-010 — Orçamento da área estourou", false, true)]
    [InlineData("GAP-010 — O subagente não terminou", false, true)]
    [InlineData("GAP-010 — Inventário não lista os jobs", false, true)]
    [InlineData("GAP-010 — Retrato não tem o banco local", false, true)]
    [InlineData("GAP-010 — Não foi possível ler o arquivo", false, true)]
    [InlineData("GAP-010 — Não foi possível ler a procedure STP_X", true, false)]
    [InlineData("GAP-010 — Cálculo diverge entre front e back", false, false)]
    [InlineData("GAP-010 — Pacote de idiomas não traduz o status", false, false)]
    public void Generator_gap_phrases(string text, bool citesCode, bool expected) =>
        Assert.Equal(expected, ReverseGeneratorGaps.Matches(text, citesCode));

    [Fact]
    public void Normalization_matches_the_skill_rule()
    {
        Assert.Equal("orcamento da area 5s", ReverseGeneratorGaps.Normalize("**Orçamento** da ÁREA — (5S)!"));
        Assert.Equal("nao foi possivel ler", ReverseGeneratorGaps.Normalize("Não foi\npossível   ler"));
        // a lista única (a skill tem a mesma em re_gaps.py): só letras minúsculas sem acento e espaços simples
        foreach (var phrase in ReverseGeneratorGaps.Phrases.Concat(ReverseGeneratorGaps.PhrasesWithoutEvidence))
            Assert.Equal(phrase, ReverseGeneratorGaps.Normalize(phrase));
    }

    [Fact]
    public async Task Generator_gaps_stay_out_of_the_index_and_the_check_warns()
    {
        var (repo, app, _) = Create();
        Project(repo, "legado-checklist", "Checklist");
        var lint = await app.LintAsync("legado-checklist", new LintReverseDocumentRequest { DocType = "funcional", Content = Funcional }, default);
        Assert.Contains(lint.Warnings, w => w.Contains("processo de geração") && w.Contains("GAP-001") && w.Contains("GAP-003") && !w.Contains("GAP-002"));

        await PublishAsync(app, "legado-checklist", "funcional", Funcional);
        var gaps = (await app.SearchAsync(null, ["legado-checklist"], ["GAP"], null, 50, false, default)).Select(h => h.ItemId).ToList();
        Assert.Equal(["GAP-002", "GAP-004"], gaps.Order().ToList());
    }

    // ── B4: índice técnico ──────────────────────────────────────────────────────────────────────

    internal const string Technical = """
        # Índice técnico — Checklist

        ## Endpoints
        ### END-0001 — POST /checklist/resp.asp (salvar respostas do checklist)
        - **Onde:** `edv-solvace/checklist/resp.asp:12`
        - **Tabelas:** TB_CHECKLIST_RESP
        - **Commit:** edv-solvace@abc1234
        ### END-0002 — GET /api/v1/checklist/{id}
        - **Onde:** `revamp-checklist-api/src/Controllers/ChecklistController.cs:40`

        ## Rotas e telas
        ### ROT-0001 — /checklist/:id → ChecklistDetailComponent
        - **Onde:** `revamp-checklist-front/src/app/app.routes.ts:20`

        ## Tabelas
        ### TAB-0001 — TB_CHECKLIST_RESP

        ## Objetos do banco
        ### DBO-0001 — procedure dbo.STP_CHECKLIST_FECHAR

        ## Chaves de configuração
        ### CHV-0001 — Checklist:MaxPerguntas

        ## Mensagens
        ### MSG-0001 — "Responda todas as perguntas antes de fechar"
        """;

    [Fact]
    public void Technical_index_kinds_do_not_collide_with_the_existing_ones()
    {
        foreach (var kind in new[] { "END", "ROT", "TAB", "DBO", "CHV", "MSG" }) Assert.True(ReverseItemKinds.IsKind(kind), kind);
        Assert.Equal(ReverseItemKinds.All.Count, ReverseItemKinds.All.Select(k => k.Prefix).Distinct().Count());
        var type = ReverseDocTypes.ByKey[ReverseDocTypes.Technical];
        Assert.True(type.Generated);
        Assert.Equal("re-tecnico", type.SectionKey);
        Assert.DoesNotContain("SQL", type.Kinds);                                     // SQL é do levantamento de arquitetura
        Assert.DoesNotContain(type.Kinds, k => ReverseDocTypes.All.Where(t => !t.Generated).Any(t => t.Kinds.Contains(k)));
        Assert.Equal(("legado-checklist", "END-001"), ReverseItemKinds.ParseRef("legado-checklist#END-0001")!.Value);
    }

    [Fact]
    public async Task Technical_index_is_published_without_review_and_summarized_in_the_module()
    {
        var (repo, app, s) = Create();
        Project(repo, "legado-checklist", "Checklist");
        await Assert.ThrowsAsync<DomainException>(() => app.StartSessionAsync("legado-checklist", ReverseDocTypes.Technical, new StartReverseSessionRequest(), "dev", Dev, default));
        await Assert.ThrowsAsync<DomainException>(() => app.PublishTechnicalIndexAsync("legado-checklist", new PublishTechnicalIndexRequest { Content = "# vazio" }, "dev", default));

        var generatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var result = await app.PublishTechnicalIndexAsync("legado-checklist", new PublishTechnicalIndexRequest
        {
            Content = Technical, GeneratedAt = generatedAt,
            Commits = [new() { Repository = "edv-solvace", Commit = "abc1234def", Branch = "master" }],
            Counts = new() { ["END"] = 2, ["ROT"] = 1, ["TAB"] = 1, ["DBO"] = 1, ["CHV"] = 1, ["MSG"] = 1 }
        }, "dev", default);
        Assert.Equal(7, result.Items);
        Assert.Equal(1, result.Version);

        var module = await app.GetModuleAsync("legado-checklist", Dev, default);
        Assert.NotNull(module.TechnicalIndex);
        Assert.Equal(1, module.TechnicalIndex!.Version);
        Assert.Equal(2, module.TechnicalIndex.Counts["END"]);
        Assert.Equal("edv-solvace", module.TechnicalIndex.Commits.Single().Repository);
        Assert.Equal(generatedAt, module.TechnicalIndex.GeneratedAt);
        Assert.False(module.Complete);                                                 // fora do "completo"
        Assert.Equal(0, module.PublishedRequired);
        var doc = module.Docs.Single(d => d.Type == ReverseDocTypes.Technical);
        Assert.True(doc.Generated);
        Assert.False(doc.Required);
        Assert.NotNull(doc.Published);
        Assert.NotNull((await app.ListModulesAsync(default)).Single(m => m.Key == "legado-checklist").TechnicalIndex);

        var revision = (await app.ListRevisionsAsync("legado-checklist", ReverseDocTypes.Technical, "all", default)).Single();
        Assert.Equal(ReverseRevisionMode.Generated, revision.Mode);
        Assert.Equal(ReverseRevisionStatus.Published, revision.Status);

        // regerar: a versão nova substitui a anterior (revisão anterior fica "substituída")
        var again = await app.PublishTechnicalIndexAsync("legado-checklist", new PublishTechnicalIndexRequest { Content = Technical.Replace("END-0002", "END-0003") }, "dev", default);
        Assert.Equal(2, again.Version);
        Assert.Contains(again.Warnings, w => w.Contains("commit"));
        var revisions = await app.ListRevisionsAsync("legado-checklist", ReverseDocTypes.Technical, "all", default);
        Assert.Equal(2, revisions.Count);
        Assert.Single(revisions, r => r.Status == ReverseRevisionStatus.Published);
        Assert.Contains(await app.SearchAsync(null, ["legado-checklist"], ["END"], null, 10, false, default), h => h.ItemId == "END-003");
        Assert.DoesNotContain(await app.SearchAsync(null, ["legado-checklist"], ["END"], null, 10, false, default), h => h.ItemId == "END-002");
    }

    [Fact]
    public async Task Technical_items_weigh_less_in_search_and_stay_out_of_the_ask_context()
    {
        var (repo, app, _) = Create();
        Project(repo, "legado-checklist", "Checklist");
        await PublishAsync(app, "legado-checklist", "funcional", Funcional);
        await app.PublishTechnicalIndexAsync("legado-checklist", new PublishTechnicalIndexRequest { Content = Technical }, "dev", default);

        var hits = await app.SearchAsync("responder perguntas checklist fechar", null, null, null, 20, false, default);
        Assert.Contains(hits, h => h.DocType == ReverseDocTypes.Technical);
        var rule = hits.First(h => h.ItemId == "RN-001");
        var message = hits.First(h => h.ItemId == "MSG-001");
        Assert.True(rule.Score > message.Score, $"RN {rule.Score} × MSG {message.Score}");

        var ask = await app.GetAnswerContextAsync("como fechar o checklist com perguntas respondidas", ["fechar", "checklist"], [],
            ArchitectureQuestionKind.Operation, 10, 6, 1500, default);
        Assert.NotEmpty(ask);
        Assert.DoesNotContain(ask, i => i.DocType == ReverseDocTypes.Technical);
    }
}
