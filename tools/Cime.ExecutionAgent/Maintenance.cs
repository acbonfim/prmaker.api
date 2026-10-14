using System.Net;
using System.Xml.Linq;

namespace Cime.ExecutionAgent;

/// <summary>
/// Checagens da máquina (0039): o que faltar aparece em "Meus executores" ANTES de uma análise precisar (mesmo cuidado
/// da 0037 com falta de permissão).
/// </summary>
public static class Doctor
{
    public static async Task<List<DoctorCheck>> RunChecksAsync(AgentConfig config, PrmakeClient? client, CancellationToken ct)
    {
        var checks = new List<DoctorCheck>();
        void Add(string name, bool ok, string? message, bool warning = false) =>
            checks.Add(new DoctorCheck { Name = name, Ok = ok, Message = message, Severity = warning ? "warning" : "error" });

        var claude = ClaudeLocator.Find(config);
        var version = claude is null ? null : await ClaudeLocator.VersionAsync(config);
        Add("Claude Code", version is not null, version ?? (claude is null ? "não encontrado no PATH (instale: https://claude.com/claude-code)" : $"não respondeu ({claude})"));

        if (claude is not null)
        {
            var psi = ClaudeLocator.StartInfo(claude, ["auth", "status"]);
            var (code, output) = await Shell.RunAsync(psi.FileName, psi.ArgumentList, timeout: TimeSpan.FromSeconds(20), ct: ct);
            var loggedOut = output.Contains("not logged", StringComparison.OrdinalIgnoreCase) || output.Contains("no auth", StringComparison.OrdinalIgnoreCase);
            Add("Login do Claude Code", code == 0 && !loggedOut, code == 0 && !loggedOut ? "ok" : "rode 'claude' no terminal e faça login (/login)", warning: code != 0 && !loggedOut);

            var mcp = ClaudeLocator.StartInfo(claude, ["mcp", "get", "prmake"]);
            var (mcpCode, _) = await Shell.RunAsync(mcp.FileName, mcp.ArgumentList, timeout: TimeSpan.FromSeconds(20), ct: ct);
            Add("MCP do PRMake", mcpCode == 0, mcpCode == 0 ? "registrado" : "não registrado — rode: bash ~/.claude/skills/.prmake/prmake-skills.sh mcp", warning: true);
        }

        var skill = Path.Combine(Paths.ClaudeHome, "skills", "analisar-bug", "SKILL.md");
        Add("Skills do PRMake", File.Exists(skill), File.Exists(skill) ? $"analisar-bug {Runner.SkillsVersion() ?? "?"}" : "não instaladas — use o comando da tela Skills do PRMake");

        var userToken = ConfigStore.UserToken();
        if (userToken is null)
            Add("api-key do usuário", false, $"não encontrada em {Paths.UserTokenFile} (a skill usa para gravar no PRMake)");
        else
        {
            using var userClient = new PrmakeClient(ConfigStore.ResolveApiBase(config), userToken);
            var status = await SafeProbe(userClient, "Skills/config", ct);
            Add("api-key do usuário", status == HttpStatusCode.OK, status == HttpStatusCode.OK ? "válida" : $"recusada pelo PRMake ({(int?)status}) — gere outra na tela Skills");
        }

        if (client is not null)
        {
            var status = await SafeProbe(client, "ExecutionWorker/me", ct);
            Add("Credencial do executor", status == HttpStatusCode.OK, status == HttpStatusCode.OK ? "válida" : $"recusada ({(int?)status}) — rode: prmake-agent register");
        }

        var workspace = ConfigStore.ResolveWorkspace(config);
        Add("Workspace", Directory.Exists(workspace), workspace);
        // 0048: os repositórios vêm do mapa da máquina (prmake-skills.sh repos), não de caminho fixo.
        var (mapOk, mapMessage) = RepoMap.DoctorLine(RepoMap.Load());
        Add("Mapa de repositórios", mapOk, mapMessage, warning: true);
        foreach (var (name, value) in new[] { ("EDV_SOLVACE_DIR", Environment.GetEnvironmentVariable("EDV_SOLVACE_DIR")), ("REVAMP_DIR", Environment.GetEnvironmentVariable("REVAMP_DIR")) })
            if (value is { Length: > 0 } && !Directory.Exists(value))
                Add(name, false, $"a variável aponta para uma pasta que não existe: {value}", warning: true);

        foreach (var tool in new[] { "git", "bash", "jq", "python3" })
        {
            var found = Shell.Which(tool) ?? (tool == "python3" && Paths.IsWindows ? Shell.Which("py") : null);
            Add($"Ferramenta {tool}", found is not null, found ?? "não encontrada no PATH");
        }

        if (await CodeArtifactCheckAsync(ct) is { } codeArtifact)
            checks.Add(codeArtifact);

        // Banco: o mesmo teste da skill no início da análise (VPN + credencial), sem consulta.
        var sql = Path.Combine(Paths.ClaudeHome, "skills", "analisar-bug", "scripts", "sql-query.sh");
        var creds = Path.Combine(Paths.ClaudeHome, "sqlserver-credentials.json");
        if (!File.Exists(creds))
            Add("Acesso aos bancos (SQL Server)", false, $"sem credenciais em {creds} — análises que precisam do banco vão parar pedindo acesso", warning: true);
        else if (File.Exists(sql) && Shell.Bash() is { } bash)
        {
            foreach (var alias in new[] { "prod", "prod3", "prod4" })
            {
                var (code, output) = await Shell.RunAsync(bash, [sql, "--host", alias, "-d", "master", "--ping"], timeout: TimeSpan.FromSeconds(30), ct: ct);
                Add($"Banco {alias}", code == 0, code == 0 ? "acessível" : $"sem acesso (VPN/credencial?): {Last(output)}", warning: true);
            }
        }
        return checks;
    }

