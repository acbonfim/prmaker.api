namespace Cime.ExecutionAgent;

/// <summary>
/// Instala o executor como serviço do usuário: LaunchAgent (macOS), systemd --user (Linux) ou tarefa no logon
/// (Windows). Copia o binário para ~/.prmake-agent/bin e desliga o vigia antigo da 0033 (prmake-card.sh agent).
/// </summary>
public static class Service
{
    private const string MacLabel = "br.app.softhouse.prmake-executor";
    private const string OldMacLabel = "br.app.softhouse.prmake-agent";
    private const string WindowsTask = "PRMake Executor";
    private const string LinuxUnit = "prmake-agent.service";

    private static string MacPlist => Path.Combine(Paths.Home, "Library", "LaunchAgents", MacLabel + ".plist");
    private static string OldMacPlist => Path.Combine(Paths.Home, "Library", "LaunchAgents", OldMacLabel + ".plist");
    private static string LinuxUnitPath => Path.Combine(Paths.Home, ".config", "systemd", "user", LinuxUnit);

    /// <summary>
    /// Standard, nunca Background: com Background o macOS estrangula CPU e disco do executor e de tudo que ele roda
    /// (claude, dotnet, git) — build/teste ~20x mais lentos (card 75648: 467 s contra 21 s).
    /// </summary>
    private const string StandardProcessType = "<key>ProcessType</key><string>Standard</string>";
    private const string BackgroundProcessType = "<key>ProcessType</key><string>Background</string>";

