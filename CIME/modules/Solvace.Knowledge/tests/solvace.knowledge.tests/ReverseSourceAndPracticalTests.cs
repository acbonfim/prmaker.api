using System.IO.Compression;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>Feature 0054: engenharia reversa como fonte, visão prática, armadilhas ligadas aos itens.</summary>
public class ReverseSourceAndPracticalTests
{
    private static readonly string[] Approver = ["gestor"];
    private static readonly string[] Dev = ["user"];

    private sealed class Settings(ReverseSettings value) : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class Kc : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", domain.Filtering.KnowledgeFilterOptions.None));
    }

    private const string Funcional = """
        # Levantamento funcional — A3
        ## Resumo do módulo
        A3 legado.
        ## Perfis e permissões
        ## Funcionalidades
        ### FN-001 — Criar A3
        Tela TELA-001.
        ## Casos de uso
        ## Regras de negócio
        ### RN-001 — A3 só passa de etapa com aprovador
        - **Onde:** `sa3_registro.asp:210`
        ### RN-002 — (removido) virou configuração
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ## Integrações com outros módulos
        ## Glossário
        ### GLO-001 — A3
        - **Sinônimos:** SA3, RCA
        ## Lacunas e pontos a confirmar
        """;

    private const string Practical = """
        # Visão prática — A3
        ## O que é e onde fica
        O A3 é o módulo legado de análise de causa raiz. <!-- fonte: FN-001 -->
        ## Como chegar
        Menu Melhoria → A3. <!-- fonte: FN-001 -->
        ## Como fazer
        ### TUT-001 — Como criar um A3
        1. Clique em Novo. <!-- fonte: FN-001 -->
        ## Perguntas práticas
        ### FAQ-001 — Por que não consigo passar a etapa?
        Falta escolher o aprovador da etapa. <!-- fonte: RN-001 -->
        ## Regras em linguagem simples
        ## Como configurar e dar acesso
        ## Como testar
        ## Glossário
        """;

    private static (ReverseEngineeringApplication App, ArchitectureApplication Arch, InMemoryKnowledgeRepository Repo) Create(IReadOnlyList<string>? required = null)
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        var p = new ArchitectureProject("legado-rca", "admin", now);
        p.Update("A3 legado", "legacy", null, "resumo", ["A3"], null, null, null, "admin", now);
        p.SetFriendly("A3", null, "RCA");
        repo.AddProject(p);
        var settings = new Settings(ReverseSettings.Default with { RequiredDocs = required ?? ["funcional", "pratica"] });
        return (new ReverseEngineeringApplication(repo, settings), new ArchitectureApplication(repo, new Kc(), settings), repo);
    }

    private static async Task<Guid> PublishAsync(ReverseEngineeringApplication app, string doc, string content)
    {
        var session = await app.StartSessionAsync("legado-rca", doc, new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = content }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        return session.Revision.Id;
    }

    private static async Task SeedOldSectionsAsync(ArchitectureApplication arch)
    {
        foreach (var (key, text) in new[] { ("modulos", "telas antigas"), ("regras-de-negocio", "regra antiga A3"), ("armadilhas", "## Etapa travada\nSem aprovador a etapa não anda (card 75067)."), ("guia-o-que-e", "guia antigo") })
            await arch.WriteSectionAsync("legado-rca", key, new WriteArchitectureSectionRequest { Content = text, Source = "admin" }, "admin", default);
    }

    [Fact]
    public async Task Old_sections_covered_by_published_documents_leave_the_mirror_search_and_index_but_stay_as_history()
    {
        var (app, arch, _) = Create();
        await SeedOldSectionsAsync(arch);
        await PublishAsync(app, "funcional", Funcional);

        var project = await arch.GetProjectAsync("legado-rca", default);
        Assert.Contains("regras-de-negocio", project.SupersededSections.Keys);   // coberta só pelo funcional
        Assert.DoesNotContain("modulos", project.SupersededSections.Keys);       // precisa de funcional + uiux
        Assert.Contains(project.Sections, s => s.Key == "regras-de-negocio");    // histórico na tela

        var (_, zip) = await arch.ExportAsync(default);
        using var archive = new ZipArchive(new MemoryStream(zip));
        Assert.Null(archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("-regras-de-negocio.md")));
        Assert.NotNull(archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("-modulos.md")));
        var hits = await arch.SearchAsync("regra antiga", 10, null, null, null, default);
        Assert.DoesNotContain(hits, h => h.SectionKey == "regras-de-negocio");
        Assert.Contains("fonte: engenharia reversa", await arch.BuildIndexAsync(default));
    }

    [Fact]
    public async Task Practical_view_waits_for_the_required_docs_demands_sources_and_replaces_the_old_guide()
    {
        var (app, arch, _) = Create();
        await SeedOldSectionsAsync(arch);
        var blocked = await Assert.ThrowsAsync<DomainException>(() => app.StartSessionAsync("legado-rca", "pratica", new StartReverseSessionRequest(), "dev", Dev, default));
        Assert.Contains("funcional", blocked.Message);
        Assert.Contains("funcional", (await app.GetModuleAsync("legado-rca", Dev, default)).Docs.Single(d => d.Type == "pratica").BlockedBy);

        await PublishAsync(app, "funcional", Funcional);
        var pack = await app.StartSessionAsync("legado-rca", "pratica", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.Contains("funcional", pack.PublishedDocs.Keys);

        var type = ReverseDocTypes.Get("pratica");
        var semFonte = ReverseLint.Run(type, Practical.Replace("Falta escolher o aprovador da etapa. <!-- fonte: RN-001 -->", "Falta escolher o aprovador."),
            otherDocIds: new Dictionary<string, string> { ["FN-001"] = "funcional", ["RN-001"] = "funcional" });
        Assert.Contains(semFonte.Errors, e => e.Contains("FAQ-001"));
        var tecnico = ReverseLint.Run(type, Practical.Replace("Clique em Novo.", "Clique em Novo (grava em TB_SA3_A3 via sa3_registro.asp)."),
            otherDocIds: new Dictionary<string, string> { ["FN-001"] = "funcional", ["RN-001"] = "funcional" });
        Assert.Contains(tecnico.Errors, e => e.Contains("TB_SA3_A3") && e.Contains("sa3_registro.asp"));
        var removida = ReverseLint.Run(type, Practical.Replace("<!-- fonte: RN-001 -->", "<!-- fonte: RN-002 -->"),
            otherDocIds: new Dictionary<string, string> { ["FN-001"] = "funcional" }, removedIds: ["RN-002"]);
        Assert.Contains(removida.Errors, e => e.Contains("removido"));
        var inexistente = ReverseLint.Run(type, Practical.Replace("<!-- fonte: RN-001 -->", "<!-- fonte: RN-099 -->"),
            otherDocIds: new Dictionary<string, string> { ["FN-001"] = "funcional", ["RN-001"] = "funcional" });
        Assert.Contains(inexistente.Errors, e => e.Contains("RN-099"));

        await app.SaveRevisionAsync(pack.Revision.Id, new SaveReverseRevisionRequest { Content = Practical }, "dev", Dev, default);
        await app.SubmitAsync(pack.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(pack.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);

        var project = await arch.GetProjectAsync("legado-rca", default);
        Assert.Equal(ArchitectureSectionAudience.Human, project.Sections.Single(s => s.Key == "re-pratica").Audience);
        Assert.Contains("guia-o-que-e", project.SupersededSections.Keys);
        var module = await app.GetModuleAsync("legado-rca", Dev, default);
        Assert.True(module.Complete);
        Assert.Empty(module.Docs.Single(d => d.Type == "pratica").StaleBecause);
        // fora do espelho (é para pessoas) e fora do MCP/for-card, mas no Pergunte (busca de seções)
        var (_, zip) = await arch.ExportAsync(default);
        using (var archive = new ZipArchive(new MemoryStream(zip)))
            Assert.Null(archive.Entries.FirstOrDefault(e => e.FullName.Contains("re-pratica")));
        Assert.Contains(await arch.SearchAsync("aprovador etapa", 10, null, null, null, default), h => h.SectionKey == "re-pratica");
        Assert.DoesNotContain("FAQ-001", await app.ForCardAsync("90001", "A3", "aprovador etapa", default));

        // técnico republicado → visão prática desatualizada
        await Task.Delay(5);
        await PublishAsync(app, "funcional", Funcional.Replace("Criar A3", "Criar um A3"));
        Assert.Contains("Levantamento funcional", (await app.GetModuleAsync("legado-rca", Dev, default)).Docs.Single(d => d.Type == "pratica").StaleBecause);
    }

    [Fact]
    public async Task Traps_link_to_items_reach_the_analysis_and_replace_the_old_section_after_migration()
    {
        var (app, arch, repo) = Create(["funcional"]);
        await SeedOldSectionsAsync(arch);
        await PublishAsync(app, "funcional", Funcional);

        var pack = await app.StartSessionAsync("legado-rca", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.Contains("Sem aprovador", pack.LegacyTraps);

        var created = await app.CreateTrapsAsync("legado-rca", [new CreateReverseTrapRequest { Title = "Etapa travada sem aprovador", Text = "Sintoma… causa… consulta…", Items = ["RN-1"], Cards = ["75067"], Origin = "migrated" }],
            "dev", Dev, default);
        var trap = Assert.Single(created);
        Assert.True(trap.NeedsReview);
        Assert.Equal(["RN-001"], trap.Items);
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => app.UpdateTrapAsync(trap.Id, new UpdateReverseTrapRequest { Confirm = true }, "dev", Dev, default));
        Assert.False((await app.UpdateTrapAsync(trap.Id, new UpdateReverseTrapRequest { Confirm = true }, "gestor", Approver, default)).NeedsReview);

        var item = Assert.Single(await app.GetItemsAsync(["legado-rca#RN-001"], null, default));
        Assert.Equal("Etapa travada sem aprovador", Assert.Single(item.Traps).Title);
        Assert.Contains("Armadilhas ligadas", await app.ForCardAsync("75100", "A3", "etapa aprovador", default));

        Assert.DoesNotContain("armadilhas", (await arch.GetProjectAsync("legado-rca", default)).SupersededSections.Keys);
        var module = await app.MarkTrapsMigratedAsync("legado-rca", "dev", Dev, default);
        Assert.NotNull(module.TrapsMigratedAt);
        Assert.Contains("armadilhas", module.SupersededSections.Keys);
        var (_, zip) = await arch.ExportAsync(default);
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var reader = new StreamReader(archive.GetEntry("projects/legado-rca/090-armadilhas.md")!.Open());
        var text = await reader.ReadToEndAsync();
        Assert.Contains("Etapa travada sem aprovador", text);
        Assert.DoesNotContain("Sem aprovador a etapa não anda", text); // a seção antiga saiu
    }

    [Fact]
    public async Task Suggestions_carry_the_item_become_traps_and_question_gaps_feed_the_practical_view()
    {
        var (app, arch, repo) = Create(["funcional"]);
        await PublishAsync(app, "funcional", Funcional);
        var s = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", SectionKey = "re-funcional", ItemId = "rn-1", Kind = "learning", Content = "Quando falta aprovador, o botão some\nmais detalhes", CardNumber = "75067" }, "analise", default);
        Assert.Equal("RN-001", s.ItemId);
        var kc = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", Kind = "kc", Content = "ART-21 diz 2 aprovadores; o código exige 1" }, "analise", default);
        Assert.Equal("kc", Assert.Single(await app.KcDivergencesAsync(default)).Kind);

        var trap = await app.SuggestionToTrapAsync(s.Id, null, "gestor", Approver, default);
        Assert.Equal("Quando falta aprovador, o botão some", trap.Title);
        Assert.Equal(["RN-001"], trap.Items);
        Assert.Equal(["75067"], trap.Cards);
        Assert.Equal(ArchitectureSuggestionStatus.Applied, (await repo.GetSuggestionAsync(s.Id, default))!.Status);

        await app.RecordQuestionGapAsync("legado-rca", "Como exportar o A3 em PDF?", "user", default);
        await app.RecordQuestionGapAsync("legado-rca", "Como exportar o A3 em PDF?", "user", default); // não duplica
        var gaps = (await repo.GetSuggestionsAsync(ArchitectureSuggestionStatus.Pending, default)).Where(x => x.SectionKey == "re-pratica").ToList();
        Assert.Single(gaps);

        await arch.RecordQuestionAsync("O A3 é legado ou revamp?", "operation", "not-found", null, null, "user", default);
        var pack = await app.StartSessionAsync("legado-rca", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.Contains(pack.Questions, q => q.Text.Contains("legado ou revamp"));
    }

    [Fact]
    public async Task Each_session_gets_and_resolves_only_the_suggestions_of_its_own_document()
    {
        var (app, arch, repo) = Create();
        await PublishAsync(app, "funcional", Funcional);
        var func = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", SectionKey = "re-funcional", ItemId = "RN-001", Kind = "learning", Content = "aprendizado do funcional" }, "analise", default);
        var old = await arch.SuggestAsync(new CreateArchitectureSuggestionRequest { ProjectKey = "legado-rca", SectionKey = "regras-de-negocio", Kind = "learning", Content = "sugestão da base antiga" }, "analise", default);
        await app.RecordQuestionGapAsync("legado-rca", "Como exportar o A3 em PDF?", "user", default);

        var pack = await app.StartSessionAsync("legado-rca", "pratica", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.DoesNotContain(pack.Suggestions, x => x.Id == func.Id);          // é do funcional: fica para a sessão dele
        Assert.Contains(pack.Suggestions, x => x.Id == old.Id);                 // sem documento da ER: qualquer sessão trata
        var gap = Assert.Single(pack.Suggestions, x => x.SectionKey == "re-pratica");

        // decisão sobre a sugestão de outro documento é ignorada na publicação
        await app.SaveRevisionAsync(pack.Revision.Id, new SaveReverseRevisionRequest
        {
            Content = Practical,
            SuggestionDecisions = [new() { SuggestionId = func.Id, Decision = "applied" }, new() { SuggestionId = gap.Id, Decision = "applied", Items = ["FAQ-001"] }]
        }, "dev", Dev, default);
        await app.SubmitAsync(pack.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(pack.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        Assert.Equal(ArchitectureSuggestionStatus.Pending, (await repo.GetSuggestionAsync(func.Id, default))!.Status);
        Assert.Equal(ArchitectureSuggestionStatus.Applied, (await repo.GetSuggestionAsync(gap.Id, default))!.Status);
    }

    [Fact]
    public async Task The_analysis_gate_uses_only_the_technical_docs()
    {
        var (app, _, _) = Create(["funcional", "pratica"]);
        await PublishAsync(app, "funcional", Funcional);
        Assert.Contains("COMPLETA", await app.ForCardAsync("90010", "A3", "etapa", default));
        Assert.NotNull(await app.CheckGateAsync("90010", "investigar-codigo", "sem citar", default));
    }
}