    private const string CodeArtifactProviderInstall =
        "dotnet tool install -g AWS.CodeArtifact.NuGet.CredentialProvider && dotnet codeartifact-creds install";

    /// <summary>
    /// Token do CodeArtifact no NuGet.Config dura 12 h e o <c>aws codeartifact login</c> só renova a fonte que ele
    /// criou — vencido, o restore do revamp cai em 401 (card 75648). O credential provider renova sozinho. Máquina sem
    /// CodeArtifact (nem fonte, nem credencial, nem provider) → sem checagem.
    /// </summary>
    private static async Task<DoctorCheck?> CodeArtifactCheckAsync(CancellationToken ct)
    {
        var config = Path.Combine(Paths.IsWindows ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) : Path.Combine(Paths.Home, ".nuget"),
            "NuGet", "NuGet.Config");
        var provider = Directory.Exists(Path.Combine(Paths.Home, ".nuget", "plugins", "netcore", "AWS.CodeArtifact.NuGetCredentialProvider"));
        string? url = null;
        var credentials = new List<(string Source, string Password)>();
        if (File.Exists(config))
        {
            try
            {
                var xml = XDocument.Load(config);
                url = xml.Descendants("packageSources").Elements("add").Select(e => (string?)e.Attribute("value"))
                    .FirstOrDefault(v => v?.Contains(".codeartifact.", StringComparison.OrdinalIgnoreCase) == true);
                foreach (var source in xml.Descendants("packageSourceCredentials").Elements())
                {
                    string? Value(string key) => (string?)source.Elements("add").FirstOrDefault(a => (string?)a.Attribute("key") == key)?.Attribute("value");
                    if (Value("Username") == "aws" && Value("ClearTextPassword") is { Length: > 0 } password)
                        credentials.Add((System.Xml.XmlConvert.DecodeName(source.Name.LocalName), password));
                }
            }
            catch (Exception e)
            {
                return Check(false, $"não consegui ler {config}: {e.Message}");
            }
        }
        if (url is null && credentials.Count == 0 && !provider) return null;

