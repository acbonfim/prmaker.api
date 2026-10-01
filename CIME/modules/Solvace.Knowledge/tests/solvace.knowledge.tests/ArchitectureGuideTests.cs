using System.IO.Compression;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Requests;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0038: o Guia (público human) fica fora do espelho das skills; os dados amigáveis também.</summary>
public class ArchitectureGuideTests
{
    [Fact]
    public void Audience_defaults_by_key_and_validates()
    {
        var id = Guid.NewGuid();
        Assert.Equal("llm", new ArchitectureSection(id, "visao-geral").Audience);
        Assert.Equal("human", new ArchitectureSection(id, "guia-o-que-e").Audience);
        Assert.Equal("llm", new ArchitectureSection(id, "guia-o-que-e", "llm").Audience);
        var s = new ArchitectureSection(id, "regras");
        Assert.False(s.SetAudience(null));
        Assert.True(s.SetAudience("HUMAN"));
        Assert.Throws<DomainException>(() => s.SetAudience("todos"));
    }

    [Fact]
    public void Friendly_fields_null_keeps_and_empty_clears()
    {
        var p = new ArchitectureProject("revamp-actionplan", "teste", DateTimeOffset.UtcNow);
        Assert.True(p.SetFriendly("Plano de Ação", "Acompanha as ações de melhoria.", "Plano de Ação"));
        Assert.False(p.SetFriendly(null, null, null));
        Assert.Equal("Plano de Ação", p.DisplayName);
        Assert.True(p.SetFriendly(null, "", null));
        Assert.Null(p.Tagline);
        Assert.Throws<DomainException>(() => p.SetFriendly(new string('x', ArchitectureProject.MaxDisplayNameLength + 1), null, null));
    }

    [Fact]
    public async Task Guide_and_friendly_fields_do_not_reach_the_skill_mirror()
    {
        var repo = new InMemoryRepository();
        var app = new ArchitectureApplication(repo, new Settings());
        await app.UpsertProjectAsync("revamp-actionplan", new UpsertArchitectureProjectRequest { Name = "Revamp — Action Plan", Kind = "revamp", Summary = "Plano de ação." }, "t", default);
        await app.WriteSectionAsync("revamp-actionplan", "visao-geral", new WriteArchitectureSectionRequest { Title = "Visão geral", Content = "TB_ACP_PLAN", Order = 10 }, "t", default);
        var before = await app.GetManifestAsync(default);
        var indexBefore = await app.BuildIndexAsync(default);

        var guide = await app.WriteSectionAsync("revamp-actionplan", "guia-o-que-e", new WriteArchitectureSectionRequest { Title = "O que é", Content = "Serve para acompanhar ações." }, "t", default);
        await app.UpsertProjectAsync("revamp-actionplan", new UpsertArchitectureProjectRequest { Name = "Revamp — Action Plan", Kind = "revamp", Summary = "Plano de ação.", DisplayName = "Plano de Ação", BusinessArea = "Plano de Ação" }, "t", default);

        Assert.Equal("human", guide.Audience);
        Assert.Equal(510, guide.Order);
        var after = await app.GetManifestAsync(default);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(1, after.Sections);
        Assert.Equal(indexBefore, await app.BuildIndexAsync(default));

        var (_, zip) = await app.ExportAsync(default);
        using var archive = new ZipArchive(new MemoryStream(zip));
        Assert.Contains(archive.Entries, e => e.FullName == "projects/revamp-actionplan/010-visao-geral.md");
        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("guia-"));

        // a tela continua vendo tudo
        var project = await app.GetProjectAsync("revamp-actionplan", default);
        Assert.Equal(2, project.Sections.Count);
        Assert.Equal("Plano de Ação", project.DisplayName);

