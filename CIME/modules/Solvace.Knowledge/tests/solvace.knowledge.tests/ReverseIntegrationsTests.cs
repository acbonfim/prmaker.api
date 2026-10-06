using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0066: integrações da engenharia reversa com destino validado, tipo pelo mecanismo e itens no mapa.</summary>
public class ReverseIntegrationsTests
{
    private static readonly IntegrationTarget[] Targets =
    [
        new("legado-checklist", ["Legado — Checklist (CHK)", "Checklist"]),
        new("legado-actionplan", ["Legado — Plano de Ação", "Plano de Ação"]),
        new("revamp-actionplan", ["Revamp — Action Plan", "Plano de Ação"]),
        new("revamp-users", ["Revamp — Users", "Usuários", "usuarios novo"])
    ];

    [Fact]
    public void Free_text_in_modules_keeps_only_known_keys_and_marks_to_confirm()
    {
        var (resolved, unresolved, confirm) = ReverseIntegrations.ResolveTargets(
            "legado-actionplan (a confirmar a chave; projeto `solvace-core/action_plan`, sigla ACP)", "legado-checklist", Targets);
        Assert.Equal(["legado-actionplan"], resolved);
        Assert.Empty(unresolved);
        Assert.True(confirm);

        (resolved, unresolved, _) = ReverseIntegrations.ResolveTargets(
            "a confirmar (Digital Obeya, helpers (S3FileUploadHelper) — infraestrutura AWS", "legado-checklist", Targets);
        Assert.Empty(resolved);
        Assert.Single(unresolved);

        (resolved, unresolved, _) = ReverseIntegrations.ResolveTargets("Usuários, ext:redis, `revamp-actionplan`", "legado-checklist", Targets);
        Assert.Equal(["revamp-users", "ext:redis", "revamp-actionplan"], resolved);
        Assert.Empty(unresolved);
    }

    [Fact]
    public void Same_name_in_both_worlds_resolves_to_the_world_of_the_source_or_stays_unresolved()
    {
        Assert.Equal(["legado-actionplan"], ReverseIntegrations.ResolveTargets("Plano de Ação (legado)", "legado-checklist", Targets).Resolved);
        Assert.Equal(["revamp-actionplan"], ReverseIntegrations.ResolveTargets("Plano de Ação", "revamp-users", Targets).Resolved);
        Assert.Single(ReverseIntegrations.ResolveTargets("Plano de Ação", "ecossistema", Targets).Unresolved);
    }

    [Theory]
    [InlineData("pacote compartilhado `helpers` lendo o banco local da planta (views/tabelas ACP).", "package")]
    [InlineData("HTTP do navegador (JS `RevampActionPlanRequest`) + leitura no banco global", "http")]
    [InlineData("fila (SQS) `NOTIFICATION_WORKER_<amb>`", "queue")]
    [InlineData("evento (SNS) tópico USER", "event")]
    [InlineData("cache (Redis) chave `chk:{site}`", "cache")]
    [InlineData("job do SQL Agent `JOB_CHK_GERA_INSPECAO`", "job")]
    [InlineData("arquivo/S3 bucket de anexos", "storage")]
    [InlineData("trigger `TR_CHK_INSPECTION` na tabela", "trigger")]
    public void Kind_comes_from_the_first_mechanism_word(string mechanism, string kind) =>
        Assert.Equal(kind, ReverseIntegrations.KindOf(mechanism));

    [Fact]
    public void Old_items_without_mechanism_fall_back_to_the_body()
    {
        var i = ReverseIntegrations.Read("revamp-kaizen", "INT-001", "Kaizen → Users", "### INT-001 — Kaizen → Users\n- **Módulos:** revamp-users\nvia HTTP GET /users", Targets);
        Assert.Equal("http", i.Kind);
        Assert.Equal(["revamp-users"], i.Targets);
        Assert.Null(i.Mechanism);
    }

