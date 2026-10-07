using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0070: leituras enxutas — mesmas respostas de antes, sem carregar a Base inteira; documento em pedaços.</summary>
public class Performance0070Tests
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

    private static (ReverseEngineeringApplication Reverse, ArchitectureApplication Architecture, InMemoryKnowledgeRepository Repo) Create()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, kind, name) in new[] { ("revamp-kaizen", "revamp", "Kaizen"), ("revamp-users", "revamp", "Usuários") })
        {
            var p = new ArchitectureProject(key, "admin", now);
            p.Update(name, kind, $"https://github.com/electradv/{key}.git", "resumo", ["kw"], null, null, null, "admin", now);
            p.SetFriendly(name, null, name);
            repo.AddProject(p);
        }
        var settings = ReverseSettings.Default with { RequiredDocs = ["funcional"] };
        return (new ReverseEngineeringApplication(repo, new Settings(settings)), new ArchitectureApplication(repo, new KcSettings(), new Settings(settings)), repo);
    }

    private static string Funcional(int rules, string filler = "")
    {
        var sb = new System.Text.StringBuilder("""
            # Levantamento funcional — Kaizen
            ## Resumo do módulo
            Ideias 🚀 com emoji fora do BMP.
            ## Perfis e permissões
            ## Funcionalidades
            ## Casos de uso
            ## Estados e ciclo de vida
            ## Notificações
            ## Configurações e parâmetros
            ## Relatórios e indicadores
            ## Integrações com outros módulos
            ## Glossário
            ## Regras de negócio

            """);
        for (var i = 1; i <= rules; i++)
            sb.Append($"### RN-{i:000} — Regra número {i}\n- **Onde:** `KaizenService.cs:{i}`\n- **Tags:** etapa, regra{i}\nTexto da regra {i}. {filler}\n\n```mermaid\ngraph TD\nA-->B\n```\n\n");
        sb.Append("## Lacunas e pontos a confirmar\n");
        return sb.ToString();
    }

    private static async Task PublishAsync(ReverseEngineeringApplication app, string content)
    {
        var session = await app.StartSessionAsync("revamp-kaizen", "funcional", new StartReverseSessionRequest(), "dev", Dev, default);
        await app.SaveRevisionAsync(session.Revision.Id, new SaveReverseRevisionRequest { Content = content, Summary = "s", CoverageRatio = 0.95 }, "dev", Dev, default);
        await app.SubmitAsync(session.Revision.Id, "dev", Dev, default);
        await app.PublishAsync(session.Revision.Id, new PublishReverseRevisionRequest { Approve = true }, "gestor", Approver, default);
    }

    [Fact]
    public void Outline_cuts_before_headings_and_slices_rebuild_the_text()
    {
        var text = Funcional(400, new string('x', 200));
        var chunks = MarkdownOutline.Split(text, 5_000);
        Assert.True(chunks.Count > 5);
        var rebuilt = string.Concat(chunks.Select(c => CodePoints.Slice(text, c.Start, c.Length)));
        Assert.Equal(text, rebuilt);
        // todo pedaço depois do primeiro começa num cabeçalho e os IDs não se repetem nem somem
        foreach (var c in chunks.Skip(1)) Assert.StartsWith("#", CodePoints.Slice(text, c.Start, c.Length));
        Assert.Equal(Enumerable.Range(1, 400).Select(i => $"RN-{i:000}"), chunks.SelectMany(c => c.Ids));
        Assert.All(chunks, c => Assert.True(c.Estimate >= 40));
    }

    [Fact]
    public void Outline_never_cuts_inside_a_code_block()
    {
        var text = "## A\n" + new string('a', 60) + "\n```\n## não é cabeçalho\n" + new string('b', 60) + "\n```\n## B\nfim";
        var chunks = MarkdownOutline.Split(text, 10);
        var starts = chunks.Select(c => CodePoints.Slice(text, c.Start, c.Length).Split('\n')[0]).ToList();
        Assert.DoesNotContain("## não é cabeçalho", starts);
        Assert.Contains("## B", starts);
    }

    [Theory]
    [InlineData("### RN-001 — Título\n- **Onde:** `A.cs:1`\n- **Tags:** a, b\nTexto curto.")]
    [InlineData("só uma linha")]
    [InlineData("### UC-002 — x\n\n\n   \n")]
    public void Preview_body_gives_the_same_snippet(string body)
    {
        Assert.Equal(ReverseSearch.Snippet(body), ReverseSearch.Snippet(ReverseSearch.PreviewBody(body)));
    }

    [Fact]
    public void Preview_body_gives_the_same_snippet_for_long_items()
    {
        var random = new Random(70);
        for (var i = 0; i < 500; i++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 40)).Select(_ => random.Next(6) switch
            {
                0 => "- **Onde:** `Service.cs:" + random.Next(999) + "` e `Outro.cs`",
                1 => "",
                2 => "| a | b | c |",
                3 => "```sql\nSELECT * FROM TB_X\n```",
                4 => new string(' ', random.Next(5)) + "texto " + new string('x', random.Next(1, 300)) + " fim ",
                _ => "[link](http://x/" + random.Next() + ") **negrito** > citação"
            });
            var body = "### RN-" + i + " — título " + new string('t', random.Next(200)) + "\n" + string.Join("\n", lines);
            Assert.Equal(ReverseSearch.Snippet(body), ReverseSearch.Snippet(ReverseSearch.PreviewBody(body)));
        }
    }

    [Fact]
    public void Segmented_string_converter_writes_the_same_json_as_the_default()
    {
        var plain = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var segmented = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        segmented.Converters.Add(new Cime.BuildingBlocks.GlobalExtensions.SegmentedStringConverter());
        var random = new Random(7);
        var alphabet = "abcçãé\n\t\"<>&\\ 🚀\u2028".ToCharArray();
        foreach (var size in new[] { 0, 10, 16_384, 16_385, 8_191, 8_192 * 3 + 1, 200_000 })
        {
            var sb = new System.Text.StringBuilder();
            while (sb.Length < size) sb.Append(alphabet[random.Next(alphabet.Length)]);
            // surrogate na fronteira do segmento
            if (size > 8_192) sb.Insert(8_191, "🚀");
            var value = new { Content = sb.ToString(), Map = new Dictionary<string, int> { [sb.ToString(0, Math.Min(20, sb.Length))] = 1 }, Items = new[] { "x", sb.ToString() } };
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(value, plain), System.Text.Json.JsonSerializer.Serialize(value, segmented));
        }
    }

    [Fact]
    public void Normalizing_in_pieces_gives_the_same_text()
    {
        static string Whole(string v)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in v.Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
        var text = Funcional(2_000, "Ação É Ç 🚀 coração") + new string('á', 40_000) + "🚀" + new string('b', 20_000);
        Assert.True(text.Length > 100_000);
        Assert.Equal(Whole(text), ArchitectureSearch.Normalize(text));
    }

    [Fact]
    public void Code_points_count_surrogate_pairs_as_one()
    {
        const string text = "a🚀b🚀c";
        Assert.Equal("b🚀", CodePoints.Slice(text, 2, 2));
        Assert.Equal(5, CodePoints.Count(text, 0, text.Length));
    }

    [Fact]
    public async Task Document_without_content_brings_outline_and_items_from_the_index()
    {
        var (reverse, architecture, _) = Create();
        var content = Funcional(300, new string('y', 300));
        await PublishAsync(reverse, content);

        var full = await reverse.GetDocAsync("revamp-kaizen", "funcional", default);
        var light = await reverse.GetDocAsync("revamp-kaizen", "funcional", default, withContent: false);

        Assert.Equal(content.Trim(), full.Content!.Trim());
        Assert.Null(full.Outline);
        Assert.Null(light.Content);
        Assert.NotNull(light.Outline);
        Assert.Equal(full.Items.Select(i => i.Id), light.Items.Select(i => i.Id));
        Assert.Equal(300, light.Published!.Items);

        // os pedaços, buscados de 8 em 8, remontam o documento publicado
        var parts = new List<string>();
        for (var from = 0; from < light.Outline!.Chunks.Count; from += ArchitectureApplication.MaxPartsPerRequest)
        {
            var got = await architecture.GetSectionPartsAsync("revamp-kaizen", light.Type.SectionKey, from, from + 100, default);
            Assert.Equal(light.Outline.Hash, got.Hash);
            Assert.True(got.Parts.Count <= ArchitectureApplication.MaxPartsPerRequest);
            parts.AddRange(got.Parts.OrderBy(p => p.Index).Select(p => p.Text));
        }
        Assert.Equal(full.Content, string.Concat(parts));
        var outline = await architecture.GetSectionOutlineAsync("revamp-kaizen", light.Type.SectionKey, default);
        Assert.Equal(light.Outline.Chunks.Count, outline.Chunks.Count);
    }

    [Fact]
    public async Task Module_counts_come_from_the_database_and_match_the_items()
    {
        var (reverse, _, _) = Create();
        await PublishAsync(reverse, Funcional(12));

        var module = await reverse.GetModuleAsync("revamp-kaizen", Dev, default);
        var list = await reverse.ListModulesAsync(default);
        var doc = await reverse.GetDocAsync("revamp-kaizen", "funcional", default);

        Assert.Equal(12, module.Items);
        Assert.Equal(12, module.ItemsByKind["RN"]);
        Assert.Equal(12, module.Docs.Single(d => d.Type == "funcional").Published!.Items);
        Assert.Equal(12, list.Single(m => m.Key == "revamp-kaizen").Items);
        Assert.Equal(doc.Items.Count(i => !i.Removed), module.Items);
        Assert.Equal(0, list.Single(m => m.Key == "revamp-users").Items);
    }

    [Fact]
    public async Task Doc_types_for_the_screen_skip_the_template()
    {
        var (reverse, _, _) = Create();
        var withTemplate = await reverse.GetDocTypesAsync(default);
        var light = await reverse.GetDocTypesAsync(default, withTemplate: false);
        Assert.All(withTemplate, t => Assert.False(string.IsNullOrWhiteSpace(t.Template)));
        Assert.All(light, t => Assert.Equal(string.Empty, t.Template));
        Assert.Equal(withTemplate.Select(t => (t.Key, t.Title, string.Join("|", t.Headings))), light.Select(t => (t.Key, t.Title, string.Join("|", t.Headings))));
    }

    [Fact]
    public async Task Search_with_snippets_from_slices_matches_the_in_memory_search()
    {
        var (reverse, architecture, repo) = Create();
        await PublishAsync(reverse, Funcional(40));
        var terms = ArchitectureSearch.Terms("regra número 33 etapa");
        var projects = await repo.GetProjectsAsync(default);

        var expected = ArchitectureSearch.Run(projects, [], terms, 5);
        var got = await architecture.SearchAsync("regra número 33 etapa", 5, null, null, null, default);

        Assert.Equal(expected.Select(h => (h.ProjectKey, h.SectionKey, h.Score, h.Heading, h.Snippet)),
            got.Select(h => (h.ProjectKey, h.SectionKey, h.Score, h.Heading, h.Snippet)));
        Assert.Contains("Regra número", got[0].Snippet);
    }

    [Fact]
    public async Task Section_read_by_key_and_versions_still_work()
    {
        var (reverse, architecture, _) = Create();
        var content = Funcional(5);
        await PublishAsync(reverse, content);
        var section = await architecture.GetSectionAsync("revamp-kaizen", ReverseDocTypes.Get("funcional").SectionKey, default);
        Assert.Equal(content.Trim(), section.Content.Trim());
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => architecture.GetSectionAsync("revamp-kaizen", "nao-existe", default));
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => architecture.GetSectionAsync("nao-existe", "x", default));
    }
}