    public static async Task<int> InstallAsync()
    {
        var bin = CopySelf();
        await DisableOldWatcherAsync();
        if (Paths.IsMac)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MacPlist)!);
            await File.WriteAllTextAsync(MacPlist, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0"><dict>
                  <key>Label</key><string>{MacLabel}</string>
                  <key>ProgramArguments</key><array><string>{Xml(bin)}</string><string>run</string></array>
                  <key>RunAtLoad</key><true/>
                  <key>KeepAlive</key><true/>
                  <key>ThrottleInterval</key><integer>30</integer>
                  {StandardProcessType}
                  <key>EnvironmentVariables</key><dict>
                    <key>PATH</key><string>{Xml(Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin")}</string>
                    <key>HOME</key><string>{Xml(Paths.Home)}</string>
                    <key>LANG</key><string>en_US.UTF-8</string>{ExtraEnvPlist()}
                  </dict>
                  <key>StandardOutPath</key><string>{Xml(Path.Combine(Paths.Root, "service.log"))}</string>
                  <key>StandardErrorPath</key><string>{Xml(Path.Combine(Paths.Root, "service.log"))}</string>
                </dict></plist>
                """);
            await Shell.RunAsync("launchctl", ["unload", MacPlist]);
            var (code, output) = await Shell.RunAsync("launchctl", ["load", "-w", MacPlist]);
            Console.WriteLine(code == 0 ? $"executor ligado (LaunchAgent {MacLabel}); log: {Paths.Log}" : $"launchctl falhou: {output}");
            return code == 0 ? 0 : 1;
        }
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxUnitPath)!);
            await File.WriteAllTextAsync(LinuxUnitPath, $"""
                [Unit]
                Description=Executor do PRMake (Claude Code sem terminal)
                After=network-online.target

                [Service]
                ExecStart="{bin}" run
                Restart=always
                RestartSec=30
                Environment="PATH={Environment.GetEnvironmentVariable("PATH")}"{ExtraEnvSystemd()}

                [Install]
                WantedBy=default.target
                """);
            await Shell.RunAsync("systemctl", ["--user", "daemon-reload"]);
            var (code, output) = await Shell.RunAsync("systemctl", ["--user", "enable", "--now", LinuxUnit]);
            Console.WriteLine(code == 0 ? $"executor ligado (systemd --user {LinuxUnit}); log: {Paths.Log}" : $"systemctl falhou: {output}");
            return code == 0 ? 0 : 1;
        }
        if (Paths.IsWindows)
        {
            var action = $"powershell.exe -NoProfile -WindowStyle Hidden -Command \"& '{bin.Replace("'", "''")}' run\"";
            var (code, output) = await Shell.RunAsync("schtasks", ["/Create", "/TN", WindowsTask, "/SC", "ONLOGON", "/TR", action, "/RL", "LIMITED", "/F"]);
            if (code != 0)
            {
                Console.WriteLine($"schtasks falhou: {output}");
                return 1;
            }
            await Shell.RunAsync("schtasks", ["/Run", "/TN", WindowsTask]);
            Console.WriteLine($"executor ligado (tarefa \"{WindowsTask}\" no logon); log: {Paths.Log}");
            return 0;
        }
        Console.WriteLine("sistema sem instalação automática — rode 'prmake-agent run' por conta própria");
        return 1;
    }

    public static async Task<int> UninstallAsync()
    {
        if (Paths.IsMac)
        {
            await Shell.RunAsync("launchctl", ["unload", "-w", MacPlist]);
            if (File.Exists(MacPlist)) File.Delete(MacPlist);
        }
        else if (OperatingSystem.IsLinux())
        {
            await Shell.RunAsync("systemctl", ["--user", "disable", "--now", LinuxUnit]);
            if (File.Exists(LinuxUnitPath)) File.Delete(LinuxUnitPath);
        }
        else if (Paths.IsWindows)
        {
            await Shell.RunAsync("schtasks", ["/End", "/TN", WindowsTask]);
            await Shell.RunAsync("schtasks", ["/Delete", "/TN", WindowsTask, "/F"]);
        }
        Console.WriteLine("executor desligado (a máquina continua registrada; revogue em \"Meus executores\" se quiser)");
        return 0;
    }

    public static async Task<string> StatusAsync()
    {
        if (Paths.IsMac)
        {
            var (code, output) = await Shell.RunAsync("launchctl", ["list", MacLabel]);
            return code == 0 ? (output.Contains("\"PID\"") ? "ligado (rodando)" : "ligado (parado)") : "desligado";
        }
        if (OperatingSystem.IsLinux())
        {
            var (_, output) = await Shell.RunAsync("systemctl", ["--user", "is-active", LinuxUnit]);
            return output.Trim();
        }
        if (Paths.IsWindows)
        {
            var (code, output) = await Shell.RunAsync("schtasks", ["/Query", "/TN", WindowsTask]);
            return code == 0 ? (output.Contains("Running", StringComparison.OrdinalIgnoreCase) || output.Contains("Em execu", StringComparison.OrdinalIgnoreCase) ? "ligado (rodando)" : "ligado") : "desligado";
        }
        return "desconhecido";
    }

    /// <summary>Restart do serviço (depois de atualizar o binário à mão).</summary>
    public static async Task RestartAsync()
    {
        if (Paths.IsMac)
            await Shell.RunAsync("launchctl", ["kickstart", "-k", $"gui/{GetUid()}/{MacLabel}"]);
        else if (OperatingSystem.IsLinux())
            await Shell.RunAsync("systemctl", ["--user", "restart", LinuxUnit]);
        else if (Paths.IsWindows)
        {
            await Shell.RunAsync("schtasks", ["/End", "/TN", WindowsTask]);
            await Shell.RunAsync("schtasks", ["/Run", "/TN", WindowsTask]);
        }
    }

    /// <summary>
    /// Instalações até a 1.0.13 têm o LaunchAgent com ProcessType=Background, e o auto-update não relê o plist (o
    /// launchd reinicia com o que tem em memória). Corrige o arquivo e recarrega o serviço por um job auxiliar de uma
    /// vez só: o bootout do próprio executor mataria quem o chamasse. Com o arquivo corrigido não volta a rodar.
    /// </summary>
    public static async Task<bool> FixMacProcessTypeAsync()
    {
        if (!Paths.IsMac || !File.Exists(MacPlist)) return false;
        var plist = await File.ReadAllTextAsync(MacPlist);
        if (!plist.Contains(BackgroundProcessType, StringComparison.Ordinal)) return false;
        await File.WriteAllTextAsync(MacPlist, plist.Replace(BackgroundProcessType, StandardProcessType, StringComparison.Ordinal));

        var uid = GetUid();
        var helperLabel = MacLabel + ".reload";
        var helper = Path.Combine(Paths.Root, helperLabel + ".plist");
        var script = $"sleep 2; launchctl bootout gui/{uid}/{MacLabel}; launchctl bootstrap gui/{uid} '{MacPlist}'; " +
                     $"rm -f '{helper}'; launchctl remove {helperLabel}";
        await File.WriteAllTextAsync(helper, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>{helperLabel}</string>
              <key>ProgramArguments</key><array><string>/bin/sh</string><string>-c</string><string>{Xml(script)}</string></array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """);
        await Shell.RunAsync("launchctl", ["bootout", $"gui/{uid}/{helperLabel}"]);
        var (code, output) = await Shell.RunAsync("launchctl", ["bootstrap", $"gui/{uid}", helper]);
        if (code != 0)
        {
            Log.Warn($"plist corrigido para ProcessType=Standard, mas o recarregamento falhou ({output}) — vale no próximo login ou com 'prmake-agent install'");
            return false;
        }
        Log.Info("LaunchAgent em ProcessType=Background (CPU e disco estrangulados): plist corrigido para Standard, recarregando o serviço");
        return true;
    }

    private static string CopySelf()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("caminho do executor desconhecido");
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.Bin)!);
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Paths.Bin), StringComparison.Ordinal))
        {
            File.Copy(self, Paths.Bin, overwrite: true);
            if (!Paths.IsWindows)
                File.SetUnixFileMode(Paths.Bin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return Paths.Bin;
    }

    /// <summary>O vigia da 0033 retomaria as mesmas sessões que a fila retoma: desliga.</summary>
    private static async Task DisableOldWatcherAsync()
    {
        if (Paths.IsMac && File.Exists(OldMacPlist))
        {
            await Shell.RunAsync("launchctl", ["unload", OldMacPlist]);
            File.Delete(OldMacPlist);
            Console.WriteLine("vigia antigo (prmake-card.sh agent) desligado — o executor substitui");
        }
    }

    /// <summary>Variáveis que as skills usam e que o serviço não herdaria do terminal.</summary>
    private static IEnumerable<(string Key, string Value)> ExtraEnv() =>
        new[] { "EDV_SOLVACE_DIR", "REVAMP_DIR", "PRMAKE_API_BASE", "PRMAKE_WORKSPACE", "CLAUDE_CODE_GIT_BASH_PATH", "AWS_PROFILE", "SQLSERVER_CREDENTIALS",
                "PRMAKE_HOME", "PRMAKE_REPOS_ROOTS", "CARDS_DIR" }
            .Select(k => (k, Environment.GetEnvironmentVariable(k) ?? ""))
            .Where(kv => kv.Item2.Length > 0);

    private static string ExtraEnvPlist() =>
        string.Concat(ExtraEnv().Select(kv => $"\n        <key>{kv.Key}</key><string>{Xml(kv.Value)}</string>"));

    private static string ExtraEnvSystemd() =>
        string.Concat(ExtraEnv().Select(kv => $"\nEnvironment=\"{kv.Key}={kv.Value}\""));

    private static string Xml(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static string GetUid() => Environment.GetEnvironmentVariable("UID") is { Length: > 0 } uid ? uid : Shell.RunAsync("id", ["-u"]).Result.Output.Trim();
}
