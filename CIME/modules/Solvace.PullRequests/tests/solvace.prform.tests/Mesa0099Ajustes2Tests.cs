using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using solvace.azure.domain.Models;
using solvace.executionplans.domain.Responses;
using solvace.prform.Controllers;
using solvace.prform.Execution;
using solvace.prform.PluginSettings;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (ajustes 2): papel <c>management</c> (Mesa só para admin/management, sem quebrar a automação), perfil de quem pede
/// (persona PO / gestor de problemas) e o acompanhamento das entregas do épico.
/// </summary>
public class Mesa0099Ajustes2Tests
{
    // ---------------------------------------------------------------- acesso

    private static HttpContext Http(IEnumerable<Claim> claims, string? header = null, bool authenticated = true)
    {
        var identity = authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        if (header is not null) http.Request.Headers["X-Execution-Client"] = header;
        return http;
    }

    private static IEnumerable<Claim> Roles(params string[] roles) => roles.Select(r => new Claim(ClaimTypes.Role, r));

    private static IActionResult? Authorize(HttpContext http)
    {
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        new DeskAccessAttribute().OnAuthorization(context);
        return context.Result;
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("management")]
    [InlineData("MANAGEMENT")]
    [InlineData("user,management")]
    public void Desk_roles_pass(string roles) => Assert.Null(Authorize(Http(Roles(roles.Split(',')))));

