using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>Feature 0053: objetos do banco, evidência de banco, glossário com sinônimos, sugestões resolvidas e termos sugeridos.</summary>
public class ReverseDatabaseGlossaryTests
{
    private static readonly string[] Approver = ["gestor"];
    private static readonly string[] Dev = ["user"];

    private sealed class Settings(ReverseSettings value) : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private const string Funcional = """
        # Levantamento funcional — A3
        ## Resumo do módulo
        A3.
        ## Perfis e permissões
        ## Funcionalidades
        ## Casos de uso
        ## Regras de negócio
        ### RN-001 — Status vai para Encerrado quando todas as ações fecham
        - **Onde:** banco DEMO local · dbo.STP_SA3_FechaA3 (linha 42)
        - **Tabelas:** TB_SA3_A3
        A procedure `STP_SA3_FechaA3` muda o status; o trigger `TR_SA3_A3_AUDIT` grava o histórico.
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ## Integrações com outros módulos
        ## Glossário
        ### GLO-001 — A3
        - **Sinônimos:** SA3, RCA, RCA 1-pager, root cause analysis
        Relatório de análise de causa raiz.
        ## Lacunas e pontos a confirmar
        """;

    [Fact]
    public void Parser_reads_synonyms_bank_evidence_and_db_objects()
    {
        var items = ReverseDocParser.Parse(Funcional);
        var rn = items.Single(i => i.Id == "RN-001");
        Assert.Contains(rn.Evidence, e => e.StartsWith("banco: banco DEMO local · dbo.STP_SA3_FechaA3"));
        Assert.Contains("STP_SA3_FECHAA3", rn.Tables);
        Assert.Contains("TR_SA3_A3_AUDIT", rn.Tables);
        Assert.Contains("TB_SA3_A3", rn.Tables);
        var glo = items.Single(i => i.Id == "GLO-001");
        Assert.Equal(["SA3", "RCA", "RCA 1-pager", "root cause analysis"], glo.Synonyms);

        var trg = ReverseDocParser.Parse("### TRG-001 — trigger em TB_SA3_A3 (UPDATE)\n- **Banco:** DEMO local · alterado em 2026-08-12\nGrava histórico.").Single();
        Assert.Equal("TRG", trg.Kind);
        Assert.Contains(trg.Evidence, e => e.Contains("DEMO local"));
    }

    [Fact]
    public void Lint_requires_glossary_in_funcional_and_bank_section_in_arquitetura_and_evidence_for_sql_trg()
    {
        Assert.True(ReverseLint.Run(ReverseDocTypes.Get("funcional"), Funcional).Ok);
        var semGlossario = Funcional.Replace("## Glossário", "## Outra coisa");
        Assert.Contains("Glossário", ReverseLint.Run(ReverseDocTypes.Get("funcional"), semGlossario).MissingHeadings);

        var arq = ReverseLint.Run(ReverseDocTypes.Get("arquitetura"), "## Dados — tabelas e entidades\n### SQL-001 — view dbo.VW_SA3_A3\nsem evidência");
        Assert.Contains("Banco de dados: views, procedures, functions, triggers e jobs", arq.MissingHeadings);
        Assert.DoesNotContain("Dados — tabelas e entidades", arq.MissingHeadings);
        Assert.Contains("SQL-001", arq.WithoutEvidence);
        Assert.True(ReverseItemKinds.IsKind("SQL") && ReverseItemKinds.IsKind("TRG"));
    }

