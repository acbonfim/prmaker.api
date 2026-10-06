using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

/// <summary>Um destino possível de integração: chave do projeto, nomes e apelidos (campo Module do card) que o identificam.</summary>
public sealed record IntegrationTarget(string Key, IReadOnlyCollection<string> Names);

/// <summary>
/// Uma integração da engenharia reversa (0066): o item <c>INT-…</c> com os destinos já validados contra os projetos da
/// Base Solvace, o tipo da ligação (do <c>**Mecanismo:**</c>) e o que não foi reconhecido (vira aviso na checagem).
/// </summary>
public sealed record ReverseIntegration(
    string Source,
    string ItemId,
    string Title,
    string Kind,
    string? Mechanism,
    string? Contract,
    string? Evidence,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Unresolved,
    bool ToConfirm)
{
    public string Ref => $"{Source}#{ItemId}";
}

/// <summary>
/// Lê os itens <c>INT</c> (0066). Antes as integrações viravam relação com os "tokens" do <c>**Módulos:**</c> sem validar
/// (texto livre como "a confirmar (Digital Obeya" virava o nó "a") e o tipo era adivinhado pelo corpo inteiro. Agora:
/// cada trecho do <c>**Módulos:**</c> só vale se casar com a chave, o nome ou um apelido de um projeto (ou <c>ext:serviço</c>);
/// o tipo sai do <c>**Mecanismo:**</c> (vocabulário fechado, com reserva no texto do item para os documentos antigos).
/// </summary>
public static partial class ReverseIntegrations
{
    /// <summary>Tipos de ligação (os mesmos de <c>ArchitectureRelationKind</c>) e as palavras do mecanismo que os indicam.</summary>
    public static readonly IReadOnlyList<(string Kind, string[] Words)> MechanismWords =
    [
        ("queue", ["sqs", "fila", "queue", "worker"]),
        ("event", ["sns", "evento", "event", "eventbridge", "topico", "topic", "publica", "assina", "signalr", "websocket", "hub"]),
        ("cache", ["redis", "elasticache", "cache", "memcached"]),
        ("storage", ["s3", "bucket", "arquivo", "storage", "blob", "upload", "download", "ftp", "sftp", "planilha", "pdf"]),
        ("job", ["job", "agendamento", "agendad", "cron", "sql agent", "rotina", "scheduler", "lambda agendada", "hangfire", "batch"]),
        ("trigger", ["trigger", "gatilho"]),
        ("database", ["banco", "tabela", "view", "procedure", "function", "sql", "database", "replica", "sincroniza"]),
        ("http", ["http", "rest", "api", "endpoint", "rota", "url", "chamada", "graphql", "soap", "webservice"]),
        ("package", ["pacote", "nuget", "npm", "biblioteca", "helpers", "helper", "dll", "projeto compartilhado", "library", "shared",
            "codigo em processo", "em processo", "in-process", "mesmo processo", "chamada de codigo"]),
        ("frontend", ["front", "iframe", "navegador", "tela", "link", "redireciona", "componente", "component", "widget", "modal"]),
        ("external", ["externo", "terceiro", "saas", "servico externo"])
    ];

    /// <summary>Mecanismos aceitos no <c>**Mecanismo:**</c> (o que a skill e o modelo pedem).</summary>
    public const string MechanismVocabulary =
        "http · evento (SNS/EventBridge/SignalR) · fila (SQS) · banco compartilhado · pacote/biblioteca · arquivo/S3 · cache (Redis) · job/agendamento · trigger · front-end · externo";

    /// <summary>Lê os campos do item e resolve destinos e tipo.</summary>
    public static ReverseIntegration Read(string source, string itemId, string title, string body, IReadOnlyCollection<IntegrationTarget> targets)
    {
        var modulesLine = Field(body, ModulesLine());
        var mechanism = Field(body, MechanismLine());
        var contract = Field(body, ContractLine());
        var evidence = Field(body, WhereLine());
        var confirm = Field(body, ConfirmLine());
        var (resolved, unresolved, toConfirm) = ResolveTargets(modulesLine, source, targets);
        var kind = KindOf(mechanism) ?? KindOf(title) ?? LegacyKind(body);
        if (kind == "other" && resolved.Count > 0 && resolved.All(t => t.StartsWith("ext:", StringComparison.Ordinal))) kind = "external";
        return new ReverseIntegration(source, itemId, title, kind, Clip(mechanism, 200), Clip(contract, 300), Clip(evidence, 300),
            resolved, unresolved, toConfirm || !string.IsNullOrWhiteSpace(confirm));
    }

    /// <summary>
    /// Destinos do <c>**Módulos:**</c>: cada trecho (separado por vírgula, ponto e vírgula, "·" ou "|") vale pela chave de projeto
    /// que contém, por <c>ext:serviço</c> ou pelo nome/apelido exato (sem acento/caixa, antes de parênteses). O resto é "não
    /// reconhecido". "a confirmar" no trecho marca a integração como a confirmar.
    /// </summary>
    public static (List<string> Resolved, List<string> Unresolved, bool ToConfirm) ResolveTargets(string? modulesLine, string source,
        IReadOnlyCollection<IntegrationTarget> targets)
    {
        var resolved = new List<string>();
        var unresolved = new List<string>();
        var toConfirm = false;
        if (string.IsNullOrWhiteSpace(modulesLine)) return (resolved, unresolved, toConfirm);
        var keys = targets.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var t in targets)
            foreach (var n in t.Names.Append(t.Key).SelectMany(n => new[] { n, StripWorld(n), Head(StripWorld(n)) }))
            {
                var k = Normalize(n);
                if (k.Length < 3) continue;
                if (!byName.TryGetValue(k, out var list)) byName[k] = list = [];
                if (!list.Contains(t.Key)) list.Add(t.Key);
            }
        // nome igual no legado e no revamp ("Checklist"): vale o do mesmo mundo da origem; senão fica sem resolver
        string? ByName(string name)
        {
            if (!byName.TryGetValue(name, out var list)) return null;
            if (list.Count == 1) return list[0];
            var world = source.StartsWith("legado-", StringComparison.OrdinalIgnoreCase) ? "legado-"
                : source.StartsWith("revamp-", StringComparison.OrdinalIgnoreCase) ? "revamp-" : null;
            var same = world is null ? [] : list.Where(k => k.StartsWith(world, StringComparison.OrdinalIgnoreCase)).ToList();
            return same.Count == 1 ? same[0] : null;
        }

