using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// O laço do executor: pergunta ao PRMake (long-poll) se há pedido para esta máquina, roda até a concorrência
/// configurada, manda o sinal de vida a cada minuto (com versões e o que a máquina alcança), reconcilia pedidos que o
/// PRMake acha que estão aqui mas não estão, roda o doctor a cada 6 h, limpa worktrees e se atualiza sozinho.
/// </summary>
public sealed class Runner(AgentConfig config)
{
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(60);
    /// <summary>Doctor de hora em hora (e na hora quando a tela pede): um resultado velho confundia a tela.</summary>
    private static readonly TimeSpan DoctorEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan JanitorEvery = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<Guid, (JobRunner Job, Task Task)> _jobs = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _recent = new();
    private WorkerState _state = new();
    private volatile bool _revoked;
    /// <summary>0041: limite da conta do Claude — sem pegar pedidos até aqui (o PRMake também segura).</summary>
    private DateTimeOffset _throttledUntil = DateTimeOffset.MinValue;
    private int _doctorRunning;

    public async Task<int> RunAsync(CancellationToken stopping)
    {
        if (config.Token is null || config.WorkerId is null)
        {
            Log.Error("executor não registrado — rode: prmake-agent register");
            return 2;
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            Log.Error($"já existe um executor rodando nesta máquina (lock {Paths.Lock})");
            return 3;
        }

        using var client = new PrmakeClient(ConfigStore.ResolveApiBase(config), config.Token);
        Log.Info($"executor {Agent.Version} iniciado em {Paths.HostName} (workspace {ConfigStore.ResolveWorkspace(config)})");
        ClaudeSettings.Ensure();
        // 0048: sem mapa de repositórios (instalação antiga, executor atualizado antes das skills) → monta agora.
        _ = RepoMap.EnsureAsync(stopping);

        var background = Task.WhenAll(
            Every(ReportEvery, () => ReportAsync(client, stopping), stopping),
            Every(DoctorEvery, () => DoctorAsync(client, stopping), stopping, initialDelay: TimeSpan.FromSeconds(5)),
            Every(JanitorEvery, () => Worktrees.CleanAsync(config, client, stopping), stopping, initialDelay: TimeSpan.FromMinutes(2)));

        var failures = 0;
        while (!stopping.IsCancellationRequested)
        {
            if (_revoked)
            {
                await Delay(TimeSpan.FromMinutes(5), stopping);
                continue;
            }
            if (DateTimeOffset.UtcNow < _throttledUntil)
            {
                await Delay(TimeSpan.FromSeconds(30), stopping);
                continue;
            }
            if (_state.Status != "active" || _jobs.Count >= Math.Max(1, _state.MaxConcurrency))
            {
                await Delay(TimeSpan.FromSeconds(5), stopping);
                continue;
            }

            try
            {
                // polling curto (0057): a requisição não fica aberta no Cloud Run — long-poll mantinha a instância cobrada 24 h
                var claim = await client.NextAsync(0, stopping);
                failures = 0;
                if (claim is null)
                {
                    await Delay(NextPollEvery, stopping);
                    continue;
                }
                StartJob(client, claim, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (PrmakeException e) when (e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _revoked = true;
                Log.Error($"credencial do executor recusada ({e.Message}) — registre a máquina de novo: prmake-agent register");
                Shell.Notify("PRMake", "O executor desta máquina foi revogado — rode: prmake-agent register");
            }
            catch (Exception e)
            {
                failures++;
                var wait = TimeSpan.FromSeconds(Math.Min(60, 2 << Math.Min(failures, 5)));
                Log.Warn($"PRMake indisponível ({e.Message}) — nova tentativa em {wait.TotalSeconds:0} s");
                await Delay(wait, stopping);
            }
        }

        Log.Info("encerrando: devolvendo os pedidos em andamento para a fila");
        foreach (var (job, _) in _jobs.Values) job.Kill("executor desligado");
        await Task.WhenAll(_jobs.Values.Select(j => j.Task));
        try { await background; } catch { /* encerrando */ }
        return 0;
    }

    private void StartJob(PrmakeClient client, ClaimResponse claim, CancellationToken stopping)
    {
        var job = new JobRunner(config, client, claim);
        _recent[claim.Request.Id] = DateTimeOffset.UtcNow;
        var task = Task.Run(async () =>
        {
            try
            {
                await job.RunAsync(stopping);
                if (job.ThrottledUntil is { } until && until > _throttledUntil)
                {
                    _throttledUntil = until;
                    var local = until.ToLocalTime();
                    Log.Warn($"limite de uso da conta do Claude — sem pegar pedidos até {local:dd/MM HH:mm}");
                    Shell.Notify("PRMake", $"Limite da conta do Claude atingido — o executor volta às {local:HH:mm}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"card {claim.Request.CardNumber}: erro inesperado no executor: {e}");
                try
                {
                    await client.FinishAsync(claim.Request.Id, new FinishRequest
                    {
                        Outcome = "failed", Retryable = true, Error = $"Erro no executor: {e.Message}"
                    }, CancellationToken.None);
                }
                catch
                {
                    // a trava vence e o PRMake devolve o pedido
                }
            }
            finally
            {
                _jobs.TryRemove(claim.Request.Id, out _);
            }
        }, CancellationToken.None);
        _jobs[claim.Request.Id] = (job, task);
    }

    private async Task ReportAsync(PrmakeClient client, CancellationToken ct)
    {
        try
        {
            var state = await client.ReportAsync(new ReportRequest
            {
                AgentVersion = Agent.Version,
                ClaudeVersion = await ClaudeLocator.VersionAsync(config),
                SkillsVersion = SkillsVersion(),
                Workspace = ConfigStore.ResolveWorkspace(config),
                Capabilities = Capabilities()
            }, ct);
            _state = state;
            _revoked = false;
            if (state.DoctorRequested)
            {
                Log.Info("diagnóstico pedido pela tela — rodando agora");
                _ = DoctorAsync(client, ct);
            }

            // O PRMake acha que um pedido está aqui, mas não há processo (o executor reiniciou): devolve para a fila.
            foreach (var id in state.ActiveRequestIds.Where(id => !_jobs.ContainsKey(id)))
            {
                if (_recent.TryGetValue(id, out var at) && DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(1)) continue;
                Log.Warn($"pedido {id} constava neste executor sem processo — devolvendo para a fila");
                await client.FinishAsync(id, new FinishRequest { Outcome = "failed", Retryable = true, Error = "O executor reiniciou durante a execução" }, ct);
            }
            foreach (var old in _recent.Where(kv => DateTimeOffset.UtcNow - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList())
                _recent.TryRemove(old, out _);

            if (config.AutoUpdate && _jobs.IsEmpty && state.LatestAgentVersion is { Length: > 0 } latest && Updater.IsNewer(latest, Agent.Version))
            {
                Log.Info($"versão nova do executor publicada ({latest}) — atualizando");
                if (await Updater.UpdateAsync(client, ct))
                    Updater.Restart();
            }
        }
        catch (PrmakeException e) when (e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _revoked = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn($"sinal de vida falhou: {e.Message}");
        }
    }

    private async Task DoctorAsync(PrmakeClient client, CancellationToken ct)
    {
        // Um de cada vez (o pedido da tela pode chegar junto com o de hora em hora).
        if (Interlocked.Exchange(ref _doctorRunning, 1) == 1) return;
        try
        {
            var checks = await Doctor.RunChecksAsync(config, client, ct);
            await client.DoctorAsync(new DoctorRequest { Checks = checks }, ct);
            var problems = checks.Where(c => !c.Ok && c.Severity != "warning").Select(c => c.Name).ToList();
            if (problems.Count > 0)
                Log.Warn($"doctor: problemas em {string.Join(", ", problems)}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn($"doctor falhou: {e.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _doctorRunning, 0);
        }
    }

    /// <summary>
    /// Repositórios git na raiz do workspace (e um nível abaixo) e, desde a 0048, o mapa de repositórios da máquina
    /// (<c>repoMap</c>) — a tela mostra o que a máquina alcança.
    /// </summary>
    private JsonElement Capabilities()
    {
        var workspace = ConfigStore.ResolveWorkspace(config);
        var repos = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(workspace))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))) repos.Add(Path.GetFileName(dir));
                else if (repos.Count < 200)
                    repos.AddRange(Directory.EnumerateDirectories(dir).Where(d => Directory.Exists(Path.Combine(d, ".git")))
                        .Select(d => Path.GetFileName(dir) + "/" + Path.GetFileName(d)).Take(100));
            }
        }
        catch
        {
            // workspace sumiu
        }
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("repos");
            foreach (var r in repos.Take(200)) w.WriteStringValue(r);
            w.WriteEndArray();
            w.WriteString("os", Paths.OsDescription);
            RepoMap.WriteCapabilities(w, RepoMap.Load());
            w.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(buffer.ToArray());
        return doc.RootElement.Clone();
    }

    public static string? SkillsVersion()
    {
        var manifest = Path.Combine(Paths.ClaudeHome, "skills", "analisar-bug", ".prmake-skill.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task Every(TimeSpan interval, Func<Task> action, CancellationToken ct, TimeSpan? initialDelay = null)
    {
        await Delay(initialDelay ?? TimeSpan.Zero, ct);
        while (!ct.IsCancellationRequested)
        {
            await action();
            await Delay(interval, ct);
        }
    }

    /// <summary>Intervalo entre consultas da fila quando está vazia.</summary>
    private static readonly TimeSpan NextPollEvery = TimeSpan.FromSeconds(10);

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>Um executor por máquina (lock exclusivo num arquivo).</summary>
public static class SingleInstance
{
    public static IDisposable? TryAcquire()
    {
        Directory.CreateDirectory(Paths.Root);
        try
        {
            return new FileStream(Paths.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
