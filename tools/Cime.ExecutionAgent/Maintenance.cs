using System.Net;

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
        var edv = Environment.GetEnvironmentVariable("EDV_SOLVACE_DIR") ?? Path.Combine(Paths.Home, "repos", "solvace", "edv-solvace");
        var revamp = Environment.GetEnvironmentVariable("REVAMP_DIR") ?? Path.Combine(Paths.Home, "repos", "solvace", "revamp_separado");
        Add("Repositório legado (edv-solvace)", Directory.Exists(edv), Directory.Exists(edv) ? edv : $"não encontrado em {edv} (EDV_SOLVACE_DIR)", warning: true);
        Add("Repositórios revamp", Directory.Exists(revamp), Directory.Exists(revamp) ? revamp : $"não encontrado em {revamp} (REVAMP_DIR)", warning: true);

        foreach (var tool in new[] { "git", "bash", "jq", "python3" })
        {
            var found = Shell.Which(tool) ?? (tool == "python3" && Paths.IsWindows ? Shell.Which("py") : null);
            Add($"Ferramenta {tool}", found is not null, found ?? "não encontrada no PATH");
        }

        // Banco: o mesmo teste da skill no início da análise (VPN + credencial), sem consulta.
        var sql = Path.Combine(Paths.ClaudeHome, "skills", "analisar-bug", "scripts", "sql-query.sh");
        var creds = Path.Combine(Paths.ClaudeHome, "sqlserver-credentials.json");
        if (!File.Exists(creds))
            Add("Acesso aos bancos (SQL Server)", false, $"sem credenciais em {creds} — análises que precisam do banco vão parar pedindo acesso", warning: true);
        else if (File.Exists(sql) && Shell.Which("bash") is { } bash)
        {
            foreach (var alias in new[] { "prod", "prod3", "prod4" })
            {
                var (code, output) = await Shell.RunAsync(bash, [sql, "--host", alias, "-d", "master", "--ping"], timeout: TimeSpan.FromSeconds(30), ct: ct);
                Add($"Banco {alias}", code == 0, code == 0 ? "acessível" : $"sem acesso (VPN/credencial?): {Last(output)}", warning: true);
            }
        }
        return checks;
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
            return;
        }
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
