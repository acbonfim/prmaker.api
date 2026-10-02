using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

/// <summary>
/// 0050: o que o Claude está fazendo agora, para a tela do card. Cada <c>tool_use</c> do stream vira um rótulo curto
/// em pt-BR ("Consultando o banco", "Lendo Foo.cs") — nunca o comando cru: comandos de banco levam host e credencial,
/// e a tela é de quem acompanha o card. A API ainda descarta o que parecer segredo.
/// </summary>
public static partial class ActivityLabel
{
    private const int MaxTerm = 40;

    /// <summary>Rótulo de um bloco <c>tool_use</c>; null = não vale mostrar (ex.: lista de tarefas interna).</summary>
    public static ActivityItem? From(JsonElement toolUse, IReadOnlyList<RepoMapEntry> repos, string workspace)
    {
        var name = Str(toolUse, "name");
        if (string.IsNullOrEmpty(name)) return null;
        var input = toolUse.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        var label = name switch
        {
            "Bash" or "PowerShell" => FromCommand(Str(input, "command") ?? string.Empty, repos, workspace),
            "Read" => $"Lendo {File(Str(input, "file_path"), repos, workspace)}",
            "Grep" => Search(Str(input, "pattern"), Where(Str(input, "path"), repos, workspace)),
            "Glob" => $"Listando arquivos{In(Where(Str(input, "path"), repos, workspace))}",
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => $"Editando {File(Str(input, "file_path") ?? Str(input, "notebook_path"), repos, workspace)}",
            "WebFetch" or "WebSearch" => "Pesquisando na web",
            "Task" or "Agent" => Str(input, "description") is { Length: > 0 } d ? $"Subagente: {Cut(d, 60)}" : "Rodando um subagente",
            "Skill" => Str(input, "skill") is { Length: > 0 } s ? $"Carregando a skill {Cut(s, 40)}" : "Carregando uma skill",
            "TodoWrite" or "ToolSearch" or "ExitPlanMode" => null,
            _ when name.StartsWith("mcp__prmake__", StringComparison.Ordinal) => Prmake(name["mcp__prmake__".Length..].Replace("prmake_", ""), input),
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => $"Usando {Cut(name.Split("__")[1].Replace('_', ' '), 40)}",
            _ => $"Usando {Cut(name, 40)}"
        };
        return label is null ? null : new ActivityItem { Label = label, Tool = name, At = DateTimeOffset.UtcNow };
    }

    // ── Bash ─────────────────────────────────────────────────────────────────────────────────────

    internal static string FromCommand(string command, IReadOnlyList<RepoMapEntry> repos, string workspace)
    {
        var where = Where(CdTarget(command), repos, workspace) ?? RepoIn(command, repos);
        var c = command.ToLowerInvariant();

        if (Contains(c, "sql-query.sh", "cognito-query.sh", "mysql ", "psql ", "sqlcmd", "db-credentials"))
            return "Consultando o banco";
        if (Contains(c, "kc.sh", "knowledge"))
            return "Consultando o Knowledge Center";
        if (Contains(c, "solvace-kb", "base-solvace"))
            return "Consultando a Base Solvace";
        if (Contains(c, "bug-fetch.sh", "prmake-card.sh"))
            return "Lendo o card no DevOps";
        if (PlanScript().Match(command) is { Success: true } plan)
            return PlanCommand(plan.Groups[1].Value.ToLowerInvariant());

        var segment = MainSegment(command);
        var tokens = Tokens(segment);
        if (tokens.Count == 0) return "Executando comando";
        var tool = Path.GetFileName(tokens[0]);
        switch (tool)
        {
            case "grep" or "egrep" or "fgrep" or "rg" or "ag":
                return Search(tokens.Skip(1).FirstOrDefault(t => !t.StartsWith('-')), where);
            case "find" or "ls" or "tree" or "fd":
                return $"Listando arquivos{In(where)}";
            case "cat" or "head" or "tail" or "less" or "sed" or "awk" or "wc":
                var file = tokens.Skip(1).LastOrDefault(t => !t.StartsWith('-') && t.Contains('.') && !t.Contains('\'') && !t.Contains('"'));
                return file is null ? $"Lendo arquivos{In(where)}" : $"Lendo {File(file, repos, workspace)}";
            case "git":
                string? sub = null;
                for (var k = 1; k < tokens.Count && sub is null; k++)
                {
                    if (tokens[k] is "-C" or "-c") { k++; continue; }
                    if (!tokens[k].StartsWith('-')) sub = tokens[k];
                }
                return sub is null ? $"Git{In(where)}" : $"Git: {Cut(sub, 20)}{In(where)}";
            case "gh":
                var parts = tokens.Skip(1).Where(t => !t.StartsWith('-')).Take(2).ToList();
                return parts.Count == 0 ? "GitHub" : $"GitHub: {Cut(string.Join(' ', parts), 30)}";
            case "dotnet" or "npm" or "npx" or "ng" or "yarn" or "pnpm" or "mvn" or "gradle" or "make":
                return $"Compilando / rodando {tool}{In(where)}";
            case "curl" or "wget":
                return "Chamando uma API";
            case "jq" or "python3" or "python" or "node":
                return $"Processando dados{In(where)}";
            default:
                return $"Executando comando{In(where)}";
        }
    }

