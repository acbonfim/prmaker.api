using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.prform.Execution;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0085 r5 (1.6): o Especialista do caso executa as ações pedidas na conversa (Timeline, card, horas, PR, itens), como quem
/// pediu, e continua sem alterar o plano.
/// </summary>
public class CaseSpecialistGuardTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Asker = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("prmake_timeline")]
    [InlineData("prmake_devops")]
    [InlineData("prmake_pr_open")]
    [InlineData("prmake_work_task")]
    [InlineData("prmake_work_item_cancel")]
    [InlineData("prmake_work_item_execute")]
    [InlineData("prmake_analysis_items")]
    [InlineData("prmake_work_item_create")]
    [InlineData("prmake_work_item_update")]
    [InlineData("prmake_work_item_add_task")]
    [InlineData("prmake_tool_run")]
    [InlineData("prmake_work_item_file")]
    [InlineData("prmake_work_item_files")]
    [InlineData("prmake_case_reply")]
    [InlineData("prmake_plan")]
    public void Actions_asked_in_the_conversation_are_allowed(string tool) =>
        Assert.True(CaseSpecialistMiddleware.IsAllowed(tool));

    [Theory]
    [InlineData("prmake_advance")]
    [InlineData("prmake_step")]
    [InlineData("prmake_steps")]
    [InlineData("prmake_correction")]
    [InlineData("prmake_answer")]
    [InlineData("prmake_ask")]
    [InlineData("prmake_cancel_questions")]
    [InlineData("prmake_block")]
    [InlineData("prmake_unblock")]
    [InlineData("prmake_checkpoint")]
    [InlineData("prmake_file")]
    [InlineData("prmake_control")]
    [InlineData("prmake_plan_status")]
    [InlineData("prmake_log")]
    [InlineData("prmake_notes")]
    [InlineData("prmake_link")]
    [InlineData("prmake_handoff")]
    [InlineData("prmake_code_ref")]
    [InlineData("prmake_evidence")]
    public void What_changes_the_plan_stays_blocked(string tool) =>
        Assert.False(CaseSpecialistMiddleware.IsAllowed(tool));

    [Fact]
    public void Every_allowed_tool_exists_and_every_acting_tool_is_allowed()
    {
        var names = typeof(PrmakeMcpTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n is not null)
            .ToHashSet();
        Assert.All(CaseSpecialistMiddleware.AllowedTools, t => Assert.Contains(t, names));
        Assert.All(CaseSpecialistMiddleware.ActingTools, t => Assert.Contains(t, CaseSpecialistMiddleware.AllowedTools));
    }

    [Fact]
    public async Task Plan_tool_is_refused_with_403()
    {
        var (context, nextCalled) = await RunAsync("prmake_advance");
        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Acting_tool_runs_as_who_asked()
    {
        ClaimsPrincipal? seen = null;
        var (context, nextCalled) = await RunAsync("prmake_timeline", c => seen = c.User);
        Assert.True(nextCalled());
        Assert.Equal(Asker.ToString(), seen!.FindFirst("ExternalId")!.Value);
        Assert.Equal(Owner.ToString(), seen.FindFirst(CaseClaims.OwnerExternalId)!.Value);
        Assert.Equal("Ana Lima", CaseClaims.RequestedByOf(seen));
        Assert.NotNull(CaseClaims.SpecialistOf(seen));
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Reading_keeps_the_owner_identity()
    {
        ClaimsPrincipal? seen = null;
        await RunAsync("prmake_plan", c => seen = c.User);
        Assert.Equal(Owner.ToString(), seen!.FindFirst("ExternalId")!.Value);
        Assert.Equal("Ana Lima", CaseClaims.RequestedByOf(seen));
    }

    [Fact]
    public void Without_a_person_the_owner_acts()
    {
        var user = Principal();
        var swapped = CaseSpecialistMiddleware.WithRequester(user, new SpecialistRequester(null, "", Owner, "Dono"), actAsRequester: true);
        Assert.Equal(Owner.ToString(), swapped.FindFirst("ExternalId")!.Value);
        Assert.Equal("Dono", CaseClaims.RequestedByOf(swapped));
    }

    [Theory]
    [InlineData("hotfix/77434-rv", "release-version", null, null)]
    [InlineData("hotfix/77434", "development", null, null)]
    [InlineData("hotfix/77434-rv-release-3.2", "release/3.2", null, null)]
    [InlineData("bugfix/77434", "development", null, "fora do padrao")]
    [InlineData("hotfix/77999-rv", "release-version", "nao e deste card", null)]
    [InlineData("77434-rv", "release-version", "branch completa", null)]
    public void Pr_source_branch_follows_the_card_rules(string source, string target, string? error, string? warning)
    {
        var (e, w) = BranchRules.CheckPrSource(source, "77434", "hotfix/77434", target, BranchRules.ParseSuffixes(null));
        if (error is null) Assert.Null(e); else Assert.Contains(error, e);
        if (warning is null) Assert.Null(w); else Assert.Contains(warning, w);
    }

    [Theory]
    [InlineData("AB#{card} {TARGET}", "release-version", "AB#77434 RELEASE-VERSION")]
    [InlineData(null, "qa", "AB#77434 QA")]
    [InlineData("{card} -> {target}", "development", "77434 -> development")]
    public void Pr_title_comes_from_the_pattern(string? pattern, string target, string expected) =>
        Assert.Equal(expected, PullRequestMcpTools.DefaultTitle(pattern, "77434", target));

    // ── Apoio ────────────────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim("ExternalId", Owner.ToString()),
        new Claim(ClaimTypes.Name, "Dono"),
        new Claim(CaseClaims.Card, "77434"),
        new Claim(CaseClaims.Request, RequestId.ToString())
    ], "test"));

    private static async Task<(DefaultHttpContext Context, Func<bool> NextCalled)> RunAsync(string tool, Action<HttpContext>? onNext = null)
    {
        var called = false;
        var middleware = new CaseSpecialistMiddleware(c =>
        {
            called = true;
            onNext?.Invoke(c);
            return Task.CompletedTask;
        });
        var body = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"TOOL","arguments":{"card":"77434"}}}""".Replace("TOOL", tool);
        var context = new DefaultHttpContext { User = Principal() };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/mcp";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        var queue = QueueProxy.Create(new SpecialistRequester(Asker, "Ana Lima", Owner, "Dono"));
        await middleware.InvokeAsync(context, queue, new MemoryCache(new MemoryCacheOptions()));
        return (context, () => called);
    }

    /// <summary>Fila falsa: só responde quem pediu o pedido do especialista.</summary>
    public class QueueProxy : DispatchProxy
    {
        private SpecialistRequester? _requester;

        public static IExecutionQueueApplication Create(SpecialistRequester requester)
        {
            var proxy = Create<IExecutionQueueApplication, QueueProxy>();
            ((QueueProxy)(object)proxy)._requester = requester;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IExecutionQueueApplication.GetActiveSpecialistRequesterAsync)
                ? Task.FromResult(_requester)
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