    private static (ReverseEngineeringApplication App, InMemoryKnowledgeRepository Repo) Create()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, kind, name) in new[] { ("legado-rca", "legacy", "A3"), ("revamp-kaizen", "revamp", "Kaizen") })
        {
            var p = new ArchitectureProject(key, "admin", now);
            p.Update(name, kind, null, "resumo", ["A3"], null, null, null, "admin", now);
            p.SetFriendly(name, null, name);
            repo.AddProject(p);
        }
        return (new ReverseEngineeringApplication(repo, new Settings(ReverseSettings.Default with { RequiredDocs = ["funcional"] })), repo);
    }

    private static async Task<Guid> SubmitAsync(ReverseEngineeringApplication app, string module, string content, List<ReverseSuggestionDecision>? decisions = null)
    {
        var session = await app.StartSessionAsync(module, "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = content, SuggestionDecisions = decisions }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        return session.Revision.Id;
    }

    [Fact]
    public async Task Synonyms_expand_the_search_RCA_finds_items_that_only_say_A3()
    {
        var (app, repo) = Create();
        var id = await SubmitAsync(app, "legado-rca", Funcional);
        await app.PublishAsync(id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);

        var hits = await app.SearchAsync("root cause analysis encerrado", null, null, null, 10, false, default);
        Assert.Contains(hits, h => h.Ref == "legado-rca#RN-001" || h.Ref == "legado-rca#GLO-001");
        var rca = await app.SearchAsync("rca status", null, ["RN"], null, 10, false, default);
        Assert.Equal("legado-rca#RN-001", Assert.Single(rca).Ref);

        // na Base Solvace (seções) também
        var arch = new ArchitectureApplication(repo, new KnowledgeSettingsStub());
        var sections = await arch.SearchAsync("rca encerrado", 5, null, null, null, default);
        Assert.Contains(sections, s => s.ProjectKey == "legado-rca" && s.SectionKey == "re-funcional");
        // sinônimo de um módulo não vale para outro
        var syn = await ReverseSearch.SynonymsAsync(repo, default);
        Assert.NotEmpty(syn.Alternatives("legado-rca", "rca"));
        Assert.Empty(syn.Alternatives("revamp-kaizen", "rca"));
    }

    [Fact]
    public async Task Publishing_resolves_the_suggestions_the_session_decided_and_suggests_glossary_terms()
    {
        var (app, repo) = Create();
        var arch = new ArchitectureApplication(repo, new KnowledgeSettingsStub());
        var applied = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", SectionKey = "re-funcional", Kind = "learning", Content = "status encerrado", CardNumber = "75067" }, "analise", default);
        var refused = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", SectionKey = "re-funcional", Kind = "gap", Content = "outra coisa" }, "analise", default);
        var untouched = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", Kind = "gap", Content = "fica" }, "analise", default);

        await Assert.ThrowsAsync<DomainException>(() => SubmitAsync(app, "legado-rca", Funcional,
            [new ReverseSuggestionDecision { SuggestionId = refused.Id, Decision = "recusada" }])); // recusa sem motivo

        var session = await app.StartSessionAsync("legado-rca", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest
        {
            Content = Funcional,
            SuggestionDecisions =
            [
                new() { SuggestionId = applied.Id, Decision = "aplicada", Items = ["RN-001"], Note = "regra do status" },
                new() { SuggestionId = refused.Id, Decision = "recusada", Note = "não é do módulo" }
            ]
        }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        var review = await app.GetRevisionAsync(session.Revision.Id, Approver, default);
        Assert.Equal(2, review.SuggestionDecisions.Count);
        Assert.All(review.SuggestionDecisions, d => Assert.Equal(ArchitectureSuggestionStatus.Pending, d.Status));

        var published = await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        Assert.Equal(2, published.ResolvedSuggestions.Count);
        Assert.Equal("75067", published.ResolvedSuggestions.Single(s => s.Decision == "applied").CardNumber);
        var all = await repo.GetSuggestionsAsync(null, default);
        Assert.Equal(ArchitectureSuggestionStatus.Applied, all.Single(s => s.Id == applied.Id).Status);
        Assert.Contains("RN-001", all.Single(s => s.Id == applied.Id).ResolutionNote);
        Assert.Equal(ArchitectureSuggestionStatus.Dismissed, all.Single(s => s.Id == refused.Id).Status);
        Assert.Equal(ArchitectureSuggestionStatus.Pending, all.Single(s => s.Id == untouched.Id).Status);

        // glossário → termos sugeridos (A3 já é palavra-chave)
        var module = await app.GetModuleAsync("legado-rca", Approver, default);
        Assert.Contains("SA3", module.SuggestedTerms);
        Assert.DoesNotContain("A3", module.SuggestedTerms);
        module = await app.ResolveTermAsync("legado-rca", new ResolveReverseTermRequest { Term = "SA3", Action = "alias" }, "gestor", Approver, default);
        Assert.Contains("SA3", module.Aliases);
        module = await app.ResolveTermAsync("legado-rca", new ResolveReverseTermRequest { Term = "RCA", Action = "keyword" }, "gestor", Approver, default);
        Assert.Contains("RCA", (await repo.GetProjectsAsync(default)).Single(p => p.Key == "legado-rca").Keywords);
        module = await app.ResolveTermAsync("legado-rca", new ResolveReverseTermRequest { Term = "root cause analysis", Action = "dismiss" }, "gestor", Approver, default);
        Assert.DoesNotContain("root cause analysis", module.SuggestedTerms);
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => app.ResolveTermAsync("legado-rca", new ResolveReverseTermRequest { Term = "RCA 1-pager", Action = "alias" }, "dev", Dev, default));
    }

    [Fact]
    public async Task Session_pack_brings_the_reference_database_exclusions_and_the_published_session()
    {
        var (app, _) = Create();
        var id = await SubmitAsync(app, "legado-rca", Funcional);
        await app.PublishAsync(id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        var pack = await app.StartSessionAsync("legado-rca", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.Equal("DB_DEMO_PRD_GLOBAL", pack.ReferenceDatabase!.Global);
        Assert.Equal(3, pack.ReferenceDatabase.Locals.Count);
        Assert.Contains("Salvar", pack.GlossaryExclusions);
        Assert.Null(pack.PublishedSession); // a revisão publicada não mandou dados de sessão
        Assert.Contains(ReverseProgress.Parse(pack.Revision.Progress is null ? null : System.Text.Json.JsonSerializer.Serialize(pack.Revision.Progress), DateTimeOffset.UtcNow).Steps, s => s.Key == "banco");
    }

    private sealed class KnowledgeSettingsStub : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new KnowledgeSettings("dev", domain.Filtering.KnowledgeFilterOptions.None));
    }
}
