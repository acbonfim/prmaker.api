using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>Feature 0060: o card chega na engenharia reversa certa (caso real do card 75294 — Checklist legado, tela em inglês).</summary>
public class ReverseCardMatchTests
{
    private static readonly string[] Approver = ["gestor"];
    private static readonly string[] Dev = ["user"];

    private sealed class Settings(ReverseSettings value) : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private const string ChecklistLegado = """
        # Levantamento funcional — Checklist
        ## Resumo do módulo
        Checklists e inspeções.
        ## Perfis e permissões
        ## Funcionalidades
        ## Casos de uso
        ## Regras de negócio
        ### RN-343 — Cumprimento por Checklist: período por data de criação da inspeção
        - **Onde:** `Services/ReportService.cs:288`
        Os campos de data usam o calendário (daterangepicker) do filtro.
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ### REL-031 — Cumprimento por Checklist (menu Analytics)
        - **Onde:** `Controllers/ReportController.cs:215`
        Filtro de período com calendário; filtros salvos.
        ## Integrações com outros módulos
        ## Glossário
        ### GLO-001 — Cumprimento por Checklist
        - **Sinônimos:** Compliance per Checklist, Cumplimiento por Checklist
        Relatório do menu Analytics.
        ## Lacunas e pontos a confirmar
        """;

    private static async Task<(ReverseEngineeringApplication, InMemoryKnowledgeRepository)> CreateAsync()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, kind, area, name, kw) in new[]
                 {
                     ("revamp-actionplan", "revamp", "Plano de Ação", "Plano de Ação", "Checklist"),
                     ("revamp-checklist", "revamp", "Checklists", "Checklist", "checklist"),
                     ("revamp-rca", "revamp", "Análise e Resolução de Problemas", "RCA", "Checklist"),
                     ("revamp-centerline", "revamp", "Checklists", "Centerline", "centerline"),
                     ("legado-centerline", "legacy", "Checklists", "Centerline legado", "centerline"),
                     ("legado-checklist", "legacy", "Checklists", "Legado — Checklist (CHK)", "CHK")
                 })
        {
            var p = new ArchitectureProject(key, "admin", now);
            p.Update(name, kind, null, "resumo", [kw], null, null, null, "admin", now);
            p.SetFriendly(name, null, area);
            repo.AddProject(p);
        }
        var app = new ReverseEngineeringApplication(repo, new Settings(ReverseSettings.Default with { RequiredDocs = ["funcional"] }));
        await app.UpsertModuleAsync("legado-checklist", new UpsertReverseModuleRequest { Aliases = ["Checklist (legado)"] }, "gestor", Approver, default);
        var session = await app.StartSessionAsync("legado-checklist", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = ChecklistLegado, CoverageRatio = 0.95 }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        return (app, repo);
    }

    [Fact]
    public async Task Card_75294_module_Checklist_reaches_the_legacy_reverse_engineering_and_turns_the_gate_on()
    {
        var (app, repo) = await CreateAsync();
        var text = await app.ForCardAsync("75294", "Checklist",
            "Astellas - dublin- Calendar error - checklist analytics Go to Compliance per Checklist Click on the filter click on the calendar", default);

        Assert.StartsWith("Módulo: legado-checklist", text.Split('\n')[1]);
        Assert.Contains("COMPLETA", text);
        Assert.Contains("legado-checklist#REL-031", text);
        Assert.DoesNotContain("use a base antiga", text);
        Assert.DoesNotContain("revamp-actionplan", text);  // palavra-chave de outra área não puxa mais o módulo
        Assert.DoesNotContain("revamp-rca", text);
        Assert.True((await repo.GetCardContextAsync("75294", false, default))!.Complete);
        Assert.NotNull(await app.CheckGateAsync("75294", "investigar-codigo", "olhei o código", default));
    }

    [Fact]
    public async Task Explicit_world_in_the_field_is_respected()
    {
        var (app, _) = await CreateAsync();
        var text = await app.ForCardAsync("75295", "Checklist (Revamp)", "Compliance per Checklist calendar", default);
        Assert.Contains("revamp-checklist", text);
        Assert.DoesNotContain("Módulo: legado-checklist", text);
    }

    [Fact]
    public async Task English_screen_name_finds_the_item_through_the_glossary_synonyms()
    {
        var (app, _) = await CreateAsync();
        var hits = await app.SearchAsync("Compliance per Checklist", ["legado-checklist"], null, null, 5, false, default);
        Assert.Contains(hits, h => h.Ref is "legado-checklist#REL-031" or "legado-checklist#GLO-001");
    }

    [Fact]
    public void Alias_without_the_world_word_is_a_strong_match()
    {
        var now = DateTimeOffset.UtcNow;
        var projects = new List<ArchitectureProject>();
        foreach (var (key, kind, area) in new[] { ("revamp-x", "revamp", "Outra"), ("legado-chk", "legacy", "Checklists") })
        {
            var p = new ArchitectureProject(key, "a", now);
            p.Update(key, kind, null, null, ["Checklist"], null, null, null, "a", now);
            p.SetFriendly(area, null, area);
            projects.Add(p);
        }
        var module = new ReverseModule("legado-chk", "a", now);
        module.Update(null, ["Checklist (legado)"], null, "a", now);
        Assert.Equal(["legado-chk"], ReverseEngineeringApplication.MatchModules("Checklist", projects, [module]));
    }
}
