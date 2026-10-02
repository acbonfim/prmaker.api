using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Cime.ExecutionAgent;

/// <summary>Pastas e arquivos do executor.</summary>
public static class Paths
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Root => Environment.GetEnvironmentVariable("PRMAKE_AGENT_HOME") is { Length: > 0 } h ? h : Path.Combine(Home, ".prmake-agent");
    public static string Config => Path.Combine(Root, "config.json");
    public static string Log => Path.Combine(Root, "agent.log");
    public static string Lock => Path.Combine(Root, "run.lock");
    public static string Bin => Path.Combine(Root, "bin", IsWindows ? "prmake-agent.exe" : "prmake-agent");
    public static string ClaudeSettings => Path.Combine(Root, "claude-settings.json");
    /// <summary>Pasta do Claude Code (CLAUDE_CONFIG_DIR, como o próprio Claude Code, ou ~/.claude).</summary>
    public static string ClaudeHome => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : Path.Combine(Home, ".claude");
    public static string UserTokenFile => Path.Combine(ClaudeHome, "prmake-token.txt");
    /// <summary>
    /// Pastas dos cards da skill (CARDS_DIR ou ~/.prmake/cards — 0046). Fora de ~/.claude: o Claude Code protege aquela
    /// pasta e, em dontAsk, nega gravar nela mesmo com Write liberado (scripts e análises do card não eram salvos).
    /// </summary>
    /// <summary>Pasta do PRMake na máquina (PRMAKE_HOME ou ~/.prmake): cards (0046) e o mapa de repositórios (0048).</summary>
    public static string PrmakeHome => Environment.GetEnvironmentVariable("PRMAKE_HOME") is { Length: > 0 } p ? p : Path.Combine(Home, ".prmake");
    public static string CardsRoot => Environment.GetEnvironmentVariable("CARDS_DIR") is { Length: > 0 } d ? d : Path.Combine(Home, ".prmake", "cards");

    /// <summary>Cria a pasta dos cards (liberada ao Claude com --add-dir) e devolve o caminho.</summary>
    public static string EnsureCardsRoot()
    {
        Directory.CreateDirectory(CardsRoot);
        return CardsRoot;
    }
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();

    public static string Rid
    {
        get
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            if (IsWindows) return "win-x64";
            if (IsMac) return $"osx-{arch}";
            return $"linux-{arch}";
        }
    }

    public static string OsDescription => $"{RuntimeInformation.OSDescription.Trim()} ({Rid})";

    public static string HostName => Environment.MachineName.Split('.')[0];
}

public static class Agent
{
    public static string Version =>
        typeof(Agent).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}

/// <summary>Log em arquivo com rotação (5 MB, mantém um .1) e espelho no console.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static bool Console { get; set; } = true;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERRO", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.Root);
                var info = new FileInfo(Paths.Log);
                if (info.Exists && info.Length > 5 * 1024 * 1024)
                {
                    File.Copy(Paths.Log, Paths.Log + ".1", overwrite: true);
                    File.WriteAllText(Paths.Log, string.Empty);
                }
                File.AppendAllText(Paths.Log, line + Environment.NewLine);
            }
            catch
            {
                // log nunca derruba o executor
            }
            if (Console) System.Console.Error.WriteLine(line);
        }
    }
}