    private static string PlanCommand(string cmd) => cmd switch
    {
        "advance" or "step" or "steps" or "status" or "block" or "unblock" or "checkpoint" or "correction" or "use" => "Atualizando o plano",
        "upload" or "sync" or "file" => "Gravando arquivos no plano",
        "ask" => "Fazendo perguntas no plano",
        "wait-answers" or "watch" => "Aguardando respostas",
        "log" or "timeline" => "Registrando na Timeline",
        "notes" or "attachment" => "Lendo os comentários do plano",
        "devops" => "Atualizando o card no DevOps",
        "branches" or "repos" or "where" => "Conferindo repositórios e branches",
        "open-pr" or "pr" => "Abrindo o PR",
        "settings" or "config" => "Lendo a configuração do PRMake",
        _ => "Lendo o plano"
    };

    private static string Prmake(string tool, JsonElement input) => tool switch
    {
        "card" => "Lendo o card no DevOps",
        "plan" or "control" or "queue" => "Lendo o plano",
        "notes" or "attachment" => "Lendo os comentários do plano",
        "answers" => "Lendo as respostas",
        "advance" or "step" or "steps" or "block" or "unblock" or "plan_status" or "correction" or "checkpoint" => "Atualizando o plano",
        "ask" => "Fazendo perguntas no plano",
        "answer" => "Registrando resposta",
        "file" => Str(input, "name") is { Length: > 0 } n ? $"Gravando {Cut(Path.GetFileName(n), 50)} no plano" : "Gravando arquivo no plano",
        "link" => "Anexando link na etapa",
        "log" or "timeline" => "Registrando na Timeline",
        "devops" => "Atualizando o card no DevOps",
        "config" or "devops_config" => "Lendo a configuração do PRMake",
        _ => $"PRMake: {Cut(tool.Replace('_', ' '), 30)}"
    };

    // ── Partes do rótulo ─────────────────────────────────────────────────────────────────────────

    private static string Search(string? term, string? where)
    {
        var t = Unquote(term);
        return t is null || LooksSensitive(t) ? $"Procurando no código{In(where)}" : $"Procurando “{Cut(t, MaxTerm)}”{In(where)}";
    }

    private static string In(string? where) => where is null ? string.Empty : $" no {where}";

    /// <summary>Nome do arquivo (sem caminho) e o repositório, quando dá para saber.</summary>
    private static string File(string? path, IReadOnlyList<RepoMapEntry> repos, string workspace)
    {
        var p = Unquote(path);
        if (p is null) return "um arquivo";
        var name = Cut(Path.GetFileName(p.TrimEnd('/', '\\')), 60);
        var repo = Where(p, repos, workspace);
        return repo is null ? name : $"{name} ({repo})";
    }

