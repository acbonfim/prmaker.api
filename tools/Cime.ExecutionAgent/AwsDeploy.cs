namespace Cime.ExecutionAgent;

// ── Contrato com o PRMake (0100) — o mesmo de AwsDeployExecutorController ────────────────────────

public sealed class AwsRefreshClaim
{
    public Guid Id { get; set; }
    public string Card { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public int Attempt { get; set; }
    /// <summary>button | follow | task.</summary>
    public string Source { get; set; } = "button";
}

public sealed class AwsRefreshReport
{
    /// <summary>done | failed.</summary>
    public string Status { get; set; } = "done";
    public string? Error { get; set; }
    public long? DurationMs { get; set; }
}

public sealed class AwsRefreshHeartbeat
{
    public bool Stop { get; set; }
}

/// <summary>
/// 0100: coleta das esteiras do card na AWS, sem o Claude — roda o script da skill <c>aws-deploy</c> (só leitura: CLI da
/// AWS + git local), que grava o retrato no PRMake com a api-key da pessoa. O pedido só chega ao executor de quem clicou
/// em Atualizar (o servidor escolhe). Um pedido por vez nesta máquina.
/// </summary>
public sealed class AwsDeployRunner(AgentConfig config, PrmakeClient client)
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(60);
    /// <summary>Primeira carga do índice (~1.100 pipelines) leva ~6 min; com folga.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    public static string Script => Path.Combine(Paths.ClaudeHome, "skills", "aws-deploy", "scripts", "aws.sh");

    /// <summary>Roda o próximo pedido, se houver. Devolve true quando processou um (vale tentar o seguinte).</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        var claim = await client.NextAwsRefreshAsync(ct);
        if (claim is null)
            return false;

        Log.Info($"AWS: card {claim.Card} ({claim.Source}, pedido de {claim.RequestedBy})");
        using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var job = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(() => HeartbeatLoopAsync(claim.Id, beat.Token, job), CancellationToken.None);
        var started = DateTimeOffset.UtcNow;
        AwsRefreshReport report;
        try
        {
            report = await CollectAsync(claim, job.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            beat.Cancel();
            return false; // desligando: a trava vence e o pedido volta para a fila
        }
        catch (Exception e)
        {
            Log.Error($"AWS {claim.Card}: erro inesperado: {e}");
            report = new AwsRefreshReport { Status = "failed", Error = $"Erro no executor: {e.Message}" };
        }
        finally
        {
            beat.Cancel();
            try { await heartbeat; } catch { /* encerrando */ }
        }

        report.DurationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        try
        {
            await client.ReportAwsRefreshAsync(claim.Id, report, ct);
            Log.Info($"AWS {claim.Card}: {report.Status}{(report.Error is { } e ? " — " + e : "")}");
        }
        catch (PrmakeException e)
        {
            Log.Warn($"AWS {claim.Card}: não consegui informar o resultado ({e.Message})");
        }
        return true;
    }

    private async Task<AwsRefreshReport> CollectAsync(AwsRefreshClaim claim, CancellationToken ct)
    {
        if (!int.TryParse(claim.Card, out var card) || card <= 0)
            return Failed($"Card inválido: '{claim.Card}'.");
        if (!File.Exists(Script))
            return Failed("A skill aws-deploy não está nesta máquina — rode: bash ~/.claude/skills/.prmake/prmake-skills.sh update aws-deploy");
        if (Shell.Bash() is not { } bash)
            return Failed("bash não encontrado (no Windows: Git Bash).");
        // A coleta grava como a pessoa (api-key das skills); sem ela, com a credencial do executor (AwsDeploy liberado).
        var token = ConfigStore.UserToken() ?? config.Token;
        if (string.IsNullOrWhiteSpace(token))
            return Failed("Sem token do PRMake nesta máquina (~/.claude/prmake-token.txt).");

        var env = new Dictionary<string, string>
        {
            ["PRMAKE_TOKEN"] = token,
            ["PRMAKE_API_BASE"] = ConfigStore.ResolveApiBase(config),
            ["GIT_TERMINAL_PROMPT"] = "0"
        };
        var (code, output) = await Shell.RunAsync(bash, [Script, "collect", card.ToString(), "--request", claim.Id.ToString()],
            ConfigStore.ResolveWorkspace(config), Timeout, ct, env);
        if (code == 0)
            return new AwsRefreshReport { Status = "done" };
        var tail = output.Trim();
        var error = tail.Split('\n').LastOrDefault(l => l.Contains("ERRO", StringComparison.Ordinal)) ?? tail;
        return Failed(code == -2 ? $"A coleta passou de {Timeout.TotalMinutes:0} min." : error.Length <= 400 ? error : "…" + error[^400..]);
    }

    private static AwsRefreshReport Failed(string message) => new() { Status = "failed", Error = message };

    private async Task HeartbeatLoopAsync(Guid id, CancellationToken ct, CancellationTokenSource job)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatEvery, ct); } catch (OperationCanceledException) { return; }
            try
            {
                if ((await client.AwsRefreshHeartbeatAsync(id, ct)).Stop)
                {
                    Log.Warn("AWS: o pedido não é mais deste executor — coleta interrompida");
                    job.Cancel();
                    return;
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { Log.Warn($"heartbeat da coleta AWS falhou: {e.Message}"); }
        }
    }
}
