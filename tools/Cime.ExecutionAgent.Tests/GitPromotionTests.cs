using System.Diagnostics;
using Cime.ExecutionAgent;
using Xunit;

namespace Cime.ExecutionAgent.Tests;

/// <summary>0074: o cherry-pick da promoção contra repositórios git reais (origin bare + clone), sem rede.</summary>
public sealed class GitPromotionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prmake-promo-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _origin;
    private readonly string _clone;

    public GitPromotionTests()
    {
        Directory.CreateDirectory(_root);
        _origin = Path.Combine(_root, "origin.git");
        _clone = Path.Combine(_root, "edv-solvace-apps");

        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Teste");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "teste@solvace.com");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Teste");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "teste@solvace.com");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(_root, "gitconfig"));
        Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");

        G(_root, "init", "--bare", "-b", "master", _origin);
        G(_root, "clone", _origin, _clone);
        Write("a.txt", "linha1\nlinha2\nlinha3\n");
        Commit("inicial");
        G(_clone, "push", "origin", "master");

        // release-3.274.10 e edge nascem da master; a edge muda a linha2 de forma diferente (vai conflitar).
        Branch("release-3.274.10", "master", () => { Write("release.txt", "r\n"); Commit("só da release"); });
        Branch("edge", "master", () => { Write("a.txt", "linha1\nEDGE\nlinha3\n"); Commit("edge muda a2"); });

        // A branch principal do card, cortada da master: dois commits.
        Branch("hotfix/77317", "master", () =>
        {
            Write("b.txt", "novo\n"); Commit("AB#77317 cria b");
            Write("a.txt", "linha1\nCORRIGIDA\nlinha3\n"); Commit("AB#77317 corrige a2");
        });
        G(_clone, "checkout", "master");
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch { /* temporário */ }
    }

    private PromotionClaim Claim(string target, string mode = "cherry-pick", string? baseBranch = "master", string? newBranch = null) => new()
    {
        ItemId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Version = "3.274.10", Card = "77317", Repository = "Solvace-Org/edv-solvace-apps",
        SourceBranch = "hotfix/77317", SourceBase = baseBranch, Target = target, Mode = mode,
        NewBranch = newBranch ?? $"hotfix/77317-{target}"
    };

    [Fact]
    public async Task Cherry_pick_pushes_a_new_branch_with_only_the_card_commits_and_leaves_the_clone_alone()
    {
        var before = G(_clone, "rev-parse", "HEAD");
        var releaseTip = G(_origin, "rev-parse", "release-3.274.10");

        var report = await GitPromotion.RunAsync(_clone, Claim("release-3.274.10"), CancellationToken.None);

        Assert.Equal("pushed", report.Outcome);
        Assert.Equal("hotfix/77317-release-3.274.10", report.NewBranch);
        Assert.Equal(2, report.CommitCount);

        // a branch nova está no origin: sobre a release, com os dois commits e o "(cherry picked from ...)"
        var log = G(_origin, "log", "--format=%s", "hotfix/77317-release-3.274.10");
        Assert.Equal(["AB#77317 corrige a2", "AB#77317 cria b", "só da release", "inicial"], log.Split('\n'));
        Assert.Contains("(cherry picked from commit", G(_origin, "log", "-1", "--format=%B", "hotfix/77317-release-3.274.10"));
        Assert.DoesNotContain("edge muda a2", log);

        // a release, a principal e o clone de trabalho não mudaram
        Assert.Equal(releaseTip, G(_origin, "rev-parse", "release-3.274.10"));
        Assert.Equal(before, G(_clone, "rev-parse", "HEAD"));
        Assert.Equal("master", G(_clone, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Empty(G(_clone, "branch", "--list", "hotfix/77317-*"));        // sem branch local sobrando
        Assert.DoesNotContain(".prmake-wt", G(_clone, "worktree", "list"));  // worktree removido
    }

    [Fact]
    public async Task Conflict_aborts_pushes_nothing_and_lists_the_files()
    {
        var report = await GitPromotion.RunAsync(_clone, Claim("edge"), CancellationToken.None);

        Assert.Equal("conflict", report.Outcome);
        Assert.Equal(["a.txt"], report.ConflictFiles);
        Assert.Equal(2, report.CommitCount);
        Assert.Empty(G(_origin, "branch", "--list", "hotfix/77317-edge"));
        Assert.DoesNotContain(".prmake-wt", G(_clone, "worktree", "list"));
        Assert.Empty(G(_clone, "branch", "--list", "hotfix/77317-edge"));
    }

    [Fact]
    public async Task Missing_target_is_reported_without_creating_anything()
    {
        var report = await GitPromotion.RunAsync(_clone, Claim("release-9.9.9"), CancellationToken.None);

        Assert.Equal("missing_target", report.Outcome);
        Assert.Empty(G(_origin, "branch", "--list", "hotfix/77317-release-9.9.9"));
    }

    [Fact]
    public async Task Commits_already_in_the_target_give_no_changes()
    {
        // "feito" já tem os commits do card (um merge fast-forward da principal).
        G(_clone, "push", "origin", "origin/hotfix/77317:refs/heads/feito");

        var report = await GitPromotion.RunAsync(_clone, Claim("feito"), CancellationToken.None);

        Assert.Equal("no_changes", report.Outcome);
        Assert.Empty(G(_origin, "branch", "--list", "hotfix/77317-feito"));
    }

    [Fact]
    public async Task Existing_branch_name_is_never_reused()
    {
        G(_clone, "push", "origin", "origin/master:refs/heads/hotfix/77317-release-3.274.10");
        var taken = G(_origin, "rev-parse", "hotfix/77317-release-3.274.10");

        var report = await GitPromotion.RunAsync(_clone, Claim("release-3.274.10"), CancellationToken.None);

        Assert.Equal("pushed", report.Outcome);
        Assert.Equal("hotfix/77317-release-3.274.10-2", report.NewBranch);
        Assert.Equal(taken, G(_origin, "rev-parse", "hotfix/77317-release-3.274.10"));
    }

    [Fact]
    public async Task Branch_cut_from_a_release_only_takes_the_commits_after_that_release()
    {
        // principal do card 88888 cortada da release (que tem 1 commit a mais que a master): só 1 commit do card.
        Branch("hotfix/88888", "release-3.274.10", () => { Write("c.txt", "c\n"); Commit("AB#88888 muda c"); });
        G(_clone, "checkout", "master");
        var claim = Claim("master", baseBranch: "release-3.274.10", newBranch: "hotfix/88888-master");
        claim.Card = "88888";
        claim.SourceBranch = "hotfix/88888";

        var report = await GitPromotion.RunAsync(_clone, claim, CancellationToken.None);

        Assert.Equal("pushed", report.Outcome);
        Assert.Equal(1, report.CommitCount);
        Assert.Equal(["AB#88888 muda c", "inicial"], G(_origin, "log", "--format=%s", "hotfix/88888-master").Split('\n'));
    }

    [Fact]
    public async Task Direct_mode_does_not_touch_git()
    {
        var report = await GitPromotion.RunAsync(_clone, Claim("release-3.274.10", mode: "direct"), CancellationToken.None);

        Assert.Equal("pushed", report.Outcome);
        Assert.Equal("hotfix/77317", report.NewBranch);
        Assert.Empty(G(_origin, "branch", "--list", "hotfix/77317-release-3.274.10"));
    }

    [Theory]
    [InlineData("--upload-pack=touch /tmp/x")]
    [InlineData("-b")]
    [InlineData("a b")]
    [InlineData("a..b")]
    [InlineData("x;rm -rf /")]
    [InlineData("")]
    public async Task Unsafe_branch_names_are_refused_before_running_git(string name)
    {
        var report = await GitPromotion.RunAsync(_clone, Claim(name), CancellationToken.None);
        Assert.Equal("error", report.Outcome);
        Assert.Contains("inválido", report.Message);
    }

    [Fact]
    public async Task Missing_clone_folder_is_an_error()
    {
        var report = await GitPromotion.RunAsync(Path.Combine(_root, "nao-existe"), Claim("edge"), CancellationToken.None);
        Assert.Equal("error", report.Outcome);
    }

    // ── auxiliares ───────────────────────────────────────────────────────────────────────────────

    private void Branch(string name, string from, Action work)
    {
        G(_clone, "checkout", "-b", name, from.StartsWith("release") || from == "master" ? $"origin/{from}" : from);
        work();
        G(_clone, "push", "origin", name);
    }

    private void Write(string file, string content) => File.WriteAllText(Path.Combine(_clone, file), content);

    private void Commit(string message)
    {
        G(_clone, "add", "-A");
        G(_clone, "commit", "-m", message);
    }

    /// <summary>Roda o git e devolve a saída (falha o teste se o comando falhar).</summary>
    private static string G(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} falhou: {error}");
        return output.Trim();
    }
}
