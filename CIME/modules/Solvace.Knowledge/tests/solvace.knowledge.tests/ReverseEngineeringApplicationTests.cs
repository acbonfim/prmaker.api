using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

public class ReverseEngineeringApplicationTests
{
    private static readonly string[] Approver = ["gestor"];
    private static readonly string[] Dev = ["user"];

    private sealed class Settings(ReverseSettings value) : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class KcSettings : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", KnowledgeFilterOptions.None));
    }

    private static (ReverseEngineeringApplication App, InMemoryKnowledgeRepository Repo) Create(IReadOnlyList<string>? required = null)
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, kind, area, name) in new[]
                 {
                     ("revamp-kaizen", "revamp", "Kaizen", "Kaizen"), ("legado-kaizen", "legacy", "Kaizen", "Kaizen legado"),
                     ("revamp-users", "revamp", "Usuários", "Usuários"), ("ecossistema", "ecosystem", null, "Ecossistema")
                 })
        {
            var p = new ArchitectureProject(key, "admin", now);
            p.Update(name, kind, $"https://github.com/electradv/{key}.git", "resumo", ["kw"], null, null, null, "admin", now);
            p.SetFriendly(name, null, area);
            repo.AddProject(p);
        }
        var settings = ReverseSettings.Default with { RequiredDocs = required ?? ["funcional"] };
        return (new ReverseEngineeringApplication(repo, new Settings(settings)), repo);
    }

    private const string Funcional = """
        # Levantamento funcional — Kaizen
        ## Resumo do módulo
        Ideias.
        ## Perfis e permissões
        ### PRF-001 — Aprovador
        ## Funcionalidades
        ### FN-001 — Registrar ideia
        ## Casos de uso
        ### UC-001 — Colaborador registra ideia
        - **Onde:** `KaizenService.cs:10`
        ## Regras de negócio
        ### RN-001 — Etapa só avança com aprovador definido
        - **Onde:** `KaizenService.cs:88`
        - **Tabelas:** TB_MLH_MELHORIA
        - **Módulos:** revamp-users
        - **Tags:** etapa, avançar, aprovador
        Mensagem "Informe o aprovador".
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ## Integrações com outros módulos
        ## Glossário
        ## Lacunas e pontos a confirmar
        """;

    private static async Task<Guid> PublishFuncionalAsync(ReverseEngineeringApplication app, string content = Funcional)
    {
        var session = await app.StartSessionAsync("revamp-kaizen", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = content, Summary = "primeira", CoverageRatio = 0.95 }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        return session.Revision.Id;
    }

    [Fact]
    public async Task Session_review_and_publish_create_the_section_and_the_item_index()
    {
        var (app, repo) = Create();
        var id = await PublishFuncionalAsync(app);

        var project = (await repo.GetProjectsAsync(default)).Single(p => p.Key == "revamp-kaizen");
        var section = Assert.Single(project.Sections, s => s.Key == "re-funcional");
        Assert.Equal(110, section.Order);
        Assert.Equal(ArchitectureSectionAudience.Llm, section.Audience);

        var entries = await repo.GetIndexEntriesAsync("revamp-kaizen", default);
        Assert.Equal(["PRF-001", "FN-001", "UC-001", "RN-001"], entries.Select(e => e.ItemId));

        var modules = await app.ListModulesAsync(default);
        Assert.DoesNotContain(modules, m => m.Key == "ecossistema");
        var kaizen = modules.Single(m => m.Key == "revamp-kaizen");
        Assert.True(kaizen.Complete);
        Assert.Equal("published", kaizen.Docs.Single(d => d.Type == "funcional").State);

        var hits = await app.SearchAsync("aprovador etapa", null, null, null, 10, false, default);
        Assert.Equal("revamp-kaizen#RN-001", hits[0].Ref);
        var exact = await app.SearchAsync("RN-001", ["revamp-kaizen"], null, null, 10, false, default);
        Assert.Equal("revamp-kaizen#RN-001", Assert.Single(exact).Ref);

        var item = Assert.Single(await app.GetItemsAsync(["rn-1"], "revamp-kaizen", default));
        Assert.Contains("Informe o aprovador", item.Body);

        var impact = await app.ImpactAsync("tb_mlh_melhoria", 10, default);
        Assert.Equal("revamp-kaizen#RN-001", Assert.Single(impact).Ref);
        var byModule = await app.ImpactAsync("revamp-users", 10, default);
        Assert.Single(byModule);
    }

    [Fact]
    public async Task Improve_session_starts_from_published_and_publishing_again_supersedes()
    {
        var (app, repo) = Create();
        var first = await PublishFuncionalAsync(app);
        var session = await app.StartSessionAsync("revamp-kaizen", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        Assert.Equal("improve", session.Revision.Mode);
        Assert.Equal(Funcional.Trim(), session.Revision.Content.Trim());
        Assert.Equal(2, session.Revision.Number);

        // Sumiu RN-001 → aviso; mudou UC-001 → diff
        var changed = Funcional.Replace("### RN-001 — Etapa só avança com aprovador definido", "### RN-002 — Outra regra")
            .Replace("Colaborador registra ideia", "Colaborador registra ideia com anexo");
        var saved = await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = changed }, "dev", Dev, default);
        Assert.Contains("RN-001", saved.Lint!.RemovedIds);
        var revision = await app.GetRevisionAsync(session.Revision.Id, Approver, default);
        Assert.Equal(1, revision.Diff!.Added);
        Assert.Equal(1, revision.Diff.Removed);
        Assert.Equal(1, revision.Diff.Changed);

        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        Assert.Equal(ReverseRevisionStatus.Superseded, (await repo.GetRevisionAsync(first, false, default))!.Status);
        Assert.Contains("RN-002", (await repo.GetIndexEntriesAsync("revamp-kaizen", default)).Select(e => e.ItemId));
    }

    [Fact]
    public async Task Only_approvers_review_publish_and_configure()
    {
        var (app, _) = Create();
        var session = await app.StartSessionAsync("revamp-kaizen", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = Funcional }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => app.ReviewAsync(session.Revision.Id, new ReviewReverseRevisionRequest { Action = "approve" }, "dev", Dev, default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "dev", Dev, default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => app.UpsertModuleAsync("revamp-kaizen", new UpsertReverseModuleRequest(), "dev", Dev, default));
        // Outro usuário (não aprovador) não mexe numa revisão enviada por alguém
        await Assert.ThrowsAsync<DomainException>(() => app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = "x" }, "outro", Dev, default));
    }

    [Fact]
    public async Task Submit_is_blocked_by_lint_errors()
    {
        var (app, _) = Create();
        var session = await app.StartSessionAsync("revamp-kaizen", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = "## Regras de negócio\nsem itens" }, "dev", Dev, default);
        var error = await Assert.ThrowsAsync<DomainException>(() => app.SubmitAsync(session.Revision.Id, "dev", Dev, default));
        Assert.Contains("Faltam seções", error.Message);
    }

    [Fact]
    public async Task For_card_matches_the_module_field_registers_the_card_and_the_gate_requires_consulting()
    {
        var (app, repo) = Create();
        await PublishFuncionalAsync(app);

        var text = await app.ForCardAsync("75067", "Kaizen", "não é possível passar a etapa da ideia aprovador", default);
        Assert.Contains("revamp-kaizen", text);
        Assert.Contains("COMPLETA", text);
        Assert.Contains("revamp-kaizen#RN-001", text);
        Assert.Contains("Informe o aprovador", text);
        Assert.Contains("legado-kaizen", text); // par da mesma área

        var ctx = await repo.GetCardContextAsync("75067", false, default);
        Assert.True(ctx!.Complete);

        var block = await app.CheckGateAsync("75067", "investigar-codigo", "olhei o código do service", default);
        Assert.NotNull(block);
        Assert.Null(await app.CheckGateAsync("75067", "coletar-dados", "x", default));
        Assert.Null(await app.CheckGateAsync("75067", "investigar-codigo", "causa: RN-001 não valida no front", default));
        Assert.Null(await app.CheckGateAsync("75067", "investigar-codigo", "sem citar", default)); // já consultou

        var other = await app.ForCardAsync("75068", "Users (Revamp)", "x", default);
        Assert.Contains("revamp-users", other);
        Assert.DoesNotContain("COMPLETA", other);
        Assert.Null(await app.CheckGateAsync("75068", "investigar-codigo", "sem citar", default));
    }

    [Fact]
    public async Task Gate_is_satisfied_by_consulting_items_with_the_card()
    {
        var (app, _) = Create();
        await PublishFuncionalAsync(app);
        await app.ForCardAsync("80000", "Kaizen", "outra coisa", default);
        Assert.NotNull(await app.CheckGateAsync("80000", "investigar-codigo", null, default));
        await app.RecordConsultedAsync("80000", ["revamp-kaizen#RN-001"], default);
        Assert.Null(await app.CheckGateAsync("80000", "investigar-codigo", null, default));
    }

    [Theory]
    [InlineData("Kaizen", new[] { "revamp-kaizen", "legado-kaizen" })]
    [InlineData("Kaizen (Revamp)", new[] { "revamp-kaizen" })]
    [InlineData("Kaizen Legado", new[] { "legado-kaizen" })]
    [InlineData("Users", new[] { "revamp-users" })]
    [InlineData("Inexistente", new string[0])]
    public void Matches_modules_from_the_card_field(string field, string[] expected)
    {
        var now = DateTimeOffset.UtcNow;
        var projects = new List<ArchitectureProject>();
        foreach (var (key, kind, area) in new[] { ("revamp-kaizen", "revamp", "Kaizen"), ("legado-kaizen", "legacy", "Kaizen"), ("revamp-users", "revamp", "Usuários") })
        {
            var p = new ArchitectureProject(key, "a", now);
            p.Update(key, kind, null, null, null, null, null, null, "a", now);
            p.SetFriendly(area, null, area);
            projects.Add(p);
        }
        Assert.Equal(expected, ReverseEngineeringApplication.MatchModules(field, projects, []));
    }

    [Fact]
    public async Task Alias_wins_and_architecture_integrations_become_relations()
    {
        var (app, repo) = Create();
        await app.UpsertModuleAsync("revamp-users", new UpsertReverseModuleRequest { Aliases = ["Usuarios Novo"] }, "gestor", Approver, default);
        Assert.Equal(["revamp-users"], ReverseEngineeringApplication.MatchModules("usuarios novo", (await repo.GetProjectsAsync(default)).ToList(),
            await repo.GetReverseModulesAsync(default)));

        var project = (await repo.GetProjectsAsync(default)).Single(p => p.Key == "revamp-kaizen");
        project.SetRelations([new ArchitectureRelation { Target = "revamp-post", Kind = "http", Evidence = "a.cs:1" }]);
        var items = ReverseDocParser.Parse("### INT-001 — Kaizen → Users: busca aprovador\n- **Módulos:** revamp-users\nvia HTTP GET /users");
        ReverseEngineeringApplication.MergeIntegrations(project, items);
        Assert.Equal(2, project.Relations.Count);
        Assert.Contains(project.Relations, r => r.Target == "revamp-users" && r.Kind == "http" && r.Evidence == "re#INT-001");
        ReverseEngineeringApplication.MergeIntegrations(project, []);
        Assert.Single(project.Relations);
    }

    [Fact]
    public async Task Editing_a_re_section_in_the_base_reindexes_and_the_mirror_exports_the_tsv()
    {
        var (app, repo) = Create();
        await PublishFuncionalAsync(app);
        var arch = new ArchitectureApplication(repo, new KcSettings());
        await arch.WriteSectionAsync("revamp-kaizen", "re-funcional", new WriteArchitectureSectionRequest
        {
            Content = Funcional + "\n### GAP-001 — Prazo da etapa a confirmar\n", Source = "admin"
        }, "admin", default);
        Assert.Contains("GAP-001", (await repo.GetIndexEntriesAsync("revamp-kaizen", default)).Select(e => e.ItemId));

        var (_, zip) = await arch.ExportAsync(default);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        Assert.NotNull(archive.GetEntry("reverse/INDEX.md"));
        using var reader = new StreamReader(archive.GetEntry("reverse/revamp-kaizen.tsv")!.Open());
        var tsv = await reader.ReadToEndAsync();
        Assert.Contains("RN-001\tRN\tfuncional\tEtapa só avança com aprovador definido\tTB_MLH_MELHORIA", tsv);
        var index = await arch.BuildIndexAsync(default);
        Assert.Contains("RE 1/6", index);
    }
}