        // vírgula/; dentro de parênteses não separa ("legado-actionplan (a confirmar a chave; projeto action_plan)")
        foreach (var raw in SplitTop(modulesLine))
        {
            var part = raw.Replace("`", "").Replace("*", "").Trim().Trim('.', ' ');
            if (part.Length == 0) continue;
            var norm = Normalize(part);
            if (norm.Contains("a confirmar") || norm.Contains("confirmar")) toConfirm = true;
            if (norm is "-" or "—" or "nenhum" or "nenhuma" or "n/a") continue;
            var found = new List<string>();
            foreach (Match m in ExternalKey().Matches(part.ToLowerInvariant()))
                found.Add(m.Value.TrimEnd('.', '-'));
            foreach (Match m in KeyToken().Matches(part.ToLowerInvariant()))
                if (keys.Contains(m.Value)) found.Add(keys.First(k => k.Equals(m.Value, StringComparison.OrdinalIgnoreCase)));
            if (found.Count == 0)
            {
                var head = Normalize(Head(part));
                if (head.Length >= 3 && ByName(head) is { } key) found.Add(key);
            }
            if (found.Count == 0) { unresolved.Add(Clip(part, 80)!); continue; }
            foreach (var f in found)
                if (!f.Equals(source, StringComparison.OrdinalIgnoreCase) && !resolved.Contains(f)) resolved.Add(f);
        }
        return (resolved, unresolved, toConfirm);
    }

    /// <summary>Tipo pelo mecanismo: a palavra que aparece primeiro decide ("HTTP do navegador + leitura no banco" → http).</summary>
    public static string? KindOf(string? mechanism)
    {
        if (string.IsNullOrWhiteSpace(mechanism)) return null;
        var text = " " + Normalize(mechanism) + " ";
        string? best = null;
        var bestAt = int.MaxValue;
        foreach (var (kind, words) in MechanismWords)
            foreach (var w in words)
            {
                var m = Regex.Match(text, @"(?<![a-z0-9])" + Regex.Escape(w));
                if (m.Success && m.Index < bestAt) { bestAt = m.Index; best = kind; }
            }
        return best;
    }

    /// <summary>Reserva para documentos sem <c>**Mecanismo:**</c> — a mesma heurística de antes da 0066.</summary>
    public static string LegacyKind(string body)
    {
        var b = Normalize(body);
        if (b.Contains("sqs") || b.Contains("fila")) return "queue";
        if (b.Contains("sns") || b.Contains("evento") || b.Contains("event")) return "event";
        if (b.Contains("banco") || b.Contains("tabela") || b.Contains(" sql")) return "database";
        if (b.Contains("http") || b.Contains("rest") || b.Contains("endpoint") || b.Contains("api ")) return "http";
        if (b.Contains("pacote") || b.Contains("nuget") || b.Contains("npm")) return "package";
        if (b.Contains("front")) return "frontend";
        return "other";
    }

    /// <summary>O mecanismo citado está no vocabulário? (null = sem mecanismo)</summary>
    public static bool? KnownMechanism(string? mechanism) => string.IsNullOrWhiteSpace(mechanism) ? null : KindOf(mechanism) is not null;

    public static string? Field(string body, Regex line)
    {
        var m = line.Match(body ?? string.Empty);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static IEnumerable<string> SplitTop(string value)
    {
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch is '(' or '[') depth++;
            else if (ch is ')' or ']') depth = Math.Max(0, depth - 1);
            if (depth == 0 && ch is ',' or ';' or '·' or '|')
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>"Legado — Checklist (CHK)" → "Checklist (CHK)" (o nome do projeto sem o prefixo do mundo).</summary>
    private static string StripWorld(string name) =>
        Regex.Replace(name, @"^(Revamp|Legado|Legacy|Front-?end|Integra(ç|c)(ã|a)o|Infra(estrutura)?)\s*[—–-]\s*", "", RegexOptions.IgnoreCase).Trim();

    /// <summary>"Plano de Ação (legado) — lista planos" → "Plano de Ação".</summary>
    private static string Head(string part)
    {
        var cut = part.IndexOfAny(['(', '—', '–', ':']);
        return (cut > 0 ? part[..cut] : part).Trim();
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }

    public static string Normalize(string value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"\*\*M[óo]dulos:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModulesLine();

    [GeneratedRegex(@"\*\*Mecanismo:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex MechanismLine();

    [GeneratedRegex(@"\*\*Contrato:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ContractLine();

    [GeneratedRegex(@"\*\*Onde:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex WhereLine();

    [GeneratedRegex(@"\*\*Confirmar:?\*\*:?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmLine();

    [GeneratedRegex(@"\bext:[a-z0-9][a-z0-9._-]*")]
    private static partial Regex ExternalKey();

    [GeneratedRegex(@"[a-z0-9][a-z0-9._-]*[a-z0-9]")]
    private static partial Regex KeyToken();
}
