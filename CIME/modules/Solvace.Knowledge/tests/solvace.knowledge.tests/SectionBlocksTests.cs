using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>Chat de melhoria em seção grande: blocos por cabeçalho, troca conferida na posição e trecho da seção para a IA.</summary>
public class SectionBlocksTests
{
    private const string Doc = "Introdução do documento.\n\n## Filtros\nTexto dos filtros.\n```\n## não é cabeçalho (código)\n```\n\n### svc-filters\nDetalhe cleanable.\n\n## Relatórios\nTexto final.\n";

    [Fact]
    public void Split_corta_por_cabecalho_fora_de_codigo_e_cobre_o_texto_inteiro()
    {
        var blocks = SectionBlocks.Split(Doc);
        Assert.Equal([null, "Filtros", "svc-filters", "Relatórios"], blocks.Select(b => b.Heading));
        Assert.Equal(Doc, string.Concat(blocks.Select(b => SectionBlocks.Text(Doc, b))));
        Assert.Contains("## não é cabeçalho", SectionBlocks.Text(Doc, blocks[1]));
    }

    [Fact]
    public void Split_corta_bloco_grande_em_linha_em_branco()
    {
        var big = "## Grande\n" + string.Join("\n\n", Enumerable.Range(0, 40).Select(i => new string('x', 100) + i));
        var blocks = SectionBlocks.Split(big, maxBlock: 1_000);
        Assert.True(blocks.Count > 3);
        Assert.All(blocks.Skip(1), b => Assert.Equal("Grande (continuação)", b.Heading));
        Assert.Equal(big, string.Concat(blocks.Select(b => SectionBlocks.Text(big, b))));
    }

    [Fact]
    public void Apply_troca_so_os_blocos_e_mantem_a_quebra_antes_do_proximo_cabecalho()
    {
        var blocks = SectionBlocks.Split(Doc);
        var svc = blocks[2];
        var result = SectionBlocks.Apply(Doc, [new SectionBlockEdit(svc.Start, SectionBlocks.Text(Doc, svc), "### svc-filters\nDetalhe cleanable: false conta como vazio.")]);
        Assert.Contains("### svc-filters\nDetalhe cleanable: false conta como vazio.\n\n## Relatórios", result);
        Assert.StartsWith("Introdução do documento.\n\n## Filtros", result);
    }

    [Fact]
    public void Apply_recusa_quando_a_secao_mudou()
    {
        var svc = SectionBlocks.Split(Doc)[2];
        var changed = Doc.Replace("Introdução", "Abertura nova");
        Assert.Throws<DomainException>(() => SectionBlocks.Apply(changed, [new SectionBlockEdit(svc.Start, SectionBlocks.Text(Doc, svc), "x")]));
    }

    [Fact]
    public void Blocos_da_ia_sao_lidos_e_tirados_da_resposta()
    {
        var content = "Mudei o bloco 2.\n<<<BLOCO\nbloco: 2\n---\n### svc-filters\nNovo.\nBLOCO>>>";
        var parsed = ArchitectureBlocks.Parse(content, "BLOCO");
        Assert.Equal("2", parsed.Single().Field("bloco"));
        Assert.Equal("### svc-filters\nNovo.", parsed.Single().Body);
        Assert.Equal("Mudei o bloco 2.", ArchitectureBlocks.Strip(content, "BLOCO"));
    }

    [Fact]
    public void DensestWindow_acha_onde_os_termos_aparecem_juntos()
    {
        var text = "cleanable " + new string('a', 5_000) + " cleanable filters sidebar " + new string('b', 5_000);
        var at = ArchitectureSearch.DensestWindow(text, ["cleanable", "filters", "sidebar"], 200);
        Assert.Equal(text.IndexOf("cleanable filters", StringComparison.Ordinal), at);
        Assert.Equal(-1, ArchitectureSearch.DensestWindow(text, ["inexistente"], 200));
    }

    [Fact]
    public void RankBlocks_prefere_id_citado_e_nomes_tecnicos_a_blocos_longos_com_palavras_comuns()
    {
        var common = string.Join(" ", Enumerable.Repeat("svc component filtros campo valor botão tela", 200));
        var blocks = new List<(string, string?)>
        {
            ("UI-015 — Storybook\n" + common, "UI-015 — Storybook da biblioteca svc"),
            ("UI-1052 — Padrão svc-filters-sidebar: Buscar/Limpar/Fechar; isTotallyEmpty", "UI-1052 — Padrão \"svc-filters-sidebar\""),
            ("UI-955 — Relatório do Elogio com svc-filters-sidebar", "UI-955 — Relatório Por período do Elogio"),
            ("UI-020 — Outra tela\n" + common, "UI-020 — Outra tela")
        };
        var terms = ArchitectureSearch.WeightedTerms("Lacuna do svc-filters-sidebar: isTotallyEmpty com cleanable: false. Item mais próximo: UI-1052.");
        var ranked = ArchitectureSearch.RankBlocks(blocks, terms);
        Assert.Equal(1, ranked[0]);
        Assert.Equal(2, ranked[1]);
        Assert.Contains(terms, t => t.Term == "ui-1052" && t.Weight > 2);
    }

    private sealed class Settings : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(ReverseSettings.Default);
    }

    private sealed class KcSettings : IKnowledgeSettingsProvider
    {
        public Task<KnowledgeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KnowledgeSettings("dev", KnowledgeFilterOptions.None));
    }

    [Fact]
    public async Task Excerpt_de_secao_grande_vem_em_volta_dos_termos_e_curta_vem_inteira()
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        var p = new ArchitectureProject("edv-apps", "admin", now);
        p.Update("Apps", "frontend", null, "resumo", [], null, null, null, "admin", now);
        repo.AddProject(p);
        var app = new ArchitectureApplication(repo, new KcSettings(), new Settings());
        var big = "## Início\n" + new string('a', 50_000) + "\n## Filtros\nsvc-filters cleanable isTotallyEmpty\n" + new string('b', 50_000);
        await app.WriteSectionAsync("edv-apps", "re-design", new WriteArchitectureSectionRequest { Content = big, Title = "Design" }, "admin", default);
        await app.WriteSectionAsync("edv-apps", "visao", new WriteArchitectureSectionRequest { Content = "curta", Title = "Visão" }, "admin", default);
        await app.SearchAsync("cleanable", 5, null, null, null, default); // prepara o texto da busca

        var excerpt = await app.GetSectionExcerptAsync("edv-apps", "re-design", 4_000, ArchitectureSearch.Terms("svc-filters cleanable"), default);
        Assert.Contains("svc-filters cleanable", excerpt.Content);
        Assert.True(excerpt.Content.Length < 4_100);
        Assert.Equal("curta", (await app.GetSectionExcerptAsync("edv-apps", "visao", 4_000, null, default)).Content);
    }
}
