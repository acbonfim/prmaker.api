using System.IO.Compression;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>
/// 0074: a engenharia reversa é só de administrador. Quem não é admin não lê as seções <c>re-*</c> que ela publica na Base
/// (nem pela seção, nem pelo espelho, índice, catálogo ou busca), não recebe texto dela no "Pergunte" e a trava da análise
/// não vale para ele (ele não consegue consultar). O restante da Base continua igual.
/// </summary>
public class ReverseAccess0074Tests
{
    private static readonly string[] Approver = ["gestor"];
    private static readonly string[] Dev = ["user"];

    private sealed class Access(bool canRead) : IReverseAccess
    {
        public bool CanRead => canRead;
    }

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

    private sealed record World(InMemoryKnowledgeRepository Repo, ReverseEngineeringApplication AdminReverse, ArchitectureApplication AdminArch,
        ReverseEngineeringApplication UserReverse, ArchitectureApplication UserArch);

    private static async Task<World> CreateAsync()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        var p = new ArchitectureProject("legado-rca", "admin", now);
        p.Update("A3 legado", "legacy", null, "resumo", ["A3"], null, null, null, "admin", now);
        p.SetFriendly("A3", null, "RCA");
        repo.AddProject(p);
        var settings = new Settings(ReverseSettings.Default with { RequiredDocs = ["funcional"] });
        var world = new World(repo,
            new ReverseEngineeringApplication(repo, settings), new ArchitectureApplication(repo, new Kc(), settings),
            new ReverseEngineeringApplication(repo, settings, new Access(false)), new ArchitectureApplication(repo, new Kc(), settings, new Access(false)));