    [Theory]
    [InlineData("user")]
    [InlineData("support")]
    [InlineData("gestor")]
    [InlineData("user,support,gestor")]
    public void Other_roles_get_403_with_code(string roles)
    {
        var result = Assert.IsType<ObjectResult>(Authorize(Http(Roles(roles.Split(',')))));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Contains("forbidden", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void Short_role_claim_counts_too() => Assert.Null(Authorize(Http([new Claim("role", "management")])));

    [Fact]
    public void Case_token_executor_and_skill_keep_working_for_a_dev()
    {
        // token do caso (especialista): papéis do dono, que pode ser dev
        Assert.Null(Authorize(Http(Roles("user").Append(new Claim(CaseClaims.Request, Guid.NewGuid().ToString())))));
        // credencial do executor
        Assert.Null(Authorize(Http(Roles("user").Append(new Claim(ExecutorClaims.WorkerId, Guid.NewGuid().ToString())))));
        // skill pelo executor/terminal (gestao.sh → DevOps/query) com a api-key de um dev
        Assert.Null(Authorize(Http(Roles("support"), header: "skill")));
        Assert.NotNull(Authorize(Http(Roles("support"), header: "outro")));
    }

    [Fact]
    public void Anonymous_is_left_to_Authorize() => Assert.Null(Authorize(Http([], authenticated: false)));

    private static bool HasDesk(MemberInfo m) => m.GetCustomAttributes<DeskAccessAttribute>(true).Any();

    private static List<MethodInfo> Actions(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute)).ToList();

    [Fact]
    public void DevOps_controller_is_desk_only()
    {
        Assert.True(HasDesk(typeof(DevOpsController)));
        Assert.Contains(typeof(DevOpsController).GetCustomAttributes<AuthorizeAttribute>(true), a => string.IsNullOrEmpty(a.Roles));
    }

    [Fact]
    public void Epics_writes_are_desk_only_and_reads_stay_open()
    {
        var type = typeof(EpicsController);
        Assert.False(HasDesk(type), "o controller inteiro não — by-card (chips da Bancada) e as leituras seguem abertas");
        var actions = Actions(type);
        foreach (var name in new[] { "Create", "Update", "SetStatus", "SetBoardFilters", "AddCard", "RemoveCard" })
            Assert.True(HasDesk(actions.Single(a => a.Name == name)), $"{name} grava: precisa de [DeskAccess]");
        foreach (var name in new[] { "List", "Tags", "Get", "ByCard", "Monitor" })
            Assert.False(HasDesk(actions.Single(a => a.Name == name)), $"{name} é leitura");
        var monitor = actions.Single(a => a.Name == "Monitor");
        Assert.Equal("{number:long}/monitor", monitor.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void Plugin_configuration_accepts_management()
    {
        var roles = typeof(PluginConfigurationController).GetCustomAttributes<AuthorizeAttribute>(true)
            .Select(a => a.Roles).Single(r => !string.IsNullOrEmpty(r))!.Split(',').Select(r => r.Trim()).ToList();
        Assert.Contains("management", roles);
        Assert.Contains("user", roles);
        Assert.Contains("support", roles);
    }

    [Fact]
    public void Reverse_engineering_approver_roles_offer_management()
    {
        var entry = PluginSettingsCatalog.Fields.Single(e => e.Key == SkillsConfigurationKeys.ReverseEngineeringApproverRoles);
        Assert.Contains(entry.Options!, o => o.Value == "management");
    }

    // ---------------------------------------------------------------- perfil

    [Theory]
    [InlineData("management", "management")]
    [InlineData("management,gestor", "management")]
    [InlineData("management,user", "dev")]
    [InlineData("management,support", "dev")]
    [InlineData("management,admin", "dev")]
    [InlineData("user", "dev")]
    [InlineData("gestor", "dev")]
    [InlineData("", "dev")]
    public void Profile_is_management_only_without_dev_roles(string roles, string expected) =>
        Assert.Equal(expected, DeskRoles.Profile(roles.Split(',', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void Persona_for_management_forbids_code_tools_and_points_to_prmake_tools()
    {
        var md = DeskPersona.Markdown("Ana PO", ["management"]);
        Assert.Contains(DeskPersona.Heading, md);
        Assert.Contains("Ana PO", md);
        Assert.Contains("perfil `management`", md);
        Assert.Contains("**Não use** git, gh", md);
        Assert.Contains("prmake_epic_monitor", md);
        Assert.Contains("prmake_aws_status", md);
        Assert.Contains("prmake_base_search", md);
        Assert.Contains("validar-ambiente", md);
    }

    [Fact]
    public void Persona_for_dev_allows_read_only_code()
    {
        var md = DeskPersona.Markdown(null, ["user", "management"]);
        Assert.Contains("perfil `dev`", md);
        Assert.DoesNotContain("**Não use**", md);
    }

    // ---------------------------------------------------------------- acompanhamento

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static EpicCardResponse Card(string card, string role = "linked", string? title = null) =>
        new() { Card = card, Role = role, Title = title, WorkItemType = "Bug", AddedAt = T0, AddedByName = "Ana" };

    private const string Snapshot = """
        [{"key":"qa","label":"QA","verdict":"deployed","message":"Nosso código está em QA",
          "pipelines":[{"primary":true,"verdict":"deployed","deployedAt":"2026-11-21T10:00:00Z","lastRunAt":"2026-11-21T09:00:00Z"},
                       {"primary":false,"verdict":"deployed","deployedAt":"2026-11-23T10:00:00Z"}]},
         {"key":"sandboxefeso","label":"Sandboxefeso","verdict":"deploying","pipelines":[{"primary":true,"verdict":"deploying","lastRunAt":"2026-11-22T08:00:00Z"}]},
         {"key":"sandboxrt","label":"SandboxRT","verdict":"failed","pipelines":[]},
         {"key":"prod","label":"Produção","verdict":"absent","pipelines":"x"},
         {"key":"dev","label":"DEV","verdict":"not-applicable"},
         "lixo"]
        """;

    [Fact]
    public void Monitor_joins_devops_prs_and_aws_per_card()
    {
        var cards = new[] { Card("100", "created", "foto"), Card("200"), Card("300") };
        var summaries = new[]
        {
            new AzureCardSummaryResponse { Id = 100, Title = "Título vivo", State = "Active", BoardColumn = "Doing", WorkItemType = "User Story",
                AssignedTo = "Bia", Url = "https://dev.azure.com/x/100", ChangedDate = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc) },
        };
        var prs = new[]
        {
            new EpicMonitorPrRow("100", "solvace/api", 12, "https://gh/12", "fix", "MERGED", false, "qa", "bugfix/100-qa", T0.AddDays(1), T0, null),
            new EpicMonitorPrRow("100", "solvace/web", 13, "https://gh/13", "fix web", "OPEN", true, "", "bugfix/100", null, T0, null),
            new EpicMonitorPrRow("200", "solvace/api", 14, "https://gh/14", "x", "CLOSED", false, "master", "hotfix/200", null, T0, null),
        };
        var aws = new[] { new EpicMonitorAwsRow { Card = "100", CollectedAt = T0.AddDays(4), CollectedBy = "Caio", Environments = Snapshot } };

        var result = EpicMonitorMapping.Build(cards, summaries, prs, aws);

        Assert.Equal(3, result.Count);
        var c100 = result.Single(c => c.Card == "100");
        Assert.Equal("Título vivo", c100.Title);
        Assert.Equal("User Story", c100.Type);
        Assert.Equal("Active", c100.State);
        Assert.Equal("Doing", c100.Column);
        Assert.Equal("Bia", c100.AssignedTo);
        Assert.Equal("created", c100.Role);
        Assert.Equal(["merged", "open"], c100.Prs.Select(p => p.Status));
        Assert.Equal("qa", c100.Prs[0].TargetBranch);
        Assert.Null(c100.Prs[1].TargetBranch);
        Assert.True(c100.Prs[1].IsDraft);
        Assert.Equal(T0.AddDays(4), c100.AwsCollectedAt);
        Assert.Equal(T0.AddDays(4), c100.UpdatedAt);
        Assert.Equal("Caio", c100.AwsCollectedBy);

        var envs = c100.Environments!;
        Assert.Equal(["qa", "sandboxefeso", "sandboxrt", "prod", "dev"], envs.Select(e => e.Key));
        Assert.Equal(["deployed", "pending", "failed", "pending", "unknown"], envs.Select(e => e.Status));
        // implantado: deployedAt da esteira principal (a secundária, mais nova, não decide)
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero), envs[0].At);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero), envs[1].At);
        Assert.Null(envs[3].At);
        Assert.True(envs.Single(e => e.Key == "prod").IsProduction);
        Assert.Equal("Produção", envs.Single(e => e.Key == "prod").Name);
        Assert.False(envs[0].IsProduction);

        // sem resumo do DevOps: foto do épico; sem retrato: null
        var c200 = result.Single(c => c.Card == "200");
        Assert.Equal("Bug", c200.Type);
        Assert.Null(c200.State);
        Assert.Null(c200.Url);
        Assert.Equal("closed", c200.Prs.Single().Status);
        Assert.Null(c200.Environments);
        Assert.Empty(result.Single(c => c.Card == "300").Prs);
        // mais recente primeiro
        Assert.Equal("100", result[0].Card);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{oops")]
    [InlineData("{}")]
    [InlineData("\"x\"")]
    public void Monitor_tolerates_broken_snapshots(string? json) => Assert.Empty(EpicMonitorMapping.Environments(json));

    [Fact]
    public void Monitor_reads_the_full_snapshot_shape_too() =>
        Assert.Single(EpicMonitorMapping.Environments("""{"environments":[{"key":"qa","label":"QA","verdict":"waiting"}]}"""));

    [Fact]
    public void Monitor_text_has_summary_legend_and_one_line_per_card()
    {
        var cards = EpicMonitorMapping.Build(
            [Card("100"), Card("200")],
            [],
            [new EpicMonitorPrRow("100", "solvace/api", 12, "u", "t", "MERGED", false, "master", "b", null, T0, null)],
            [new EpicMonitorAwsRow { Card = "100", CollectedAt = T0, CollectedBy = "x", Environments = Snapshot.Replace("\"absent\"", "\"deployed\"") }]);
        var text = EpicMonitorMapping.Text(7, "Release", cards);
        Assert.Contains("E7 — Release", text);
        Assert.Contains("2 cards · 1 com PR mesclado · 1 em produção · 1 sem retrato da AWS", text);
        Assert.Contains("solvace/api#12 merged → master", text);
        Assert.Contains("✓ QA", text);
        Assert.Contains("sem retrato da AWS (peça a coleta", text);
        Assert.Contains("PRs: nenhum registrado", text);
        Assert.Contains("Nenhum card", EpicMonitorMapping.Text(7, "Vazio", []));
    }
}
