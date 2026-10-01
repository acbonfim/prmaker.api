using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// Um pedido rodando: sobe <c>claude -p</c> (stream-json) na pasta certa, avisa o PRMake, manda heartbeat a cada 30 s
/// (cancelar, pausa longa, tempo máximo), lê o resultado (custo, turnos) e diz como terminou.
/// </summary>
public sealed class JobRunner(AgentConfig config, PrmakeClient client, ClaimResponse claim)
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);
    private const int StderrLines = 60;

    private readonly Queue<string> _stderr = new();
    private readonly CancellationTokenSource _kill = new();
    private string? _killReason;
    private bool _killedByServer;
    private bool _killedByPause;
    private bool _killedByTimeout;

    private JsonElement? _result;
    private string? _sessionFromStream;

    public Guid RequestId => claim.Request.Id;
    public string Card => claim.Request.CardNumber;
    public int? Pid { get; private set; }

    /// <summary>Encerra o processo (cancelamento local, desligando o executor).</summary>
    public void Kill(string reason)
    {
        _killReason ??= reason;
        _kill.Cancel();
    }

    public async Task RunAsync(CancellationToken stopping)
    {
        var r = claim.Request;
        var claudePath = ClaudeLocator.Find(config);
        if (claudePath is null)
        {
            await FinishAsync(new FinishRequest
            {
                Outcome = "failed", Retryable = false,
                Error = "Claude Code não encontrado nesta máquina (instale e faça login, ou configure claudePath no executor)"
            });
            return;
        }

        var workspace = ConfigStore.ResolveWorkspace(config);
        var resume = r.Kind == "resume" && !string.IsNullOrEmpty(r.SessionId) && ClaudeLocator.TranscriptExists(r.SessionId);
        var sessionId = resume ? r.SessionId! : Guid.NewGuid().ToString();
        var cwd = resume && r.SessionCwd is { Length: > 0 } sc && Directory.Exists(sc) ? sc : workspace;

        var args = new List<string> { "-p", resume ? claim.ResumePrompt : claim.FreshPrompt };
        args.AddRange(resume ? ["--resume", sessionId] : ["--session-id", sessionId]);
        args.AddRange(["--output-format", "stream-json", "--verbose", "--permission-mode", "dontAsk",
            "--settings", ClaudeSettings.Ensure(), "--add-dir", Paths.ClaudeHome]);
        if (!string.Equals(Path.GetFullPath(cwd), Path.GetFullPath(workspace), StringComparison.Ordinal))
            args.AddRange(["--add-dir", workspace]);
        if (claim.RemainingBudgetUsd is { } budget && budget > 0)
            args.AddRange(["--max-budget-usd", budget.ToString("0.00", CultureInfo.InvariantCulture)]);

        var psi = ClaudeLocator.StartInfo(claudePath, args);
        psi.WorkingDirectory = cwd;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.Environment["PRMAKE_EXECUTOR"] = "1";
        psi.Environment["PRMAKE_REQUEST_ID"] = r.Id.ToString();
        psi.Environment["PRMAKE_CARD"] = r.CardNumber;
        psi.Environment["PRMAKE_WORKSPACE"] = workspace;
        if (Environment.GetEnvironmentVariable("PRMAKE_API_BASE") is null)
            psi.Environment["PRMAKE_API_BASE"] = ConfigStore.ResolveApiBase(config);

        Log.Info($"card {r.CardNumber}: {(resume ? $"retomando a sessão {sessionId}" : $"sessão nova {sessionId}")} em {cwd} (pedido {r.Id}, tentativa {r.Attempts}/{r.MaxAttempts})");

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            await FinishAsync(new FinishRequest { Outcome = "failed", Retryable = false, Error = $"Não consegui iniciar o Claude Code ({claudePath}): {e.Message}" });
            return;
        }
        Pid = process.Id;
        process.StandardInput.Close();

        try
        {
            await client.StartAsync(r.Id, new StartRequest { Pid = process.Id, SessionId = sessionId }, stopping);
        }
        catch (Exception e)
        {
            Log.Warn($"card {r.CardNumber}: o PRMake recusou o início ({e.Message}) — encerrando o processo");
            KillTree(process);
            return;
        }

        var stdout = ReadStdoutAsync(process);
        var stderr = ReadStderrAsync(process);
        var started = DateTimeOffset.UtcNow;
        DateTimeOffset? pausedSince = null;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping, _kill.Token);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        while (!exit.IsCompleted)
        {
            var tick = Task.Delay(HeartbeatEvery, stop.Token);
            await Task.WhenAny(exit, tick);
            if (exit.IsCompleted) break;

            if (stop.IsCancellationRequested)
            {
                KillTree(process);
                break;
            }

            if (DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(Math.Max(5, config.TimeoutMinutes)))
            {
                _killedByTimeout = true;
                KillTree(process);
                break;
            }

            try
            {
                var hb = await client.HeartbeatAsync(r.Id, new HeartbeatRequest { Pid = process.Id, StderrTail = StderrTail() }, stopping);
                if (hb.Action == "cancel")
                {
                    Log.Info($"card {r.CardNumber}: cancelado pelo PRMake ({hb.Reason}) — encerrando o Claude");
                    _killedByServer = true;
                    KillTree(process);
                    break;
                }
                if (hb.PlanStatus == "paused")
                {
                    pausedSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - pausedSince > TimeSpan.FromMinutes(Math.Max(1, config.PauseGraceMinutes)))
                    {
                        Log.Info($"card {r.CardNumber}: plano pausado há mais de {config.PauseGraceMinutes} min — encerrando (a sessão continua no próximo 'Continuar')");
                        _killedByPause = true;
                        KillTree(process);
                        break;
                    }
                }
                else
                {
                    pausedSince = null;
                }
            }
            catch (PrmakeException e) when (e.Status == System.Net.HttpStatusCode.Unauthorized)
            {
                Log.Error($"card {r.CardNumber}: credencial do executor revogada — encerrando o Claude");
                _killedByServer = true;
                KillTree(process);
                break;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Rede caiu: o processo segue; se a trava vencer, o PRMake devolve o pedido e o próximo heartbeat cancela.
                Log.Warn($"card {r.CardNumber}: heartbeat falhou ({e.Message})");
            }
        }

        await exit;
        await Task.WhenAll(stdout, stderr);
        await FinishAsync(Outcome(process));
        Log.Info($"card {r.CardNumber}: terminou (código {SafeExitCode(process)})");
        await NotifyIfNeededAsync();
    }

    private FinishRequest Outcome(Process process)
    {
        var code = SafeExitCode(process);
        var finish = new FinishRequest { ExitCode = code, StderrTail = StderrTail() };
        if (_result is { } res)
        {
            finish.CostUsd = res.TryGetProperty("total_cost_usd", out var cost) && cost.TryGetDecimal(out var c) ? c : null;
            finish.Turns = res.TryGetProperty("num_turns", out var turns) && turns.TryGetInt32(out var t) ? t : null;
            if (res.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                long Sum(params string[] names) => names.Sum(n => usage.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0);
                finish.InputTokens = Sum("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens");
                finish.OutputTokens = Sum("output_tokens");
            }
        }

        if (_killedByServer)
            return With(finish, "done", "Encerrado: cancelado pelo PRMake", null, false);
        if (_killedByPause)
            return With(finish, "done", "Plano pausado pela tela — a sessão continua no próximo 'Continuar'", null, false);
        if (_killedByTimeout)
            return With(finish, "failed", null, $"Passou do tempo máximo de {config.TimeoutMinutes} min", false);
        if (_killReason is not null)
            return With(finish, "failed", null, $"Executor encerrado ({_killReason})", true);

        var subtype = _result?.TryGetProperty("subtype", out var st) == true ? st.GetString() : null;
        var isError = _result?.TryGetProperty("is_error", out var ie) == true && ie.ValueKind == JsonValueKind.True;
        if (_result is not null && !isError && subtype == "success")
            return With(finish, "done", $"A sessão encerrou a vez ({finish.Turns ?? 0} turnos{(finish.CostUsd is { } usd ? $", US$ {usd.ToString("0.00", CultureInfo.InvariantCulture)}" : "")})", null, false);
        if (subtype?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true)
            return With(finish, "failed", null, "Orçamento do dia atingido durante a sessão", false);
        if (subtype == "error_max_turns")
            return With(finish, "done", "A sessão atingiu o máximo de turnos — continua no próximo pedido", null, false);

        var detail = _result?.TryGetProperty("result", out var rt) == true ? rt.GetString() : null;
        var error = !string.IsNullOrWhiteSpace(detail) ? detail : LastStderrLine() ?? $"O Claude Code saiu com código {code}";
        // Login expirado/sem login não se resolve sozinho: não adianta tentar de novo.
        var retryable = !(error.Contains("login", StringComparison.OrdinalIgnoreCase) || error.Contains("auth", StringComparison.OrdinalIgnoreCase)
                          || error.Contains("credit", StringComparison.OrdinalIgnoreCase));
        return With(finish, "failed", null, error, retryable);
    }

    private static FinishRequest With(FinishRequest f, string outcome, string? reason, string? error, bool retryable)
    {
        f.Outcome = outcome;
        f.Reason = reason;
        f.Error = error;
        f.Retryable = retryable;
        return f;
    }

    private async Task FinishAsync(FinishRequest finish)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await client.FinishAsync(RequestId, finish, CancellationToken.None);
                return;
            }
            catch (PrmakeException e) when ((int)e.Status is >= 400 and < 500)
            {
                Log.Warn($"card {Card}: o PRMake recusou o fim ({e.Message})");
                return;
            }
            catch (Exception e)
            {
                Log.Warn($"card {Card}: falha ao avisar o fim (tentativa {attempt}/5): {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
            }
        }
    }

    private async Task NotifyIfNeededAsync()
    {
        if (!config.Notifications || _killedByServer) return;
        try
        {
            var plan = await client.CurrentPlanAsync(Card, CancellationToken.None);
            if (plan is not null && plan.UserActions.Count > 0)
                Shell.Notify("PRMake", $"Card {Card}: {plan.UserActions.Count} {(plan.UserActions.Count == 1 ? "ação aguardando" : "ações aguardando")} você");
        }
        catch
        {
            // cortesia
        }
    }

    private async Task ReadStdoutAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "result")
                        _result = root.Clone();
                    else if (type == "system" && root.TryGetProperty("session_id", out var sid))
                        _sessionFromStream = sid.GetString();
                }
                catch (JsonException)
                {
                    // linha que não é JSON: ignora
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"card {Card}: leitura da saída do Claude falhou: {e.Message}");
        }
    }

    private async Task ReadStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                lock (_stderr)
                {
                    _stderr.Enqueue(Redact(line));
                    while (_stderr.Count > StderrLines) _stderr.Dequeue();
                }
            }
        }
        catch
        {
            // processo morreu
        }
    }

    private string? StderrTail()
    {
        lock (_stderr)
            return _stderr.Count == 0 ? null : string.Join('\n', _stderr);
    }

    private string? LastStderrLine()
    {
        lock (_stderr)
            return _stderr.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
    }

    /// <summary>Nada de token/segredo no stderr que vai para o PRMake.</summary>
    private static string Redact(string line) =>
        System.Text.RegularExpressions.Regex.Replace(line,
            @"(eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]+|sk-ant-[A-Za-z0-9_\-]+|ghp_[A-Za-z0-9]+|(password|senha|token|secret)\s*[=:]\s*\S+)",
            "[oculto]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static int? SafeExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode : null; } catch { return null; }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // já saiu
        }
    }
}