public static class ConfigStore
{
    public static AgentConfig Load()
    {
        if (!File.Exists(Paths.Config)) return new AgentConfig();
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(Paths.Config), AgentConfigJson.Default.AgentConfig) ?? new AgentConfig();
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"config inválida em {Paths.Config}: {e.Message}");
        }
    }

    public static void Save(AgentConfig config)
    {
        Directory.CreateDirectory(Paths.Root);
        var tmp = Paths.Config + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, AgentConfigJson.Default.AgentConfig));
        if (!Paths.IsWindows)
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, Paths.Config, overwrite: true);
    }

    /// <summary>api-key do usuário (a mesma das skills): PRMAKE_TOKEN ou ~/.claude/prmake-token.txt.</summary>
    public static string? UserToken()
    {
        if (Environment.GetEnvironmentVariable("PRMAKE_TOKEN") is { Length: > 0 } t) return t.Trim();
        return File.Exists(Paths.UserTokenFile) ? File.ReadAllText(Paths.UserTokenFile).Trim() : null;
    }

    public static string ResolveApiBase(AgentConfig config) =>
        (Environment.GetEnvironmentVariable("PRMAKE_API_BASE") is { Length: > 0 } b ? b : config.ApiBase).TrimEnd('/');

    /// <summary>
    /// Raiz dos repositórios: config, PRMAKE_WORKSPACE, pai do EDV_SOLVACE_DIR, pasta comum aos repositórios do mapa
    /// (0048), ~/repos/solvace ou a home.
    /// </summary>
    public static string ResolveWorkspace(AgentConfig config)
    {
        if (config.Workspace is { Length: > 0 } w && Directory.Exists(w)) return w;
        if (Environment.GetEnvironmentVariable("PRMAKE_WORKSPACE") is { Length: > 0 } env && Directory.Exists(env)) return env;
        if (Environment.GetEnvironmentVariable("EDV_SOLVACE_DIR") is { Length: > 0 } edv && Directory.GetParent(edv) is { Exists: true } parent)
            return parent.FullName;
        if (RepoMap.CommonAncestor(RepoMap.Folders(RepoMap.Load())) is { } common)
            return common;
        var solvace = Path.Combine(Paths.Home, "repos", "solvace");
        return Directory.Exists(solvace) ? solvace : Paths.Home;
    }
}

/// <summary>Erro HTTP do PRMake com o status (401 = credencial revogada).</summary>
public sealed class PrmakeException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Cliente da API do PRMake com a credencial do executor (ou a api-key do usuário, no register).</summary>
public sealed class PrmakeClient : IDisposable
{
    private readonly HttpClient _http;

    public PrmakeClient(string apiBase, string token)
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("x-api-key", token);
        _http.DefaultRequestHeaders.Add("X-Execution-Client", "agent");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"prmake-agent/{Agent.Version}");
    }

    public Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken ct) =>
        PostAsync("ExecutionWorker/register", request, AgentJson.Default.RegisterRequest, AgentJson.Default.RegistrationResponse, ct)!;

    public async Task<WorkerInfo> MeAsync(CancellationToken ct) => (await GetAsync("ExecutionWorker/me", AgentJson.Default.WorkerInfo, ct))!;

    public async Task<WorkerState> ReportAsync(ReportRequest request, CancellationToken ct) =>
        (await PostAsync("ExecutionWorker/report", request, AgentJson.Default.ReportRequest, AgentJson.Default.WorkerState, ct))!;

    public Task DoctorAsync(DoctorRequest request, CancellationToken ct) =>
        PostAsync("ExecutionWorker/doctor", request, AgentJson.Default.DoctorRequest, AgentJson.Default.WorkerInfo, ct);

    /// <summary>Long-poll: null = nada na fila.</summary>
    public async Task<ClaimResponse?> NextAsync(int waitSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(waitSeconds + 30));
        using var response = await _http.GetAsync($"ExecutionQueue/next?wait={waitSeconds}", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync(AgentJson.Default.ClaimResponse, ct);
    }

    public Task StartAsync(Guid id, StartRequest request, CancellationToken ct) =>
        PostAsync($"ExecutionQueue/{id}/start", request, AgentJson.Default.StartRequest, AgentJson.Default.DictionaryStringJsonElement, ct);

    public async Task<HeartbeatResponse> HeartbeatAsync(Guid id, HeartbeatRequest request, CancellationToken ct) =>
        (await PostAsync($"ExecutionQueue/{id}/heartbeat", request, AgentJson.Default.HeartbeatRequest, AgentJson.Default.HeartbeatResponse, ct))!;

    public Task FinishAsync(Guid id, FinishRequest request, CancellationToken ct) =>
        PostAsync($"ExecutionQueue/{id}/finish", request, AgentJson.Default.FinishRequest, AgentJson.Default.DictionaryStringJsonElement, ct);

    public async Task<PlanPending?> CurrentPlanAsync(string card, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"ExecutionPlan/card/{Uri.EscapeDataString(card)}/current", ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync(AgentJson.Default.PlanPending, ct);
    }

    public async Task<AgentDescriptor?> AgentDescriptorAsync(CancellationToken ct) =>
        await GetAsync("ExecutionWorker/agent", AgentJson.Default.AgentDescriptor, ct);

    public async Task DownloadAgentAsync(string rid, string target, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"ExecutionWorker/agent/{rid}", HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureAsync(response, ct);
        await using var file = File.Create(target);
        await response.Content.CopyToAsync(file, ct);
    }

    /// <summary>GET simples devolvendo o status (para o doctor).</summary>
    public async Task<HttpStatusCode> ProbeAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        return response.StatusCode;
    }

    private async Task<TOut?> GetAsync<TOut>(string path, JsonTypeInfo<TOut> output, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync(output, ct);
    }

    private async Task<TOut?> PostAsync<TIn, TOut>(string path, TIn body, JsonTypeInfo<TIn> input, JsonTypeInfo<TOut> output, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, input, ct);
        await EnsureAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync(output, ct);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e)) message = e.GetString() ?? body;
        }
        catch (JsonException)
        {
        }
        throw new PrmakeException(response.StatusCode, $"HTTP {(int)response.StatusCode}: {Trim(message, 300)}");
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose() => _http.Dispose();
}

