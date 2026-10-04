using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;

namespace solvace.prform.Execution;

/// <summary>
/// Base Solvace pelo MCP (0052): engenharia reversa por item, seções e Knowledge Center — a análise consulta a base
/// antes do código, pelo mesmo caminho do resto do plano ("MCP primeiro"), e recebe só o que pediu (texto enxuto, não
/// JSON). Com <c>card</c>, a consulta fica registrada (trava da etapa de investigação).
/// </summary>
[McpServerToolType]
public partial class PrmakeBaseMcpTools(IReverseEngineeringApplication reverse, IArchitectureApplication architecture, IKnowledgeApplication knowledge)
{
    [McpServerTool(Name = "prmake_base_search", ReadOnly = true)]
    [Description("Base Solvace PRIMEIRO (antes de Grep/Read no codigo): busca na engenharia reversa por item (regras RN, casos de uso UC, " +
                 "telas TELA, endpoints API, tabelas DB, integracoes INT...) e, se faltar, nas secoes e no Knowledge Center. Devolve referencias " +
                 "curtas (modulo#ID); leia o texto com prmake_base_get. Informe o card para registrar a consulta.")]
    public Task<string> Search(
        [Description("Termos: tela, regra, entidade, mensagem de erro, tabela (TB_...), ou um ID (RN-012)")] string query,
        [Description("Modulo(s) (chave da Base Solvace, ex.: revamp-kaizen, legado-kaizen), separados por virgula (opcional)")] string? module = null,
        [Description("Tipos de item, separados por virgula: RN, UC, FN, TELA, API, DB, INT, EVT, JOB, CFG, PRF, EST, NTF, GAP... (opcional)")] string? kinds = null,
        [Description("Numero do card em analise (registra a consulta)")] string? card = null,
        int limit = 10,
        CancellationToken ct = default) => Safe(async () =>
    {
        var modules = Split(module);
        // 0054: a visão prática é para pessoas — não entra nas análises
        var hits = (await reverse.SearchAsync(query, modules, Split(kinds)?.Select(k => k.ToUpperInvariant()).ToList(), null, Math.Clamp(limit, 1, 40) + 5, false, ct))
            .Where(h => h.DocType != solvace.knowledge.domain.Reverse.ReverseDocTypes.Practical).Take(Math.Clamp(limit, 1, 40)).ToList();
        var sb = new StringBuilder();
        if (hits.Count > 0)
        {
            sb.AppendLine($"Engenharia reversa ({hits.Count}):");
            foreach (var h in hits)
                sb.AppendLine($"- {h.Ref} [{h.KindLabel}] {h.Title}{(h.Tables.Count > 0 ? " · " + string.Join(", ", h.Tables.Take(3)) : "")} ({h.DocType})"
                              + (h.Snippet.Length > 0 ? $"\n    {Short(h.Snippet, 140)}" : ""));
        }
        if (hits.Count < Math.Max(3, limit / 2))
        {
            var extra = await architecture.SearchAsync(query, 6, null, modules, null, ct);
            var sections = extra.Where(x => x.Type == "article" || x.Audience != ArchitectureSectionAudience.Human).Take(5).ToList();
            if (sections.Count > 0)
            {
                sb.AppendLine(hits.Count > 0 ? "Base Solvace (secoes/KC):" : "Sem item na engenharia reversa; Base Solvace (secoes/KC):");
                foreach (var x in sections)
                    sb.AppendLine(x.Type == "article"
                        ? $"- ART-{x.ArticleNumber} {Short(x.Title, 120)}"
                        : $"- {x.ProjectKey}/{x.SectionKey}{(x.Heading is null ? "" : " § " + x.Heading)} — {Short(x.Snippet, 140)}");
            }
        }
        if (sb.Length == 0)
            return "Nada na Base Solvace para esses termos. Tente sinonimos/nome da tela/tabela; se o assunto nao existe, e lacuna: " +
                   "busque so na pasta do modulo e registre (arch.sh suggest <projeto> re-funcional lacuna.md --kind gap --card <card>).";
        if (!string.IsNullOrWhiteSpace(card)) await reverse.RecordConsultedAsync(card, hits.Take(5).Select(h => h.Ref), ct);
        sb.Append("Texto: prmake_base_get(refs=\"<ref>, <ref>\"" + (string.IsNullOrWhiteSpace(card) ? "" : $", card=\"{card}\"") + ").");
        return sb.ToString();
    });

