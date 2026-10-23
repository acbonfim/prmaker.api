using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>
/// Contexto por ITEM para o "Pergunte à Base Solvace" (0071). A busca por seção devolve um documento de centenas de
/// milhares de caracteres e o Pergunte levava um bloco só; a resposta de "como fazer" está espalhada em vários itens
/// (UC da tela, RN da pré-condição, RN de quem pode, RN dos efeitos). Aqui: duas buscas por item (palavras da pergunta e
/// termos da IA) fundidas por posição (RRF), pesos por tipo de item conforme a intenção da pergunta e a expansão pelas
/// referências (<c>Refs</c>) dos melhores itens — o UC cita as RN que o completam.
/// </summary>
public static class ReverseAnswerContext
{
    private const double RrfK = 20;

    /// <summary>Peso do tipo de item para a intenção da pergunta (<see cref="ArchitectureQuestionKind"/>).</summary>
    public static double KindWeight(string kind, string intent)
    {
        if (intent == ArchitectureQuestionKind.Technical)
            return kind switch
            {
                "API" or "DB" => 1.4, "SQL" or "TRG" => 1.3, "TEC" or "CMP" or "EVT" or "JOB" or "INT" or "SEQ" => 1.2, "INF" or "ADR" or "RN" => 1.1,
                "UC" or "FN" => 1.0, "TELA" or "UI" or "GAP" => 0.8, "FAQ" => 0.6, "TUT" => 0.5, _ => 1.0
            };
        if (intent == ArchitectureQuestionKind.Rule)
            return kind switch
            {
                "RN" => 1.6, "UC" or "EST" => 1.3, "PRF" or "FAQ" => 1.2, "FN" or "TUT" or "CFG" => 1.1, "NTF" or "TELA" => 1.0,
                "API" => 0.7, "GAP" or "DB" or "SQL" or "TRG" => 0.6, "TEC" or "CMP" or "EVT" or "JOB" or "INF" or "ADR" or "NFR" or "UI" => 0.5, _ => 0.9
            };
        if (intent == ArchitectureQuestionKind.Operation)
            return kind switch
            {
                "TUT" => 1.8, "FAQ" => 1.7, "UC" => 1.5, "TELA" or "PRF" => 1.4, "RN" => 1.3, "CFG" or "FLX" => 1.2, "NTF" => 1.1, "FN" or "EST" => 1.0,
                "REL" or "OBJ" => 0.9, "PER" => 0.8, "UI" or "GLO" => 0.7, "API" or "INT" or "SEQ" => 0.6, "CMP" or "EVT" or "JOB" or "GAP" => 0.5,
                "TEC" or "DB" or "SQL" or "TRG" or "NFR" => 0.4, "INF" or "ADR" => 0.3, _ => 0.8
            };
        return kind == "GAP" ? 0.7 : 1.0;
    }

    /// <summary>"Como faço / o que preciso / quem pode / onde fica": pergunta de uso, mesmo que a IA tenha dito "regra" ou "outra".</summary>
    public static bool LooksLikeHowTo(string question)
    {
        var q = ArchitectureSearch.Normalize(question);
        return HowToPhrases.Any(q.Contains);
    }

    private static readonly string[] HowToPhrases =
    [
        "como faco", "como fazer", "como posso", "como eu", "como consigo", "como cadastr", "como cri", "como alter", "como edit", "como exclu",
        "como habilit", "como ativ", "como desativ", "como configur", "como acess", "como vej", "como encontr", "como tran", "como libera",
        "o que preciso", "o que e preciso", "o que eu preciso", "o que devo", "o que tenho que", "passo a passo", "quem pode", "quem tem permissao",
        "onde fica", "onde vejo", "onde cadastr", "onde configur", "onde encontr", "por que nao aparece", "porque nao aparece", "nao consigo",
        "tenho que fazer", "tem como", "e possivel"
    ];

    /// <summary>Intenção final: a regra de "como fazer" vale sobre "regra"/"outra" da IA, nunca sobre "técnica".</summary>
    public static string Intent(string question, string aiKind) =>
        aiKind != ArchitectureQuestionKind.Technical && LooksLikeHowTo(question) ? ArchitectureQuestionKind.Operation : aiKind;