/// <summary>Processos auxiliares (git, claude --version, notificações).</summary>
public static class Shell
{
    public static async Task<(int Code, string Output)> RunAsync(string file, IEnumerable<string> args, string? cwd = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
        var output = new StringBuilder();
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var er = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* já saiu */ }
            return (-2, "tempo esgotado");
        }
        output.Append(await o).Append(await er);
        return (p.ExitCode, output.ToString().Trim());
    }

    /// <summary>
    /// bash para rodar os scripts das skills: no Windows o do Git (CLAUDE_CODE_GIT_BASH_PATH ou a instalação padrão) — o
    /// bash.exe do System32 é o do WSL e não enxerga a home do Windows.
    /// </summary>
    public static string? Bash()
    {
        if (!Paths.IsWindows) return Which("bash");
        if (Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } b && File.Exists(b)) return b;
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
            if (root is { Length: > 0 } && Path.Combine(root, "Git", "bin", "bash.exe") is var candidate && File.Exists(candidate))
                return candidate;
        return Which("bash") is { } any && !any.Contains("System32", StringComparison.OrdinalIgnoreCase) ? any : null;
    }

    /// <summary>Executável no PATH (com as extensões do Windows).</summary>
    public static string? Which(string name)
    {
        var exts = Paths.IsWindows ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static void Notify(string title, string message)
    {
        try
        {
            if (Paths.IsMac)
                _ = RunAsync("osascript", ["-e", $"display notification \"{Escape(message)}\" with title \"{Escape(title)}\""]);
            else if (OperatingSystem.IsLinux() && Which("notify-send") is { } ns)
                _ = RunAsync(ns, [title, message]);
            else if (Paths.IsWindows)
                _ = RunAsync("powershell", ["-NoProfile", "-WindowStyle", "Hidden", "-Command",
                    "[void][System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms');" +
                    "$n=New-Object System.Windows.Forms.NotifyIcon;$n.Icon=[System.Drawing.SystemIcons]::Information;$n.Visible=$true;" +
                    $"$n.ShowBalloonTip(10000,'{title.Replace("'", "''")}','{message.Replace("'", "''")}','Info');Start-Sleep 11;$n.Dispose()"]);
        }
        catch
        {
            // notificação é cortesia
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