    [McpServerTool(Name = "prmake_base_get", ReadOnly = true)]
    [Description("Le da Base Solvace so o que pediu: itens da engenharia reversa (modulo#RN-012, varios separados por virgula), artigo do " +
                 "Knowledge Center (ART-21) ou secao (projeto/secao, opcional '§ titulo' para so aquele trecho). Cite os IDs na analise.")]
    public Task<string> Get(
        [Description("Referencias separadas por virgula: revamp-kaizen#RN-012, ART-21, revamp-kaizen/020-modulos § Telas")] string refs,
        [Description("Modulo padrao para IDs sem modulo (opcional)")] string? module = null,
        [Description("Numero do card em analise (registra a consulta)")] string? card = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var sb = new StringBuilder();
        var consulted = new List<string>();
        foreach (var raw in refs.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(12))
        {
            var art = Article().Match(raw);
            if (art.Success)
            {
                var a = await knowledge.GetArticleAsync(int.Parse(art.Groups[1].Value), ct);
                sb.AppendLine($"--- ART-{a.ArticleNumber} — {a.Title} ({a.Category})").AppendLine(Cap(a.Content, 8000));
                consulted.Add($"ART-{a.ArticleNumber}");
                continue;
            }
            if (ReverseItemKinds.ParseRef(raw.Replace("§", " ")) is not null)
            {
                foreach (var item in await reverse.GetItemsAsync([raw], module, ct))
                {
                    sb.AppendLine($"--- {item.Ref} ({item.DocType}, v{item.SectionVersion}){(item.ReferencedBy.Count > 0 ? " · citado por: " + string.Join(", ", item.ReferencedBy.Take(8)) : "")}")
                        .AppendLine(item.Body);
                    // 0054: o que já deu errado neste item
                    foreach (var trap in item.Traps)
                        sb.AppendLine($"  ARMADILHA{(trap.NeedsReview ? " (a conferir)" : "")}: {trap.Title}{(trap.Cards.Count > 0 ? " · cards " + string.Join(", ", trap.Cards.Take(4)) : "")}\n    {trap.Text.Replace("\n", "\n    ")}");
                    consulted.Add(item.Ref);
                }
                continue;
            }
            var section = SectionRef().Match(raw);
            if (!section.Success)
            {
                sb.AppendLine($"--- {raw}: referencia invalida (use modulo#RN-012, ART-21 ou projeto/secao).");
                continue;
            }
            // O espelho mostra "020-modulos"; a chave da seção é "modulos".
            var s = await architecture.GetSectionAsync(section.Groups["project"].Value, Regex.Replace(section.Groups["section"].Value, @"^\d{3}-", ""), ct);
            var content = s.Content;
            var heading = section.Groups["heading"].Success ? section.Groups["heading"].Value.Trim() : null;
            if (heading is { Length: > 0 }) content = HeadingBlock(content, heading) ?? $"(trecho '{heading}' nao encontrado; titulos: {string.Join(" | ", Headings(content).Take(30))})";
            sb.AppendLine($"--- {section.Groups["project"].Value}/{s.Key} — {s.Title} v{s.Version}{(heading is null ? "" : " § " + heading)}").AppendLine(Cap(content, 15000));
            consulted.Add($"{section.Groups["project"].Value}/{s.Key}");
        }
        if (!string.IsNullOrWhiteSpace(card) && consulted.Count > 0) await reverse.RecordConsultedAsync(card, consulted, ct);
        return sb.ToString().TrimEnd();
    });

    [McpServerTool(Name = "prmake_base_module", ReadOnly = true)]
    [Description("Ficha do modulo na engenharia reversa: mundo (legado/revamp), fontes de codigo, documentos publicados, contagem de itens " +
                 "e a lista de IDs + titulos dos tipos pedidos (padrao: funcionalidades e casos de uso). Bom para se orientar num modulo antes de buscar.")]
    public Task<string> Module(
        [Description("Chave do modulo (ex.: revamp-kaizen)")] string module,
        [Description("Tipos a listar, separados por virgula (padrao FN,UC; 'all' = todos)")] string? kinds = null,
        string? card = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var m = await reverse.GetModuleAsync(module, [], ct);
        var sb = new StringBuilder();
        sb.AppendLine($"{m.Key} — {m.DisplayName ?? m.Name} ({m.World}){(m.BusinessArea is null ? "" : " · area " + m.BusinessArea)} · engenharia reversa "
                      + (m.Complete ? "COMPLETA" : $"{m.PublishedRequired}/{m.RequiredCount}"));
        if (m.Siblings.Count > 0) sb.AppendLine($"Mesma area: {string.Join(", ", m.Siblings)}");
        if (m.Sources.Count > 0) sb.AppendLine("Fontes: " + string.Join("; ", m.Sources.Select(s => $"{s.Repository}{(s.Path is null ? "" : "/" + s.Path)} ({s.Role})")));
        sb.AppendLine("Documentos: " + string.Join(" · ", m.Docs.Select(d => $"{d.Type} {(d.Published is null ? "—" : "v" + d.Published.Version + $" ({d.Published.Items} itens)")}")));
        if (m.ItemsByKind.Count > 0) sb.AppendLine("Itens: " + string.Join(" · ", m.ItemsByKind.Select(k => $"{k.Key} {k.Value}")));
        if (m.Relations.Count > 0 || m.UsedBy.Count > 0)
            sb.AppendLine($"Depende de: {string.Join(", ", m.Relations.Select(r => r.Target).Distinct().Take(15))} · usado por: {string.Join(", ", m.UsedBy.Select(r => r.Source).Distinct().Take(15))}");
        var wanted = string.Equals(kinds?.Trim(), "all", StringComparison.OrdinalIgnoreCase) ? null : (Split(kinds) ?? ["FN", "UC"]).Select(k => k.ToUpperInvariant()).ToList();
        var items = await reverse.SearchAsync(null, [m.Key], wanted, null, 400, false, ct);
        if (items.Count > 0)
        {
            sb.AppendLine($"IDs ({(wanted is null ? "todos" : string.Join(",", wanted))}):");
            foreach (var i in items) sb.AppendLine($"- {i.ItemId} {i.Title}");
        }
        else if (m.Docs.All(d => d.Published is null))
            sb.AppendLine("Sem engenharia reversa publicada: use a base antiga (prmake_base_get(\"" + m.Key + "/020-modulos\")) e registre as lacunas.");
        if (!string.IsNullOrWhiteSpace(card)) await reverse.RecordConsultedAsync(card, [$"module:{m.Key}"], ct);
        return sb.ToString().TrimEnd();
    });

    [McpServerTool(Name = "prmake_base_impact", ReadOnly = true)]
    [Description("Impacto entre modulos: quem (em qualquer modulo) usa uma tabela (TB_...), um item (modulo#API-004), um modulo (chave) ou uma tag. " +
                 "Use antes de propor correcao que mexe em tabela, evento, endpoint ou regra compartilhada.")]
    public Task<string> Impact(
        [Description("Tabela, item, modulo ou tag")] string term,
        CancellationToken ct = default) => Safe(async () =>
    {
        var hits = await reverse.ImpactAsync(term, 120, ct);
        if (hits.Count == 0) return $"Nenhum item publicado cita '{term}'. (As relacoes do grafo antigo: prmake_base_module.)";
        var sb = new StringBuilder($"Itens que citam {term} ({hits.Count}):\n");
        foreach (var g in hits.GroupBy(h => h.ModuleKey))
        {
            sb.AppendLine($"{g.Key}:");
            foreach (var h in g) sb.AppendLine($"  - {h.ItemId} [{h.Kind}] {h.Title}");
        }
        return sb.ToString().TrimEnd();
    });

    private static async Task<string> Safe(Func<Task<string>> body)
    {
        try { return await body(); }
        catch (Exception e) when (e is DomainException or KnowledgeNotFoundException or ReverseForbiddenException) { throw new McpException(e.Message, e); }
    }

    private static List<string>? Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max] + $"\n…(cortado em {max} caracteres — peca um trecho com '§ titulo' ou um item)";

    private static IEnumerable<string> Headings(string content) => ReverseDocParser.Headings(content).Where(h => h.Level <= 3).Select(h => h.Text);

    /// <summary>Bloco do primeiro cabeçalho que contém o texto até o próximo de nível igual ou maior.</summary>
    private static string? HeadingBlock(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var headings = ReverseDocParser.Headings(content);
        var wanted = ReverseLint.Normalize(heading);
        var index = headings.FindIndex(h => ReverseLint.Normalize(h.Text).Contains(wanted, StringComparison.Ordinal));
        if (index < 0) return null;
        var start = headings[index];
        var end = headings.Skip(index + 1).FirstOrDefault(h => h.Level <= start.Level)?.Line ?? lines.Length;
        return string.Join('\n', lines[start.Line..end]);
    }

    [GeneratedRegex(@"^ART[-\s]?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    [GeneratedRegex(@"^(?<project>[a-z0-9][a-z0-9._-]*)/(?<section>[a-z0-9][a-z0-9._-]*)(?:\s*§\s*(?<heading>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRef();
}