    /// <summary>Repositório do mapa (o de caminho mais longo que contém o arquivo) ou a pasta do workspace.</summary>
    internal static string? Where(string? path, IReadOnlyList<RepoMapEntry> repos, string workspace)
    {
        var p = Unquote(path);
        if (string.IsNullOrEmpty(p)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(p.StartsWith('~') ? Path.Combine(Paths.Home, p.TrimStart('~', '/')) : p, workspace);
        }
        catch
        {
            return null;
        }
        var best = repos
            .Select(r => (r.Name, Path: SafeFull(r.Path)))
            .Where(r => r.Path is not null && (full == r.Path || full.StartsWith(r.Path + Path.DirectorySeparatorChar)))
            .OrderByDescending(r => r.Path!.Length)
            .FirstOrDefault();
        if (best.Name is not null) return Cut(best.Name, 40);
        var root = SafeFull(workspace);
        if (root is not null && full.StartsWith(root + Path.DirectorySeparatorChar))
            return Cut(full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar)[0], 40);
        return null;
    }

    private static string? RepoIn(string command, IReadOnlyList<RepoMapEntry> repos) =>
        repos.Where(r => r.Name.Length > 2 && command.Contains(r.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Name.Length).Select(r => Cut(r.Name, 40)).FirstOrDefault();

    private static string? CdTarget(string command) =>
        CdPattern().Match(command) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>O primeiro trecho que não é <c>cd</c>/<c>export</c> (em <c>cd x &amp;&amp; grep ...</c>, o grep).</summary>
    private static string MainSegment(string command)
    {
        foreach (var part in SegmentSplit().Split(command))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            var first = p.Split(' ', 2)[0];
            if (first is "cd" or "export" or "set" or "source" or "." or "pushd" or "true" or "echo" || first.Contains('=')) continue;
            return p;
        }
        return command;
    }

    private static List<string> Tokens(string segment) =>
        TokenPattern().Matches(segment).Select(m => m.Value).Where(t => t.Length > 0).ToList();

    private static string? Unquote(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0]) v = v[1..^1];
        return v.Length == 0 ? null : v;
    }

    /// <summary>Termo com cara de credencial (senha=, token, connection string) não aparece na tela.</summary>
    private static bool LooksSensitive(string term) =>
        term.Contains('=') || SensitiveTerm().IsMatch(term);

    private static bool Contains(string text, params string[] needles) => needles.Any(text.Contains);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string? SafeFull(string? path)
    {
        try
        {
            return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path.StartsWith('~') ? Path.Combine(Paths.Home, path.TrimStart('~', '/')) : path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"(?:prmake-plan\.sh[""']?|\$\{?PLAN\}?[""']?)\s+([a-z-]+)")]
    private static partial Regex PlanScript();

    [GeneratedRegex(@"(?:^|&&|;)\s*cd\s+(""[^""]+""|'[^']+'|[^\s;&|]+)")]
    private static partial Regex CdPattern();

    [GeneratedRegex(@"&&|\|\||;|\|")]
    private static partial Regex SegmentSplit();

    [GeneratedRegex(@"""[^""]*""|'[^']*'|\S+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)pass(word)?|pwd|secret|token|api[-_]?key|bearer")]
    private static partial Regex SensitiveTerm();
}

public sealed class ActivityItem
{
    public string Label { get; set; } = string.Empty;
    public string? Tool { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// 0050: atividade atual + as últimas <see cref="MaxRecent"/>, com aviso de mudança para o laço do heartbeat mandar
/// na hora (respeitando o intervalo mínimo). Lido pela thread do heartbeat, escrito pela leitura do stdout.
/// </summary>
public sealed class ActivityTracker
{
    public const int MaxRecent = 10;
    private readonly object _lock = new();
    private readonly LinkedList<ActivityItem> _recent = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _dirty;

    public bool Dirty
    {
        get { lock (_lock) return _dirty; }
    }

    public void Add(ActivityItem item)
    {
        lock (_lock)
        {
            if (_recent.First?.Value is { } last && last.Label == item.Label)
                _recent.RemoveFirst(); // mesma atividade repetida: só atualiza a hora
            _recent.AddFirst(item);
            while (_recent.Count > MaxRecent) _recent.RemoveLast();
            _dirty = true;
            _changed.TrySetResult();
        }
    }

    /// <summary>Completa na próxima mudança (já sujo = só quando o laço pegar o que está pendente).</summary>
    public Task Changed()
    {
        lock (_lock) return _dirty ? Task.Delay(Timeout.Infinite) : _changed.Task;
    }

    /// <summary>O que vai no heartbeat (e marca como enviado).</summary>
    public (ActivityItem? Current, List<ActivityItem>? Recent) Take()
    {
        lock (_lock)
        {
            _dirty = false;
            if (_changed.Task.IsCompleted) _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _recent.Count == 0 ? (null, null) : (_recent.First!.Value, _recent.ToList());
        }
    }
}