        // uma seção comum da Base (continua legível para todos) e o documento da engenharia reversa publicado por admin
        await world.AdminArch.WriteSectionAsync("legado-rca", "modulos", new WriteArchitectureSectionRequest { Content = "telas do A3 e o aprovador", Source = "admin" }, "admin", default);
        var session = await world.AdminReverse.StartSessionAsync("legado-rca", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await world.AdminReverse.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = Funcional, CoverageRatio = 0.95 }, "dev", Dev, default);
        await world.AdminReverse.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await world.AdminReverse.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
        return world;
    }

    [Fact]
    public async Task Admin_reads_the_reverse_section_and_non_admin_gets_forbidden_on_every_read()
    {
        var w = await CreateAsync();

        Assert.Contains("RN-001", (await w.AdminArch.GetSectionAsync("legado-rca", "re-funcional", default)).Content);

        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetSectionAsync("legado-rca", "re-funcional", default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetSectionAsync("legado-rca", "RE-Funcional", default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetSectionExcerptAsync("legado-rca", "re-funcional", 50, null, default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetSectionOutlineAsync("legado-rca", "re-funcional", default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetSectionPartsAsync("legado-rca", "re-funcional", 0, 0, default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetVersionsAsync("legado-rca", "re-funcional", default));
        await Assert.ThrowsAsync<ReverseForbiddenException>(() => w.UserArch.GetVersionAsync("legado-rca", "re-funcional", 1, default));
    }

    [Fact]
    public async Task The_rest_of_the_base_stays_readable_for_non_admin()
    {
        var w = await CreateAsync();

        var section = await w.UserArch.GetSectionAsync("legado-rca", "modulos", default);
        Assert.Contains("telas do A3", section.Content);
        Assert.NotEmpty((await w.UserArch.GetSectionOutlineAsync("legado-rca", "modulos", default)).Chunks);
    }

    [Fact]
    public async Task The_project_and_the_project_list_hide_the_reverse_sections_from_non_admin_only()
    {
        var w = await CreateAsync();

        Assert.Contains((await w.AdminArch.GetProjectAsync("legado-rca", default)).Sections, s => s.Key == "re-funcional");
        Assert.Contains((await w.AdminArch.ListProjectsAsync(default)).Single().Sections, s => s.Key == "re-funcional");

        var project = await w.UserArch.GetProjectAsync("legado-rca", default);
        Assert.DoesNotContain(project.Sections, s => s.Key.StartsWith("re-", StringComparison.Ordinal));
        Assert.Contains(project.Sections, s => s.Key == "modulos");
        var listed = (await w.UserArch.ListProjectsAsync(default)).Single();
        Assert.DoesNotContain(listed.Sections, s => s.Key.StartsWith("re-", StringComparison.Ordinal));

        // esconder da resposta não apaga nada: o admin continua vendo depois
        Assert.Contains((await w.AdminArch.GetProjectAsync("legado-rca", default)).Sections, s => s.Key == "re-funcional");
    }

    [Fact]
    public async Task The_mirror_index_catalog_and_search_leave_the_reverse_engineering_out_for_non_admin()
    {
        var w = await CreateAsync();

        var (adminManifest, adminZip) = await w.AdminArch.ExportAsync(default);
        var (userManifest, userZip) = await w.UserArch.ExportAsync(default);
        using (var admin = new ZipArchive(new MemoryStream(adminZip)))
            Assert.Contains(admin.Entries, e => e.FullName.Contains("re-funcional", StringComparison.Ordinal));
        using (var user = new ZipArchive(new MemoryStream(userZip)))
        {
            Assert.DoesNotContain(user.Entries, e => e.FullName.Contains("re-funcional", StringComparison.Ordinal));
            Assert.Contains(user.Entries, e => e.FullName.Contains("modulos", StringComparison.Ordinal));
        }
        Assert.NotEqual(adminManifest.Hash, userManifest.Hash);                       // cada um baixa só o que pode ler
        Assert.Equal(userManifest.Hash, (await w.UserArch.GetManifestAsync(default)).Hash);   // o manifesto avulso bate com o do pacote

        Assert.Contains("RE 1/", await w.AdminArch.BuildIndexAsync(default));          // o índice marca "RE n/m" quando há documento da engenharia reversa
        Assert.DoesNotContain("RE 1/", await w.UserArch.BuildIndexAsync(default));
        Assert.DoesNotContain("engenharia reversa", await w.UserArch.BuildIndexAsync(default), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("re-funcional", await w.AdminArch.BuildCatalogAsync(4000, default));
        Assert.DoesNotContain("re-funcional", await w.UserArch.BuildCatalogAsync(4000, default));

        // o índice por item (reverse/*.tsv, reverse/INDEX.md) é a engenharia reversa por dentro: só no pacote do admin
        using (var admin = new ZipArchive(new MemoryStream(adminZip)))
            Assert.Contains(admin.Entries, e => e.FullName.StartsWith("reverse/", StringComparison.Ordinal));
        using (var user = new ZipArchive(new MemoryStream(userZip)))
        {
            Assert.DoesNotContain(user.Entries, e => e.FullName.StartsWith("reverse/", StringComparison.Ordinal));
            foreach (var entry in user.Entries.Where(e => e.FullName.EndsWith(".md") || e.FullName.EndsWith(".json")))
            {
                using var reader = new StreamReader(entry.Open());
                var text = await reader.ReadToEndAsync();
                Assert.DoesNotContain("RN-001", text);                                  // nem o título de um item da engenharia reversa
            }
        }

        var adminHits = await w.AdminArch.SearchAsync("aprovador etapa", 10, null, null, null, default);
        var userHits = await w.UserArch.SearchAsync("aprovador etapa", 10, null, null, null, default);
        Assert.Contains(adminHits, h => h.SectionKey == "re-funcional");
        Assert.DoesNotContain(userHits, h => h.SectionKey is { } k && k.StartsWith("re-", StringComparison.Ordinal));
        Assert.Contains(userHits, h => h.SectionKey == "modulos");                    // o texto comum da Base ainda aparece
    }

    [Fact]
    public async Task The_ask_context_carries_no_reverse_engineering_text_for_non_admin()
    {
        var w = await CreateAsync();

        var admin = await w.AdminReverse.GetAnswerContextAsync("a3 passa de etapa com aprovador", ["aprovador"], [], ArchitectureQuestionKind.Rule, 7, 6, 1500, default);
        var user = await w.UserReverse.GetAnswerContextAsync("a3 passa de etapa com aprovador", ["aprovador"], [], ArchitectureQuestionKind.Rule, 7, 6, 1500, default);

        Assert.NotEmpty(admin);
        Assert.Empty(user);
    }

    [Fact]
    public async Task The_analysis_gate_does_not_apply_to_someone_who_cannot_consult_the_reverse_engineering()
    {
        var w = await CreateAsync();
        // o card do módulo A3 (engenharia reversa completa) liga a trava para quem consulta
        await w.AdminReverse.ForCardAsync("75001", "A3", "A3 não passa de etapa", default);

        Assert.NotNull(await w.AdminReverse.CheckGateAsync("75001", "investigar-codigo", "olhei o código", default));
        Assert.Null(await w.UserReverse.CheckGateAsync("75001", "investigar-codigo", "olhei o código", default));
    }

    [Fact]
    public async Task Old_sections_replaced_by_the_reverse_engineering_stay_visible_for_non_admin_only()
    {
        var w = await CreateAsync();
        // "regras-de-negocio" é coberta pelo documento funcional publicado: para o admin sai do espelho/busca; para os demais fica
        await w.AdminArch.WriteSectionAsync("legado-rca", "regras-de-negocio", new WriteArchitectureSectionRequest { Content = "regra antiga do A3 sobre aprovador", Source = "admin" }, "admin", default);

        var adminProject = await w.AdminArch.GetProjectAsync("legado-rca", default);
        var userProject = await w.UserArch.GetProjectAsync("legado-rca", default);
        Assert.Contains("regras-de-negocio", adminProject.SupersededSections.Keys);
        Assert.Empty(userProject.SupersededSections);

        var (_, adminZip) = await w.AdminArch.ExportAsync(default);
        var (_, userZip) = await w.UserArch.ExportAsync(default);
        using (var admin = new ZipArchive(new MemoryStream(adminZip)))
            Assert.DoesNotContain(admin.Entries, e => e.FullName.EndsWith("-regras-de-negocio.md", StringComparison.Ordinal));
        using (var user = new ZipArchive(new MemoryStream(userZip)))
            Assert.Contains(user.Entries, e => e.FullName.EndsWith("-regras-de-negocio.md", StringComparison.Ordinal));
    }
}