/// <summary>Onde está o Claude Code e as sessões dele nesta máquina.</summary>
public static class ClaudeLocator
{
    public static string? Find(AgentConfig config)
    {
        if (config.ClaudePath is { Length: > 0 } configured && File.Exists(configured)) return configured;
        if (Shell.Which("claude") is { } inPath) return inPath;
        var home = Paths.Home;
        string[] candidates = Paths.IsWindows
            ? [Path.Combine(home, ".local", "bin", "claude.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd")]
            : [Path.Combine(home, ".local", "bin", "claude"), Path.Combine(home, ".claude", "local", "claude"), "/opt/homebrew/bin/claude", "/usr/local/bin/claude"];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Claude instalado pelo npm no Windows é um .cmd: roda pelo cmd.</summary>
    public static ProcessStartInfo StartInfo(string claudePath, IEnumerable<string> args)
    {
        if (Paths.IsWindows && claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("cmd.exe");
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(claudePath);
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }
        var direct = new ProcessStartInfo(claudePath);
        foreach (var a in args) direct.ArgumentList.Add(a);
        return direct;
    }

    /// <summary>O transcript da sessão está nesta máquina (dá para <c>--resume</c>)?</summary>
    public static bool TranscriptExists(string sessionId)
    {
        var projects = Path.Combine(Paths.ClaudeHome, "projects");
        if (!Directory.Exists(projects)) return false;
        try
        {
            return Directory.EnumerateDirectories(projects).Any(d => File.Exists(Path.Combine(d, sessionId + ".jsonl")));
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> VersionAsync(AgentConfig config)
    {
        if (Find(config) is not { } path) return null;
        var psi = StartInfo(path, ["--version"]);
        var (code, output) = await Shell.RunAsync(psi.FileName, psi.ArgumentList, timeout: TimeSpan.FromSeconds(20));
        return code == 0 ? output.Split('\n')[0].Trim() : null;
    }
}