    private sealed class KcSettings : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", KnowledgeFilterOptions.None));
    }

    private const string Arquitetura = """
        ## Integrações
        ### INT-001 — legado-checklist → Plano de Ação: lista os planos do checklist
        - **Módulos:** legado-actionplan
        - **Mecanismo:** pacote compartilhado `helpers` lendo o banco local
        - **Contrato:** `ActionPlanHelper.ListPlansByApplicationId`
        - **Onde:** `Controllers/ChecklistController.cs:650`
        ### INT-002 — legado-checklist → ?: a confirmar
        - **Módulos:** a confirmar (Digital Obeya
        - **Onde:** `Views/x.cshtml:1`
        ### INT-003 — legado-checklist → Redis: cache dos menus
        - **Módulos:** ext:redis
        - **Mecanismo:** cache (Redis)
        - **Onde:** `Services/MenuCache.cs:12`
        """;

    private static async Task<(InMemoryKnowledgeRepository Repo, ArchitectureApplication Arch)> SeedAsync()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, kind, name) in new[] { ("legado-checklist", "legacy", "Legado — Checklist (CHK)"), ("legado-actionplan", "legacy", "Legado — Plano de Ação") })
        {
            var p = new ArchitectureProject(key, "admin", now);
            p.Update(name, kind, null, "resumo", ["kw"], null, null, null, "admin", now);
            repo.AddProject(p);
        }
        // relação gravada por uma publicação antiga (destino em texto livre) — não pode aparecer mais
        var checklist = (await repo.GetProjectsAsync(default)).Single(p => p.Key == "legado-checklist");
        checklist.SetRelations([
            new ArchitectureRelation { Target = "a", Kind = "other", Detail = "antiga", Evidence = "re#INT-002" },
            new ArchitectureRelation { Target = "legado-actionplan", Kind = "database", Detail = "VW_ACP_PLAN", Evidence = "helpers/ActionPlanHelper.cs:200" }
        ]);
        await ReverseEngineeringApplication.ReplaceIndexAsync(repo, "legado-checklist", "arquitetura", ReverseDocParser.Parse(Arquitetura), 1, now, default);
        await repo.SaveChangesAsync(default);
        ReverseRelations.Invalidate();
        return (repo, new ArchitectureApplication(repo, new KcSettings()));
    }

    [Fact]
    public async Task Map_uses_resolved_integrations_with_their_items_and_drops_old_free_text_relations()
    {
        var (_, arch) = await SeedAsync();
        var graph = await arch.GetGraphAsync(default);

        Assert.DoesNotContain(graph.Nodes, n => n.Key == "a");
        var package = Assert.Single(graph.Edges, e => e.Source == "legado-checklist" && e.Target == "legado-actionplan" && e.Kind == "package");
        Assert.Equal("engenharia", package.Origin);
        var item = Assert.Single(package.Items);
        Assert.Equal("legado-checklist#INT-001", item.Ref);
        Assert.Contains("helpers", item.Mechanism);
        // a relação do extrator continua, com a origem "base"
        Assert.Contains(graph.Edges, e => e.Target == "legado-actionplan" && e.Kind == "database" && e.Origin == "base");
        var cache = Assert.Single(graph.Edges, e => e.Target == "ext:redis");
        Assert.Equal("cache", cache.Kind);
        Assert.Contains(graph.Nodes, n => n.Key == "ext:redis" && n.Name.Contains("Redis"));

        var project = await arch.GetProjectAsync("legado-actionplan", default);
        Assert.Contains(project.UsedBy, r => r.Source == "legado-checklist" && r.Kind == "package" && r.Evidence == "re#INT-001");
    }

    [Fact]
    public async Task Lint_warns_about_unknown_module_and_missing_mechanism_without_blocking()
    {
        var (repo, _) = await SeedAsync();
        var targets = ReverseRelations.Targets(await repo.GetProjectsAsync(default), await repo.GetReverseModulesAsync(default));
        var lint = ReverseLint.Run(ReverseDocTypes.Get("arquitetura"), Arquitetura, moduleKey: "legado-checklist", targets: targets);
        Assert.Contains(lint.Warnings, w => w.Contains("não reconhecido") && w.Contains("INT-002"));
        Assert.Contains(lint.Warnings, w => w.Contains("sem **Mecanismo:**") && w.Contains("INT-002"));
        Assert.DoesNotContain(lint.Errors, e => e.Contains("INT-"));
    }

    [Fact]
    public void Republishing_drops_the_relations_the_old_publication_wrote()
    {
        var p = new ArchitectureProject("legado-checklist", "a", DateTimeOffset.UtcNow);
        p.SetRelations([
            new ArchitectureRelation { Target = "a", Kind = "other", Evidence = "re#INT-002" },
            new ArchitectureRelation { Target = "legado-actionplan", Kind = "database", Evidence = "x.cs:1" }
        ]);
        ReverseEngineeringApplication.DropReverseRelations(p);
        Assert.Equal("legado-actionplan", Assert.Single(p.Relations).Target);
    }
}