        // virar técnica volta a exportar (e muda o hash)
        await app.WriteSectionAsync("revamp-actionplan", "guia-o-que-e", new WriteArchitectureSectionRequest { Content = "Serve para acompanhar ações.", Audience = "llm" }, "t", default);
        Assert.NotEqual(before.Hash, (await app.GetManifestAsync(default)).Hash);
    }

    [Fact]
    public void Blocks_parse_fields_body_and_mermaid()
    {
        const string content = """
            texto solto
            <<<GUIA
            chave: guia-conexoes
            titulo: Com quem conversa
            ---
            Quando um plano é criado:
            ```mermaid
            flowchart LR
              A-->B
            ```
            GUIA>>>
            <<<PROJETO
            nome: Plano de Ação
            frase: Acompanha ações.
            GUIA>>>
            <<<RESUMO
            Só corpo, sem campos.
            RESUMO>>>
            """;
        var guide = Assert.Single(ArchitectureBlocks.Parse(content, "GUIA"));
        Assert.Equal("guia-conexoes", guide.Field("chave"));
        Assert.Contains("```mermaid", guide.Body);
        Assert.Empty(ArchitectureBlocks.Parse(content, "PROJETO")); // bloco sem fechamento próprio não conta
        var summary = ArchitectureBlocks.First(content, "RESUMO");
        Assert.NotNull(summary);
        Assert.Empty(summary!.Fields);
        Assert.Equal("Só corpo, sem campos.", summary.Body);
    }

    [Fact]
    public void Suggestion_accepts_gap_kind()
    {
        var s = new ArchitectureSuggestion("login", "guia-regras", "gap", "Pergunta: login com senha ou só SSO?", null, "t", DateTimeOffset.UtcNow);
        Assert.Equal("gap", s.Kind);
    }

    private sealed class Settings : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", KnowledgeFilterOptions.None));
    }

    private sealed class InMemoryRepository : IKnowledgeRepository
    {
        private readonly List<ArchitectureProject> _projects = [];
        public Task<List<KnowledgeArticle>> GetArticlesAsync(string environment, bool tracked, CancellationToken cancellationToken) => Task.FromResult(new List<KnowledgeArticle>());
        public Task<KnowledgeArticle?> GetArticleAsync(string environment, int articleNumber, CancellationToken cancellationToken) => Task.FromResult<KnowledgeArticle?>(null);
        public Task<List<KnowledgeArticle>> SearchArticlesAsync(string environment, string? term, int limit, CancellationToken cancellationToken) => Task.FromResult(new List<KnowledgeArticle>());
        public void AddArticle(KnowledgeArticle article) { }
        public void RemoveArticle(KnowledgeArticle article) { }
        public Task<KnowledgeSyncState?> GetStateAsync(string environment, CancellationToken cancellationToken) => Task.FromResult<KnowledgeSyncState?>(null);
        public void AddState(KnowledgeSyncState state) { }
        public Task<List<ArchitectureProject>> GetProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(_projects.Where(p => !p.IsDeleted).ToList());
        public Task<ArchitectureProject?> GetProjectForUpdateAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_projects.FirstOrDefault(p => p.Key == key));
        public void AddProject(ArchitectureProject project) => _projects.Add(project);
        public void AddSection(ArchitectureSection section) { }
        public void AddVersion(ArchitectureSectionVersion version) { }
        public Task<List<ArchitectureSectionVersion>> GetVersionsAsync(Guid sectionId, CancellationToken cancellationToken) => Task.FromResult(new List<ArchitectureSectionVersion>());
        public Task<ArchitectureSectionVersion?> GetVersionAsync(Guid sectionId, int version, CancellationToken cancellationToken) => Task.FromResult<ArchitectureSectionVersion?>(null);
        public void AddSuggestion(ArchitectureSuggestion suggestion) { }
        public Task<ArchitectureSuggestion?> GetSuggestionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ArchitectureSuggestion?>(null);
        public Task<List<ArchitectureSuggestion>> GetSuggestionsAsync(string? status, CancellationToken cancellationToken) => Task.FromResult(new List<ArchitectureSuggestion>());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
