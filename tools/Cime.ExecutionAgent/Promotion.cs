using System.Text.RegularExpressions;

namespace Cime.ExecutionAgent;

// ── Contrato com o PRMake (0074) — o mesmo de PromotionExecutorController ────────────────────────

public sealed class PromotionClaim
{
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Card { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string? SourceBase { get; set; }
    public string Target { get; set; } = string.Empty;
    /// <summary>cherry-pick | direct.</summary>
    public string Mode { get; set; } = "cherry-pick";
    public string NewBranch { get; set; } = string.Empty;
}

public sealed class PromotionReport
{
    /// <summary>pushed | conflict | missing_target | no_changes | error.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? NewBranch { get; set; }
    public int CommitCount { get; set; }
    public List<string>? ConflictFiles { get; set; }
}

public sealed class PromotionHeartbeat
{
    public bool Stop { get; set; }
}

/// <summary>
/// Cherry-pick de uma promoção (0074), só com git local: cria a branch do cherry-pick a partir de <c>origin/&lt;destino&gt;</c>
/// num <b>worktree próprio</b> (<c>&lt;pasta do repositório&gt;/../.prmake-wt/promo-&lt;card&gt;</c>) — o clone de trabalho e a
/// branch em que ele estiver não são tocados —, aplica os commits da branch principal com <c>cherry-pick -x</c> e dá
/// <c>push</c> da branch NOVA. Nunca faz <c>--force</c>, nunca mescla, nunca mexe em branch que já existe.
/// </summary>
public static class GitPromotion
{
    private static readonly Regex SafeRef = new(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,198}$", RegexOptions.Compiled);
    private static readonly Regex Sha = new(@"^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly TimeSpan Net = TimeSpan.FromMinutes(5);

    public static bool IsSafeRef(string? name) =>
        name is { Length: > 0 } && SafeRef.IsMatch(name) && !name.Contains("..") && !name.EndsWith('/') && !name.EndsWith(".lock") && !name.Contains("//");

    public static async Task<PromotionReport> RunAsync(string repoPath, PromotionClaim claim, CancellationToken ct, Action<string>? log = null)
    {
        log ??= _ => { };
        foreach (var (what, value) in new[] { ("branch principal", claim.SourceBranch), ("destino", claim.Target), ("nova branch", claim.NewBranch) })
            if (!IsSafeRef(value))
                return Fail($"Nome de {what} inválido: '{value}'.");
        if (claim.SourceBase is { Length: > 0 } && !IsSafeRef(claim.SourceBase))
            return Fail($"Nome da base inválido: '{claim.SourceBase}'.");
        if (!Directory.Exists(repoPath))
            return Fail($"A pasta do repositório não existe: {repoPath}");

        // Modo direct: o PR sai da própria branch principal — nada de git local.
        if (claim.Mode == "direct")
            return new PromotionReport { Outcome = "pushed", NewBranch = claim.SourceBranch, CommitCount = 0 };

        var fetch = await Git(repoPath, ["fetch", "origin", "--prune"], ct, Net);
        if (fetch.Code != 0)
            return Fail($"git fetch falhou: {Tail(fetch.Output)}");

        if (!await RefExists(repoPath, $"refs/remotes/origin/{claim.Target}", ct))
            return new PromotionReport { Outcome = "missing_target", Message = $"A branch {claim.Target} não existe no remoto." };
        if (!await RefExists(repoPath, $"refs/remotes/origin/{claim.SourceBranch}", ct))
            return Fail($"A branch principal {claim.SourceBranch} não está no remoto.");

        var baseRef = claim.SourceBase is { Length: > 0 } b ? b : "master";
        if (!await RefExists(repoPath, $"refs/remotes/origin/{baseRef}", ct))
            return Fail($"A base {baseRef} (ponto de corte da branch principal) não existe no remoto.");

        var mergeBase = await Git(repoPath, ["merge-base", $"origin/{baseRef}", $"origin/{claim.SourceBranch}"], ct);
        if (mergeBase.Code != 0 || !Sha.IsMatch(mergeBase.Output.Trim()))
            return Fail($"Não achei o ponto de corte entre {baseRef} e {claim.SourceBranch}.");

        var list = await Git(repoPath, ["rev-list", "--reverse", "--topo-order", "--no-merges", $"origin/{claim.SourceBranch}", $"^{mergeBase.Output.Trim()}"], ct);
        if (list.Code != 0)
            return Fail($"git rev-list falhou: {Tail(list.Output)}");
        var commits = list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (commits.Count == 0)
            return new PromotionReport { Outcome = "no_changes", Message = $"A branch {claim.SourceBranch} não tem commits além de {baseRef}." };
        if (commits.Any(c => !Sha.IsMatch(c)))
            return Fail("Lista de commits inesperada.");

        var newBranch = await FreeBranchNameAsync(repoPath, claim.NewBranch, ct);
        var repoName = Path.GetFileName(repoPath.TrimEnd('/', '\\'));
        var parent = Path.GetDirectoryName(Path.GetFullPath(repoPath.TrimEnd('/', '\\'))) ?? repoPath;
        var worktree = Path.Combine(parent, ".prmake-wt", $"promo-{Slug(claim.Card)}", $"{repoName}-{Slug(claim.Target)}");
        if (Directory.Exists(worktree))
            await RemoveWorktreeAsync(repoPath, worktree, null, ct); // sobra de uma tentativa anterior

        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        var add = await Git(repoPath, ["worktree", "add", "--no-track", "-b", newBranch, worktree, $"origin/{claim.Target}"], ct);
        if (add.Code != 0)
            return Fail($"Não consegui criar o worktree: {Tail(add.Output)}");

        try
        {
            var applied = 0;
            foreach (var sha in commits)
            {
                var pick = await Git(worktree, ["cherry-pick", "-x", sha], ct);
                if (pick.Code == 0)
                {
                    applied++;
                    continue;
                }

                var unmerged = await Git(worktree, ["diff", "--name-only", "--diff-filter=U"], ct);
                var files = unmerged.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (files.Count > 0)
                {
                    await Git(worktree, ["cherry-pick", "--abort"], ct);
                    return new PromotionReport
                    {
                        Outcome = "conflict",
                        Message = $"Conflito ao aplicar {sha[..8]} em {claim.Target}.",
                        ConflictFiles = files,
                        CommitCount = commits.Count
                    };
                }

                // Sem arquivo em conflito: o commit já está no destino (ficou vazio) — pula.
                var skip = await Git(worktree, ["cherry-pick", "--skip"], ct);
                if (skip.Code != 0)
                {
                    await Git(worktree, ["cherry-pick", "--abort"], ct);
                    return Fail($"cherry-pick de {sha[..8]} falhou: {Tail(pick.Output)}");
                }
                log($"commit {sha[..8]} já está em {claim.Target} — pulado");
            }

            if (applied == 0)
                return new PromotionReport { Outcome = "no_changes", Message = $"Os commits de {claim.SourceBranch} já estão em {claim.Target}.", CommitCount = commits.Count };

            // Branch nova, sem --force: se alguém criou o mesmo nome no meio do caminho, o push recusa.
            var push = await Git(worktree, ["push", "origin", $"HEAD:refs/heads/{newBranch}"], ct, Net);
            if (push.Code != 0)
                return Fail($"git push falhou: {Tail(push.Output)}");

            return new PromotionReport { Outcome = "pushed", NewBranch = newBranch, CommitCount = applied };
        }
        finally
        {
            await RemoveWorktreeAsync(repoPath, worktree, newBranch, ct);
        }
    }

    private static async Task RemoveWorktreeAsync(string repoPath, string worktree, string? branch, CancellationToken ct)
    {
        await Git(repoPath, ["worktree", "remove", "--force", worktree], ct);
        if (Directory.Exists(worktree))
        {
            try { Directory.Delete(worktree, recursive: true); } catch { /* o faxineiro apaga depois */ }
            await Git(repoPath, ["worktree", "prune"], ct);
        }
        // A branch local só existia para o push (a remota fica); a pasta do card some se esvaziou.
        if (branch is not null)
            await Git(repoPath, ["branch", "-D", branch], ct);
        try
        {
            var dir = Path.GetDirectoryName(worktree);
            if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Se já existe (local ou no remoto), tenta -2, -3… — nunca reaproveita uma branch existente.</summary>
    private static async Task<string> FreeBranchNameAsync(string repo, string wanted, CancellationToken ct)
    {
        for (var n = 1; n <= 9; n++)
        {
            var name = n == 1 ? wanted : $"{wanted}-{n}";
            if (!await RefExists(repo, $"refs/remotes/origin/{name}", ct) && !await RefExists(repo, $"refs/heads/{name}", ct))
                return name;
        }
        return $"{wanted}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static async Task<bool> RefExists(string repo, string refName, CancellationToken ct) =>
        (await Git(repo, ["rev-parse", "--verify", "--quiet", refName], ct)).Code == 0;

    private static Task<(int Code, string Output)> Git(string cwd, IEnumerable<string> args, CancellationToken ct, TimeSpan? timeout = null) =>
        Shell.RunAsync("git", ["-C", cwd, .. args], timeout: timeout ?? TimeSpan.FromMinutes(2), ct: ct);

    private static string Slug(string value) => Regex.Replace(value.Trim(), @"[^A-Za-z0-9._-]+", "-").Trim('-') is { Length: > 0 } s ? s : "x";

    private static string Tail(string output)
    {
        var trimmed = output.Trim();
        return trimmed.Length <= 400 ? trimmed : "…" + trimmed[^400..];
    }

    private static PromotionReport Fail(string message) => new() { Outcome = "error", Message = message };
}

/// <summary>
/// Laço da promoção (0074): pede o próximo item ao PRMake, acha o clone no mapa de repositórios da máquina, roda o
/// <see cref="GitPromotion"/> e devolve o resultado (o PRMake abre o PR). Um item por vez nesta máquina.
/// </summary>
public sealed class PromotionRunner(AgentConfig config, PrmakeClient client)
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(60);

    /// <summary>Roda o próximo item, se houver. Devolve true quando processou um (vale tentar o seguinte).</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        var claim = await client.NextPromotionAsync(ct);
        if (claim is null)
            return false;

        Log.Info($"promoção {claim.Version}: card {claim.Card} → {claim.Target} ({claim.Repository}, {claim.Mode})");
        using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(() => HeartbeatLoopAsync(claim.ItemId, beat.Token), CancellationToken.None);

        PromotionReport report;
        try
        {
            var repo = LocateRepository(claim.Repository);
            report = repo is null
                ? new PromotionReport { Outcome = "error", Message = $"Não encontrei o repositório {claim.Repository} nesta máquina (mapa de repositórios: prmake-skills.sh repos scan)." }
                : await GitPromotion.RunAsync(repo, claim, ct, m => Log.Info($"promoção {claim.Card}: {m}"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // desligando: a trava vence e o item volta para a fila
            beat.Cancel();
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"promoção {claim.Card}: erro inesperado: {e}");
            report = new PromotionReport { Outcome = "error", Message = $"Erro no executor: {e.Message}" };
        }
        finally
        {
            beat.Cancel();
            try { await heartbeat; } catch { /* encerrando */ }
        }

        try
        {
            await client.ReportPromotionAsync(claim.ItemId, report, ct);
            Log.Info($"promoção {claim.Card} → {claim.Target}: {report.Outcome}");
        }
        catch (PrmakeException e)
        {
            Log.Warn($"promoção {claim.Card}: não consegui informar o resultado ({e.Message}) — o item volta para a fila quando a trava vencer");
        }
        return true;
    }

    private async Task HeartbeatLoopAsync(Guid itemId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatEvery, ct); } catch (OperationCanceledException) { return; }
            try { await client.PromotionHeartbeatAsync(itemId, ct); }
            catch (Exception e) when (!ct.IsCancellationRequested) { Log.Warn($"heartbeat da promoção falhou: {e.Message}"); }
        }
    }

    /// <summary>Pasta do clone: pelo mapa de repositórios ("owner/repo" ou "repo"); sem mapa, uma pasta com o nome no workspace.</summary>
    private string? LocateRepository(string repository)
    {
        var name = repository.Trim();
        var slash = name.LastIndexOf('/');
        var shortName = slash >= 0 ? name[(slash + 1)..] : name;
        var mapped = RepoMap.Load()?.Repos.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Name, shortName, StringComparison.OrdinalIgnoreCase));
        if (mapped is not null && Directory.Exists(mapped.Path))
            return mapped.Path;

        var guess = Path.Combine(ConfigStore.ResolveWorkspace(config), shortName);
        return Directory.Exists(Path.Combine(guess, ".git")) ? guess : null;
    }
}
