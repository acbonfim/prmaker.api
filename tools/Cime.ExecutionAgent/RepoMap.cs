using System.Text;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>Um repositório do mapa: pasta local, tipo (regra do BranchStrategy) e de onde veio (scan, manual, env).</summary>
public sealed record RepoMapEntry(string Name, string Path, string? Kind, string? Source, bool Confirmed);

/// <summary>Conteúdo do <c>~/.prmake/repos.json</c> (0048).</summary>
public sealed class RepoMapFile
{
    public List<RepoMapEntry> Repos { get; } = [];
    public Dictionary<string, List<string>> Ambiguous { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Missing { get; } = [];
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Mapa dos repositórios da máquina (0048): nome do repositório pelo remote → pasta. Montado pelas skills
/// (<c>prmake-skills.sh repos scan</c>) e lido aqui para escolher a pasta onde o Claude abre, liberar com
/// <c>--add-dir</c> os repositórios fora dela e mostrar em "Meus executores". Arquivo, não variável: mudou, vale na
/// próxima execução, sem reinstalar o serviço.
/// </summary>
public static class RepoMap
{
    private static readonly StringComparison PathComparison = Paths.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static DateTimeOffset _lastScanAttempt = DateTimeOffset.MinValue;
    private static int _scanning;

    public static string FilePath => Path.Combine(Paths.PrmakeHome, "repos.json");
    public const string Command = "bash ~/.claude/skills/.prmake/prmake-skills.sh repos";
    private const int MaxReported = 150;

    /// <summary>Lê o mapa; ausente, inválido ou de outra versão → null.</summary>
    public static RepoMapFile? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1) return null;
            var map = new RepoMapFile { UpdatedAt = Str(root, "updatedAt") };
            if (root.TryGetProperty("repos", out var repos) && repos.ValueKind == JsonValueKind.Object)
                foreach (var p in repos.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object || Str(p.Value, "path") is not { Length: > 0 } path) continue;
                    var confirmed = p.Value.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;
                    map.Repos.Add(new RepoMapEntry(p.Name, path, Str(p.Value, "kind"), Str(p.Value, "source"), confirmed));
                }
            if (root.TryGetProperty("ambiguous", out var amb) && amb.ValueKind == JsonValueKind.Object)
                foreach (var p in amb.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Array)
                        map.Ambiguous[p.Name] = p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
            if (root.TryGetProperty("missing", out var missing) && missing.ValueKind == JsonValueKind.Array)
                map.Missing.AddRange(missing.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            return map;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Pastas do mapa que existem nesta máquina (caminho completo, no formato do sistema).</summary>
    public static List<string> Folders(RepoMapFile? map) =>
        map is null ? [] : map.Repos.Select(r => Full(r.Path)).Where(p => p is not null && Directory.Exists(p)).Select(p => p!).Distinct(Comparer).ToList();

    /// <summary>Pasta comum aos repositórios do mapa — null se não houver, ou se for a home ou a raiz do disco.</summary>
    public static string? CommonAncestor(IReadOnlyCollection<string> folders)
    {
        if (folders.Count == 0) return null;
        // Com um repositório só, a pasta dele (o Claude abre nele); com vários, o pai comum.
        var common = folders.Count == 1 ? folders.First() : Path.GetDirectoryName(folders.First());
        foreach (var f in folders.Skip(1))
            while (common is not null && !IsUnder(f, common) && !Same(f, common))
                common = Path.GetDirectoryName(common);
        if (common is null || Path.GetPathRoot(common) is { } root && Same(common, root)) return null;
        var home = Full(Paths.Home);
        if (home is not null && (Same(common, home) || IsUnder(home, common))) return null;
        return common;
    }

    /// <summary>Pastas a liberar com --add-dir: as do mapa fora do workspace, sem repetir as que estão dentro de outra.</summary>
    public static List<string> ExtraDirs(string workspace, IEnumerable<string> folders)
    {
        var result = new List<string>();
        foreach (var f in folders.OrderBy(x => x.Length))
        {
            if (IsUnder(f, workspace) || Same(f, workspace)) continue;
            if (result.Any(r => Same(f, r) || IsUnder(f, r))) continue;
            result.Add(f);
        }
        return result;
    }

    /// <summary>
    /// Sem mapa (executor atualizado antes de a pessoa abrir o Claude Code, ou instalação antiga): roda a busca das skills
    /// em segundo plano — no máximo uma tentativa por hora.
    /// </summary>
    public static async Task EnsureAsync(CancellationToken ct)
    {
        if (File.Exists(FilePath) || DateTimeOffset.UtcNow - _lastScanAttempt < TimeSpan.FromHours(1)) return;
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            _lastScanAttempt = DateTimeOffset.UtcNow;
            var tool = Path.Combine(Paths.ClaudeHome, "skills", ".prmake", "prmake-skills.sh");
            if (!File.Exists(tool) || Shell.Bash() is not { } bash)
            {
                Log.Warn($"sem mapa de repositórios e sem como montar (ferramenta das skills ou bash ausente) — rode: {Command} scan");
                return;
            }
            Log.Info("sem mapa de repositórios — procurando os repositórios desta máquina");
            var (code, output) = await Shell.RunAsync(bash, [tool, "repos", "scan", "--quiet", "--mark"], timeout: TimeSpan.FromMinutes(3), ct: ct);
            if (code == 0 && Load() is { } map)
                Log.Info($"mapa de repositórios: {map.Repos.Count} repositório(s)" +
                         (map.Ambiguous.Count > 0 ? $", ambíguos: {string.Join(", ", map.Ambiguous.Keys)}" : "") +
                         (map.Missing.Count > 0 ? $", sem clone: {string.Join(", ", map.Missing)}" : ""));
            else
                Log.Warn($"não consegui montar o mapa de repositórios ({code}): {Last(output)}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn($"busca dos repositórios falhou: {e.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    /// <summary><c>repoMap</c> do report (contrato da 0048) — null sem mapa.</summary>
    public static void WriteCapabilities(Utf8JsonWriter w, RepoMapFile? map)
    {
        if (map is null) return;
        w.WriteStartObject("repoMap");
        w.WriteStartArray("items");
        // Limite: o PRMake recusa o report inteiro acima de 64 KB (o resto continua no arquivo e no doctor).
        foreach (var r in map.Repos.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(MaxReported))
        {
            w.WriteStartObject();
            w.WriteString("name", r.Name);
            w.WriteString("path", r.Path);
            if (r.Kind is { Length: > 0 }) w.WriteString("kind", r.Kind);
            if (Branch(r.Path) is { } b) w.WriteString("branch", b);
            else if (!Directory.Exists(Full(r.Path) ?? r.Path)) w.WriteBoolean("gone", true);
            if (r.Source is { Length: > 0 }) w.WriteString("source", r.Source);
            w.WriteBoolean("confirmed", r.Confirmed);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if (map.Repos.Count > MaxReported) w.WriteNumber("total", map.Repos.Count);
        w.WriteStartObject("ambiguous");
        foreach (var (name, paths) in map.Ambiguous)
        {
            w.WriteStartArray(name);
            foreach (var p in paths) w.WriteStringValue(p);
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteStartArray("missing");
        foreach (var m in map.Missing) w.WriteStringValue(m);
        w.WriteEndArray();
        if (map.UpdatedAt is { } at) w.WriteString("updatedAt", at);
        w.WriteEndObject();
    }

    /// <summary>Branch atual lendo o <c>.git/HEAD</c> (sem subir o git a cada report); commit curto se destacado.</summary>
    public static string? Branch(string repo)
    {
        try
        {
            var full = Full(repo);
            if (full is null) return null;
            var git = Path.Combine(full, ".git");
            string? gitDir = Directory.Exists(git) ? git : null;
            if (gitDir is null && File.Exists(git) && File.ReadAllText(git).Trim() is var link && link.StartsWith("gitdir:", StringComparison.Ordinal))
                gitDir = Path.GetFullPath(Path.Combine(full, link["gitdir:".Length..].Trim()));
            if (gitDir is null) return null;
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            return head.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? head["ref: refs/heads/".Length..] : head.Length >= 7 ? head[..7] : head;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Linha do doctor: (ok, mensagem).</summary>
    public static (bool Ok, string Message) DoctorLine(RepoMapFile? map)
    {
        if (map is null) return (false, $"sem mapa — os repositórios desta máquina não foram procurados; rode: {Command} scan");
        var folders = Folders(map);
        var gone = map.Repos.Count - folders.Count;
        var sb = new StringBuilder($"{folders.Count} repositório(s)");
        if (CommonAncestor(folders) is { } common) sb.Append($" em {common}");
        var ok = map.Ambiguous.Count == 0 && map.Missing.Count == 0 && gone == 0 && folders.Count > 0;
        if (map.Ambiguous.Count > 0) sb.Append($"; mais de um clone de: {string.Join(", ", map.Ambiguous.Keys)}");
        if (map.Missing.Count > 0) sb.Append($"; sem clone: {string.Join(", ", map.Missing)}");
        if (gone > 0) sb.Append($"; {gone} pasta(s) do mapa sumiram");
        if (!ok) sb.Append($" — confira: {Command}");
        return (ok, sb.ToString());
    }

    private static readonly StringComparer Comparer = Paths.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string? Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    private static bool Same(string a, string b) => string.Equals(Full(a), Full(b), PathComparison);

    private static bool IsUnder(string path, string parent)
    {
        var p = Full(path);
        var d = Full(parent);
        if (p is null || d is null) return false;
        return p.StartsWith(d.EndsWith(Path.DirectorySeparatorChar) ? d : d + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Last(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        return line.Length > 200 ? line[..200] : line;
    }
}
