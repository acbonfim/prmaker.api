using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>
/// 0073: a busca da ER não monta mais o índice inteiro na memória — o banco escolhe os candidatos (termos + sinônimos, ID exato, nome do
/// glossário) e o ranking roda só neles. Aqui: o pedido que a busca faz ao repositório e as consultas leves (padrões em memória; o SQL
/// é comparado com a master no harness local).
/// </summary>
public class ReverseSearch0073Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ReverseItem Item(string id, string title, string body, string[]? tags = null, string[]? tables = null, string[]? refs = null,
        string[]? synonyms = null, string[]? modules = null) =>
        new(id, id.Split('-')[0], title, 3, 1, $"### {id} — {title}\n{body}", [.. tags ?? []], [.. tables ?? []], [.. refs ?? []], [], [.. modules ?? []], [.. synonyms ?? []]);

    /// <summary>Repositório em memória que anota o que a busca pediu (reimplementa só os membros que quer observar).</summary>
    private sealed class Recording : InMemoryKnowledgeRepository, IKnowledgeRepository
    {
        public List<ReverseSearchCandidates> Requests { get; } = [];
        public int GlossaryReads { get; private set; }
        public Func<IEnumerable<ReverseIndexEntry>, IEnumerable<ReverseIndexEntry>> Narrow { get; set; } = x => x;

        async IAsyncEnumerable<ReverseIndexEntry> IKnowledgeRepository.StreamSearchCandidatesAsync(ReverseSearchCandidates candidates,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(candidates);
            foreach (var e in Narrow(await GetIndexEntriesAsync(null, cancellationToken))) yield return e;
        }

        async Task<List<ReverseIndexEntry>> IKnowledgeRepository.QueryIndexAsync(ReverseIndexQuery query, CancellationToken cancellationToken)
        {
            if (query.Kinds is { Count: 1 } kinds && kinds.Contains("GLO")) GlossaryReads++;
            var items = await GetIndexEntriesAsync(null, cancellationToken);
            return items.Where(e => (query.Kinds is not { Count: > 0 } || query.Kinds.Contains(e.Kind)) && (query.IncludeRemoved || !e.Removed)).ToList();
        }
    }

    private static Recording Create(params (string Module, string Doc, ReverseItem Item)[] items)
    {
        var repo = new Recording();
        foreach (var (module, doc, item) in items) repo.AddIndexEntry(new ReverseIndexEntry(module, doc, item, 1, Now));
        return repo;
    }

    private static Task<List<domain.Responses.ReverseIndexHit>> Search(IKnowledgeRepository repo, string query, int limit = 20) =>
        ReverseSearch.RunAsync(repo, query, null, null, null, limit, false, new Dictionary<string, string>(), default);

    [Fact]
    public async Task Search_asks_for_term_groups_with_glossary_synonyms_and_the_exact_ids_in_the_query()
    {
        ReverseSearch.Invalidate();
        var repo = Create(
            ("revamp-rca", "funcional", Item("GLO-001", "RCA", "Análise de causa raiz.", synonyms: ["A3"])),
            ("revamp-rca", "funcional", Item("RN-001", "Gerar relatório A3", "O relatório é gerado ao concluir a análise.")));
        var hits = await Search(repo, "como gerar o relatorio RCA, ver RN-001");

        var request = Assert.Single(repo.Requests);
        // um grupo por termo; o do "rca" leva o sinônimo "a3" (de qualquer módulo — o ranking confere o do módulo do item)
        Assert.Contains(request.TermGroups, g => g.Contains("rca") && g.Contains("a3"));
        Assert.Contains(request.TermGroups, g => g.Count == 1 && g[0].StartsWith("relat"));
        Assert.Contains((null, "RN-001"), request.ExactIds);
        Assert.Equal(ReverseSearch.MaxCandidates, request.Max);
        Assert.Equal("revamp-rca#RN-001", hits[0].Ref); // ID exato vale mais que tudo
    }

    [Fact]
    public async Task Search_ranks_only_what_the_database_returns_as_candidates()
    {
        ReverseSearch.Invalidate();
        var repo = Create(
            ("revamp-a", "funcional", Item("RN-001", "Checklist de auditoria", "Texto sobre checklist.")),
            ("revamp-b", "funcional", Item("RN-002", "Checklist de segurança", "Texto sobre checklist.")));
        repo.Narrow = all => all.Where(e => e.ModuleKey == "revamp-b");
        var hits = await Search(repo, "checklist");
        Assert.Equal(["revamp-b#RN-002"], hits.Select(h => h.Ref));
    }

    [Fact]
    public async Task Search_filters_and_modules_go_to_the_database_request()
    {
        ReverseSearch.Invalidate();
        var repo = Create(("revamp-a", "funcional", Item("RN-001", "Checklist", "Texto.")));
        await ReverseSearch.RunAsync(repo, "checklist", ["revamp-a"], ["RN"], "funcional", 10, true, new Dictionary<string, string>(), default);
        var request = Assert.Single(repo.Requests);
        Assert.Equal(["revamp-a"], request.Modules);
        Assert.Equal(["RN"], request.Kinds);
        Assert.Equal("funcional", request.DocType);
        Assert.True(request.IncludeRemoved);
    }

    [Fact]
    public async Task Glossary_is_read_once_until_the_index_changes()
    {
        // o cache é estático (um por instância da API) e outras classes de teste, em paralelo, também o usam: se uma delas o trocar
        // entre as duas leituras, tenta de novo com um repositório novo
        for (var attempt = 1; ; attempt++)
        {
            var repo = Create(("revamp-rca", "funcional", Item("GLO-001", "RCA", "Causa raiz.", synonyms: ["A3"])));
            await ReverseSearch.SynonymsAsync(repo, default);
            await ReverseSearch.SynonymsAsync(repo, default);
            repo.AddIndexEntry(new ReverseIndexEntry("revamp-rca", "funcional", Item("RN-009", "Nova regra", "Texto."), 1, Now.AddMinutes(1)));
            await ReverseSearch.SynonymsAsync(repo, default);
            if (repo.GlossaryReads == 2) return; // 1 leitura para as duas primeiras chamadas + 1 depois que a marca do índice mudou
            Assert.True(attempt < 8, $"leituras do glossário: {repo.GlossaryReads} (esperado 2)");
        }
    }

    [Fact]
    public async Task Search_with_nothing_but_stop_words_lists_by_reference_like_before()
    {
        ReverseSearch.Invalidate();
        var repo = Create(("revamp-b", "funcional", Item("RN-002", "Beta", "x")), ("revamp-a", "funcional", Item("RN-001", "Alfa", "y")));
        var hits = await Search(repo, "de a o");
        Assert.Equal(["revamp-a#RN-001", "revamp-b#RN-002"], hits.Select(h => h.Ref));
    }

    [Fact]
    public async Task Default_light_queries_filter_like_the_database_ones()
    {
        var plain = new InMemoryKnowledgeRepository();   // os padrões da interface, sem o espião
        foreach (var (module, doc, item) in new[]
                 {
                     ("revamp-a", "funcional", Item("RN-001", "Regra", "x", tables: ["TB_A"], refs: ["RN-002"], modules: ["revamp-b"])),
                     ("revamp-a", "arquitetura", Item("API-001", "Endpoint", "y", refs: ["revamp-a#RN-001"])),
                     ("revamp-a", "funcional", Item("GAP-001", "Lacuna", "z")),
                     ("revamp-b", "funcional", Item("RN-002", "Outra", "w", tags: ["Painel"], refs: ["revamp-a#RN-001"]))
                 })
            plain.AddIndexEntry(new ReverseIndexEntry(module, doc, item, 1, Now));
        IKnowledgeRepository repo = plain;

        var others = await repo.QueryIndexAsync(new ReverseIndexQuery { Modules = ["revamp-a"], NotDocType = "arquitetura", ExcludeKinds = ["GAP"], IncludeRemoved = false }, default);
        Assert.Equal(["RN-001"], others.Select(e => e.ItemId));

        var impactTable = await repo.GetIndexImpactAsync("tb_a", null, 50, default);
        Assert.Equal(["revamp-a#RN-001"], impactTable.Select(e => e.Ref));
        var impactItem = await repo.GetIndexImpactAsync("revamp-a#RN-001", ("revamp-a", "RN-001"), 50, default);
        Assert.Equal(["revamp-a#API-001", "revamp-b#RN-002"], impactItem.Select(e => e.Ref));
        var impactTag = await repo.GetIndexImpactAsync("painel", null, 50, default);
        Assert.Equal(["revamp-b#RN-002"], impactTag.Select(e => e.Ref));

        var referrers = await repo.GetIndexReferrersAsync("revamp-a", "RN-001", 40, default);
        Assert.Equal(["revamp-a#API-001", "revamp-b#RN-002"], referrers.Select(e => e.Ref).Order());
        var citing = await repo.GetIndexCitingModuleAsync("revamp-b", 120, default);
        Assert.Equal(["revamp-a#RN-001"], citing.Select(e => e.Ref));
        var byRefs = await repo.GetIndexItemsByRefsAsync(["revamp-b#RN-002", "revamp-a#NOPE-1"], ReverseBodyMode.None, default);
        Assert.Equal(["revamp-b#RN-002"], byRefs.Select(e => e.Ref));
        Assert.Equal(["revamp-a", "revamp-b"], (await repo.GetIndexModuleKeysAsync(default)).Order());
    }
}