    /// <summary>
    /// Funde os rankings (cada um do melhor para o pior) pela posição — um item bem colocado nas duas buscas sobe, e a
    /// magnitude diferente das pontuações de cada busca não atrapalha. ID citado na pergunta (pontuação ≥ 100) pesa triplo.
    /// </summary>
    public static Dictionary<string, double> Fuse(params IReadOnlyList<ReverseIndexHit>[] runs)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var run in runs)
            for (var i = 0; i < run.Count; i++)
                scores[run[i].Ref] = scores.GetValueOrDefault(run[i].Ref) + (run[i].Score >= 100 ? 3 : 1) / (RrfK + i);
        return scores;
    }

    /// <summary>Itens que respondem: os <paramref name="limit"/> melhores pelo peso do tipo e, depois, os citados por eles.</summary>
    public static (List<(ReverseIndexEntry Entry, double Score, string? Via)> Items, int Primary) Pick(
        Dictionary<string, double> fused, IReadOnlyList<ReverseIndexEntry> entries, string intent, IReadOnlyCollection<string> boostModules,
        int limit, int maxRelated)
    {
        var byRef = new Dictionary<string, ReverseIndexEntry>(StringComparer.Ordinal);
        foreach (var e in entries) byRef.TryAdd(e.Ref, e);

        var ranked = fused.Where(kv => byRef.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var e = byRef[kv.Key];
                var score = kv.Value * KindWeight(e.Kind, intent) * (boostModules.Contains(e.ModuleKey) ? 1.5 : 1.0);
                // visão prática (human) é escrita para quem não programa
                if (e.DocType == domain.Reverse.ReverseDocTypes.Practical && intent != ArchitectureQuestionKind.Technical) score *= 1.4;
                return (Entry: e, Score: score);
            })
            .Where(x => !x.Entry.Removed)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Entry.Ref, StringComparer.Ordinal)
            .Take(limit).ToList();

        var chosen = ranked.Select(x => x.Entry.Ref).ToHashSet(StringComparer.Ordinal);
        var items = ranked.Select(x => (x.Entry, x.Score, Via: (string?)null)).ToList();

        // o UC cita as RN que o completam: só os que importam para a intenção (peso ≥ 1). Revezando entre os melhores itens — um item
        // muito citado (ex.: uma sequência técnica com várias RN) não ocupa todas as vagas e deixa de fora a pré-condição do UC da tela.
        var lists = ranked.Take(6).Select(r => (Item: r.Entry, Related: r.Entry.Refs
                .Select(raw => raw.Contains('#') ? raw : $"{r.Entry.ModuleKey}#{raw}").Distinct()
                .Where(key => !chosen.Contains(key) && byRef.TryGetValue(key, out var t) && !t.Removed && KindWeight(t.Kind, intent) >= 1.0)
                .Select(key => byRef[key]).OrderByDescending(t => KindWeight(t.Kind, intent)).ToList()))
            .Where(x => x.Related.Count > 0).ToList();
        var related = new List<(ReverseIndexEntry Entry, double Score, string? Via)>();
        for (var round = 0; related.Count < maxRelated && lists.Any(l => l.Related.Count > round); round++)
            foreach (var (item, candidates) in lists)
            {
                if (related.Count >= maxRelated) break;
                if (candidates.Count <= round) continue;
                var target = candidates[round];
                if (!chosen.Add(target.Ref)) continue;
                related.Add((target, ranked.First(r => r.Entry.Ref == item.Ref).Score * KindWeight(target.Kind, intent), item.ItemId));
            }
        items.AddRange(related);
        return (items, ranked.Count);
    }

    /// <summary>Texto do item cortado no limite (num ponto de quebra, sem partir uma linha ao meio quando dá).</summary>
    public static string CutBody(string body, int max)
    {
        var text = (body ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf('\n', max - 1, Math.Min(max, 400));
        return text[..(cut > 0 ? cut : max)].TrimEnd() + "\n…";
    }
}