        var expired = new List<string>();
        if (url is not null)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            foreach (var (source, password) in credentials)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aws:" + password)));
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) expired.Add(source);
                }
                catch
                {
                    // sem rede agora: não acusa token vencido
                }
            }
        }

        if (expired.Count > 0)
            return Check(false, $"token vencido em {string.Join(", ", expired)} no {config} (restore do revamp falha) — " +
                (provider ? "apague essas credenciais: o credential provider instalado renova sozinho"
                          : $"instale o credential provider ({CodeArtifactProviderInstall}) e apague as credenciais, ou rode aws codeartifact login"));
        if (provider)
            return Check(true, credentials.Count == 0 ? "credential provider instalado (renova o token sozinho)"
                : "credential provider instalado — apague as credenciais fixas do NuGet.Config, elas vencem em 12 h e têm prioridade");
        if (credentials.Count > 0)
            return Check(true, $"token válido, mas vence em 12 h — instale o credential provider: {CodeArtifactProviderInstall}");
        return Check(false, $"fonte do CodeArtifact sem credencial — instale o credential provider: {CodeArtifactProviderInstall}");

        static DoctorCheck Check(bool ok, string message) =>
            new() { Name = "NuGet CodeArtifact", Ok = ok, Message = message, Severity = "warning" };
    }

    private static async Task<HttpStatusCode?> SafeProbe(PrmakeClient client, string path, CancellationToken ct)
    {
        try
        {
            return await client.ProbeAsync(path, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string Last(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        return line.Length > 200 ? line[..200] : line;
    }
}

/// <summary>
/// Worktrees por card (0039): a skill cria em <c>&lt;pasta-do-repo&gt;/../.prmake-wt/&lt;card&gt;/&lt;repo&gt;</c> para dois
/// cards nunca dividirem o mesmo checkout. Plano terminado há mais de N dias (ou card sem plano) → remove.
/// </summary>
public static class Worktrees
{
    public static async Task CleanAsync(AgentConfig config, PrmakeClient client, CancellationToken ct)
    {
        var workspace = ConfigStore.ResolveWorkspace(config);
        var roots = new List<string>();
        try
        {
            roots.AddRange(Directory.EnumerateDirectories(workspace, ".prmake-wt", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }));
        }
        catch
        {
            // workspace sumiu — ainda olha ao lado dos repositórios do mapa
        }
        // 0048: o worktree fica em <pasta-do-repo>/../.prmake-wt — repositórios do mapa fora do workspace também.
        foreach (var repo in RepoMap.Folders(RepoMap.Load()))
            if (Path.GetDirectoryName(repo) is { } parent && Path.Combine(parent, ".prmake-wt") is var wtRoot && Directory.Exists(wtRoot))
                roots.Add(wtRoot);
        roots = roots.Select(Path.GetFullPath).Distinct(Paths.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
        var retention = TimeSpan.FromDays(Math.Max(1, config.WorktreeRetentionDays));
        foreach (var root in roots)
        {
            foreach (var cardDir in Directory.EnumerateDirectories(root))
            {
                var card = Path.GetFileName(cardDir);
                var age = DateTime.UtcNow - Directory.GetLastWriteTimeUtc(cardDir);
                if (age < retention) continue;
                string? status = null;
                try
                {
                    status = (await client.CurrentPlanAsync(card, ct))?.Status;
                }
                catch
                {
                    continue; // sem resposta do PRMake: tenta na próxima
                }
                if (status is not (null or "completed" or "cancelled")) continue;

                foreach (var wt in Directory.EnumerateDirectories(cardDir))
                {
                    var (code, common) = await Shell.RunAsync("git", ["-C", wt, "rev-parse", "--path-format=absolute", "--git-common-dir"], ct: ct);
                    if (code == 0)
                        await Shell.RunAsync("git", ["--git-dir", common.Trim(), "worktree", "remove", "--force", wt], ct: ct);
                    if (Directory.Exists(wt))
                    {
                        try { Directory.Delete(wt, recursive: true); } catch { /* fica para a próxima */ }
                    }
                    if (code == 0)
                        await Shell.RunAsync("git", ["--git-dir", common.Trim(), "worktree", "prune"], ct: ct);
                }
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(cardDir).Any()) Directory.Delete(cardDir);
                    Log.Info($"worktrees do card {card} removidas ({status ?? "sem plano"}, {age.TotalDays:0} dias)");
                }
                catch
                {
                    // fica para a próxima
                }
            }
        }
    }
}

/// <summary>Atualização do executor pelo próprio PRMake (binário publicado na imagem da API).</summary>
public static class Updater
{
    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) ? l > c : !string.Equals(latest, current, StringComparison.Ordinal);

    public static async Task<bool> UpdateAsync(PrmakeClient client, CancellationToken ct)
    {
        var self = Environment.ProcessPath;
        if (self is null || !File.Exists(self)) return false;
        var tmp = self + ".new";
        try
        {
            await client.DownloadAgentAsync(Paths.Rid, tmp, ct);
            if (!Paths.IsWindows)
                File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            if (Paths.IsMac)
                await Shell.RunAsync("codesign", ["--force", "--sign", "-", tmp], ct: ct);
            var (code, output) = await Shell.RunAsync(tmp, ["version"], timeout: TimeSpan.FromSeconds(20), ct: ct);
            if (code != 0)
            {
                Log.Warn($"binário novo não rodou ({output}) — mantendo a versão atual");
                File.Delete(tmp);
                return false;
            }
            // Windows deixa renomear o executável em uso (não sobrescrever).
            var old = self + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(self, old);
            File.Move(tmp, self);
            Log.Info($"executor atualizado para {output.Trim()}");
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"atualização falhou: {e.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignora */ }
            return false;
        }
    }

    /// <summary>
    /// macOS/Linux: o LaunchAgent/systemd reinicia ao sair. Windows (Agendador de Tarefas não reinicia): sobe a versão
    /// nova e sai (o lock garante uma instância só).
    /// </summary>
    public static void Restart()
    {
        if (Paths.IsWindows && Environment.ProcessPath is { } self)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--wait-lock");
            System.Diagnostics.Process.Start(psi);
        }
        Environment.Exit(75);
    }
}
