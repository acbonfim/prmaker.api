using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.github.application.Contract;
using solvace.prform.domain.Responses;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Promotion;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0085 r4: renderizador dos modelos (ferramentas do especialista), from-template e tipos de item.</summary>
public class WorkItemTemplateRendererTests
{
    public const string Strategy = """
    {"repositories": [
       {"match": "edv-solvace-apps", "kind": "revamp-frontend"}, {"match": "edv-solvace-api", "kind": "integration-api"},
       {"match": "edv-solvace", "kind": "legacy"}, {"match": "revamp-*", "kind": "revamp-backend"}],
     "flows": {
       "producao": {
         "legacy": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "from": "qa", "target": "qa"}]},
         "revamp-backend": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}]},
         "revamp-frontend": {"base": "master", "prs": [{"suffix": "-dev", "from": "development", "target": "development"}, {"suffix": "-qa", "from": "qa", "target": "qa"}]}},
       "release": {
         "legacy": {"baseOptions": ["release-version", "hotfix-version"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]},
         "revamp-backend": {"baseOptions": ["release-version", "hotfix-version"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]},
         "revamp-frontend": {"baseOptions": ["edge"], "askBase": true, "prs": [{"suffix": "", "target": "{base}"}]}}
     }}
    """;

    public const string Profiles = """
    {"profiles": {
      "legacy": {"label": "Netcore", "targets": [{"branch": "hotfix-version"}]},
      "integration-api": {"label": "API", "targets": [{"branch": "release-version"}, {"branch": "master"}]},
      "revamp-frontend": {"label": "Angular", "targets": [{"branch": "release-version"}, {"branch": "edge"}, {"branch": "master", "draft": true}]},
      "revamp-backend": {"label": "Revamp", "targets": [{"branch": "release-version"}, {"branch": "edge"}, {"branch": "master", "draft": true}]}
    }}
    """;

    private static WorkItemTemplateContext Ctx(string? repository = "edv-solvace-apps", string? flow = "producao", string? type = "bug",
        params string[] cardRepos) => new()
    {
        Card = "77434",
        Type = type,
        Flow = flow,
        State = "Active",
        MainBranch = "hotfix/77434",
        Suffixes = BranchRules.ParseSuffixes(null),
        Repository = repository,
        CardRepositories = cardRepos.Length > 0 ? cardRepos : repository is null ? [] : [repository],
        BranchStrategyJson = Strategy,
        Profiles = PromotionProfileSet.Parse(Profiles),
        QaBranchPattern = "qa*, homolog",
        TicketSystem = "Freshservice, Jira"
    };

    private static JsonObject Template(string key)
    {
        var all = JsonNode.Parse(SeedSpecialistTools0085c.NewTemplates)!.AsArray()
            .Append(JsonNode.Parse(SeedSpecialistTools0085c.NewAdjustment));
        return (JsonObject)all.OfType<JsonObject>().Single(t => (string?)t["key"] == key).DeepClone();
    }

    private static string Render(string text, WorkItemTemplateContext ctx, Dictionary<string, string>? answers = null) =>
        WorkItemTemplateRenderer.RenderText(text, ctx, WorkItemTemplateRenderer.Vars(ctx, null, answers));

    // ── Marcadores ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Markers_are_rendered()
    {
        var ctx = Ctx();
        var text = Render("{card} {branch}{suffix:release-version} {branch}{suffix:@target} {ask:target} {qaBranch} {base} {repoKind} {ticketSystem} {flow} [{flowTargets}] [{promotionTargets}] {unknown} {ask:missing}",
            ctx, new() { ["target"] = "edge" });
        Assert.Equal("77434 hotfix/77434-rv hotfix/77434-edge edge qa master revamp-frontend Freshservice producao [development, qa] [release-version, edge, master] {unknown} {ask:missing}", text);
    }

    [Fact]
    public void Base_follows_the_flow_of_the_repository_kind()
    {
        Assert.Equal("master", Render("{base}", Ctx("edv-solvace")));
        Assert.Equal("release-version", Render("{base}", Ctx("edv-solvace", flow: "release")));
        Assert.Equal("edge", Render("{base}", Ctx("edv-solvace-apps", flow: "release")));
    }

    [Fact]
    public void Repository_is_the_card_pr_repository_and_the_answer_wins()
    {
        var ctx = Ctx("edv-solvace-apps", cardRepos: ["edv-solvace-apps", "revamp-Users"]);
        Assert.Equal("edv-solvace-apps revamp-frontend", Render("{repository} {repoKind}", ctx));
        var vars = WorkItemTemplateRenderer.Vars(ctx, "revamp-Users", null);
        Assert.Equal("revamp-Users revamp-backend development", WorkItemTemplateRenderer.RenderText("{repository} {repoKind} {flowTargets}", ctx, vars));
    }

    [Theory]
    [InlineData("edv-solvace-apps", new[] { "release-version", "edge", "master" })]
    [InlineData("revamp-RCA", new[] { "release-version", "edge", "master" })]
    [InlineData("edv-solvace", new[] { "hotfix-version" })]
    [InlineData("edv-solvace-api", new[] { "release-version", "master" })]
    public void Promotion_targets_come_from_the_profile_of_the_repository_kind(string repo, string[] expected)
    {
        var ctx = Ctx(repo);
        Assert.Equal(expected, WorkItemTemplateRenderer.PromotionTargets(ctx, WorkItemTemplateRenderer.KindOf(ctx, repo)));
    }

    [Fact]
    public void Ask_options_resolve_the_promotion_targets_and_the_default()
    {
        var ctx = Ctx("edv-solvace-apps", cardRepos: ["edv-solvace-apps", "edv-solvace"]);
        var asks = WorkItemTemplateRenderer.ResolveAsks(Template("pr-para-branch"), ctx);
        var repo = asks.OfType<JsonObject>().Single(a => (string?)a["key"] == "repository");
        Assert.Equal("edv-solvace-apps", (string?)repo["default"]);
        Assert.Equal(["edv-solvace-apps", "edv-solvace"], repo["suggestions"]!.AsArray().Select(x => (string?)x));
        var target = asks.OfType<JsonObject>().Single(a => (string?)a["key"] == "target");
        var options = target["options"]!.AsArray().Select(x => (string?)x).ToList();
        Assert.Equal(["release-version", "edge", "master"], options);
        Assert.DoesNotContain("hotfix-version", options); // nunca hotfix-version em revamp-frontend
        Assert.Equal(["hotfix-version"], target["optionsByRepository"]!["edv-solvace"]!.AsArray().Select(x => (string?)x));
    }

    // ── when ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void When_flow_targets_hides_qa_tools_where_the_flow_has_no_qa()
    {
        var qa = JsonNode.Parse("""{"key": "validar-em-qa", "when": {"flowTargets": ["qa"]}}""")!.AsObject();
        // revamp-backend (edv… "revamp-*") não tem QA no fluxo de produção.
        var (ok, reason) = WorkItemTemplateRenderer.Evaluate(qa, Ctx("revamp-Users"));
        Assert.False(ok);
        Assert.Contains("revamp-backend", reason);
        Assert.Contains("qa", reason);
        Assert.True(WorkItemTemplateRenderer.Evaluate(qa, Ctx("edv-solvace")).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(qa, Ctx("edv-solvace-apps")).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(qa, Ctx(repository: null)).Applicable);        // repositório não lido não bloqueia
        Assert.True(WorkItemTemplateRenderer.Evaluate(qa, Ctx("outro-repo")).Applicable);             // sem regra para o tipo
    }


    [Fact]
    public void When_filters_by_type_flow_repo_kind_and_state()
    {
        var hotfix = JsonNode.Parse("""{"key": "pr-hotfix-version", "when": {"repoKinds": ["legacy", "revamp-backend"]}}""")!.AsObject();
        var (ok, reason) = WorkItemTemplateRenderer.Evaluate(hotfix, Ctx("edv-solvace-apps"));
        Assert.False(ok);
        Assert.Contains("revamp-frontend", reason);
        Assert.True(WorkItemTemplateRenderer.Evaluate(hotfix, Ctx("edv-solvace")).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(hotfix, Ctx("edv-solvace-apps"), "revamp-Users").Applicable);

        var closing = Template("fechamento-code-fix");
        (ok, reason) = WorkItemTemplateRenderer.Evaluate(closing, Ctx(type: "user-story"));
        Assert.False(ok);
        Assert.Contains("Bug", reason);
        Assert.True(WorkItemTemplateRenderer.Evaluate(closing, Ctx(type: null)).Applicable); // tipo não lido não bloqueia

        var flow = Template("prs-fluxo-producao");
        Assert.False(WorkItemTemplateRenderer.Evaluate(flow, Ctx("edv-solvace", flow: "release")).Applicable);
        Assert.False(WorkItemTemplateRenderer.Evaluate(flow, Ctx("revamp-Users")).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(flow, Ctx("edv-solvace", flow: null)).Applicable);

        var states = JsonNode.Parse("""{"when": {"states": ["Dev Test in QA"]}}""")!.AsObject();
        Assert.False(WorkItemTemplateRenderer.Evaluate(states, Ctx()).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(JsonNode.Parse("{}")!.AsObject(), Ctx()).Applicable);
    }

    // ── Render (from-template) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_fills_the_branch_with_the_suffix_of_the_chosen_target()
    {
        var item = WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx(), new Dictionary<string, string> { ["target"] = "release-version" });
        Assert.Equal("pull-request", item.Type);
        Assert.Equal(["branch", "cherry-pick", "pr"], item.Tasks.Select(t => t.Kind));
        Assert.Equal("Criar hotfix/77434-rv a partir de release-version", item.Tasks[0].Title);
        Assert.Equal("edv-solvace-apps", item.Tasks[0].Params["repository"]);
        Assert.Equal("hotfix/77434-rv", item.Tasks[2].Params["source"]);
        Assert.Equal("release-version", item.Tasks[2].Params["target"]);
        Assert.Contains("release-version, edge, master", item.Description);
    }

    [Fact]
    public void Card_brief_template_renders_for_bug_and_user_story_without_answers()
    {
        var template = JsonNode.Parse(SeedCardBrief0089.NewTemplate)!.AsObject();
        foreach (var type in new[] { "bug", "user-story" })
        {
            var item = WorkItemTemplateRenderer.Render((JsonObject)template.DeepClone(), Ctx(type: type), null);
            Assert.Equal("text", item.Type);
            var task = Assert.Single(item.Tasks);
            Assert.Equal("card-brief", task.Kind);
            Assert.Equal("não", task.Params["timeline"]);
            Assert.Equal("", task.Params["focus"]);
        }
        var focused = WorkItemTemplateRenderer.Render(template, Ctx(),
            new Dictionary<string, string> { ["timeline"] = "sim", ["focus"] = "o que o cliente quer no relatório" });
        Assert.Equal("sim", focused.Tasks[0].Params["timeline"]);
        Assert.Equal("o que o cliente quer no relatório", focused.Tasks[0].Params["focus"]);
    }

    [Fact]
    public void Render_requires_the_required_answers()
    {
        var e = Assert.Throws<WorkItemTemplateException>(() => WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx(), null));
        Assert.Equal(WorkItemTemplateException.InvalidAnswers, e.Code);
        Assert.Equal(["target"], e.Missing); // repository veio do padrão (PR do card)

        var noRepo = Assert.Throws<WorkItemTemplateException>(() =>
            WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx(repository: null), new Dictionary<string, string> { ["target"] = "edge" }));
        Assert.Equal(["repository"], noRepo.Missing);
    }

    [Fact]
    public void Render_blocks_a_target_of_another_repository_kind_but_accepts_other_branches()
    {
        var e = Assert.Throws<WorkItemTemplateException>(() => WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx("edv-solvace-apps"),
            new Dictionary<string, string> { ["target"] = "hotfix-version" }));
        Assert.Contains("hotfix-version", e.Message);

        WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx("edv-solvace-apps"),
            new Dictionary<string, string> { ["target"] = "hotfix-version", ["repository"] = "edv-solvace" });
        var other = WorkItemTemplateRenderer.Render(Template("pr-para-branch"), Ctx("edv-solvace-apps"),
            new Dictionary<string, string> { ["target"] = "feature/x" });
        Assert.Equal("hotfix/77434-feature-x", other.Tasks[0].Params["branch"]);
    }

    [Fact]
    public void Render_checks_closed_options_and_not_applicable()
    {
        var bad = Assert.Throws<WorkItemTemplateException>(() => WorkItemTemplateRenderer.Render(Template("fechamento-sem-codigo"), Ctx(),
            new Dictionary<string, string> { ["classification"] = "inventada" }));
        Assert.Equal(WorkItemTemplateException.InvalidAnswers, bad.Code);

        var ok = WorkItemTemplateRenderer.Render(Template("fechamento-sem-codigo"), Ctx(),
            new Dictionary<string, string> { ["classification"] = "user-education" });
        Assert.Equal("user-education", ok.Tasks[1].Params["value"]);
        Assert.Equal("Classificar (user-education)", ok.Tasks[1].Title);

        var na = Assert.Throws<WorkItemTemplateException>(() => WorkItemTemplateRenderer.Render(Template("fechamento-code-fix"), Ctx(type: "user-story"), null));
        Assert.Equal(WorkItemTemplateException.NotApplicable, na.Code);
    }

    [Fact]
    public void Render_inherits_the_executor_of_the_kind_and_drops_unanswered_optional_asks()
    {
        var executors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["manual"] = "user", ["devops-action"] = "claude" };
        var qa = WorkItemTemplateRenderer.Render(Template("validar-em-qa"), Ctx(), null, executors);
        Assert.Equal("user", qa.Tasks[0].Executor);
        Assert.Equal("claude", qa.Tasks[1].Executor);

        var review = WorkItemTemplateRenderer.Render(Template("revisar-pr"), Ctx(), new Dictionary<string, string> { ["pullRequest"] = "#42" });
        Assert.Equal("review", review.Type);
        Assert.Equal(string.Empty, review.Tasks[0].Params["focus"]);
        Assert.Equal("Revisar o PR #42", review.Tasks[0].Title);
    }

    [Fact]
    public void Seeded_templates_only_use_known_markers_and_render_completely()
    {
        var templates = JsonNode.Parse(SeedSpecialistTools0085c.NewTemplates)!.AsArray().OfType<JsonObject>()
            .Append(JsonNode.Parse(SeedSpecialistTools0085c.NewAdjustment)!.AsObject())
            .ToList();
        Assert.Equal(14, templates.Count);
        var types = JsonNode.Parse(SeedSpecialistTools0085c.ItemTypes)!.AsArray().Select(t => (string?)t!["key"]).ToHashSet();
        var kinds = JsonNode.Parse(SeedSpecialistTools0085c.NewKinds)!.AsArray().Select(t => (string?)t!["kind"])
            .Concat(["branch", "cherry-pick", "code", "pr", "devops-state", "sql-script", "validation", "comment", "manual", "ticket", "notify"]).ToHashSet();
        foreach (var t in templates)
        {
            var key = (string)t["key"]!;
            Assert.Contains((string?)t["type"], types);
            foreach (var text in Strings(t))
                Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"{key}: marcador desconhecido em \"{text}\"");
            var answers = new Dictionary<string, string> { ["target"] = "edge", ["classification"] = "user-education", ["pullRequest"] = "#1", ["focus"] = "x" };
            var ctx = Ctx("edv-solvace");
            if (t["when"]?["repoKinds"] is JsonArray rk && !rk.Any(k => (string?)k == "legacy"))
                ctx = Ctx("edv-solvace-apps");
            if (key is "pr-para-branch" or "resolver-conflito-promocao" && ctx.Repository == "edv-solvace")
                answers["target"] = "hotfix-version";
            var item = WorkItemTemplateRenderer.Render(t, ctx, answers);
            Assert.DoesNotContain('{', item.Title);
            foreach (var task in item.Tasks)
            {
                Assert.Contains(task.Kind, kinds);
                Assert.DoesNotContain('{', task.Title);
                Assert.All(task.Params.Values, v => Assert.DoesNotContain('{', v));
            }
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        _ => []
    };

    // ── Tipos de item ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Item_type_comes_from_the_request_the_template_or_task_and_maps_the_legacy_ones()
    {
        var settings = new WorkItemTypeSettings(WorkItemType.Defaults, new Dictionary<string, string> { ["revisar-pr"] = "review" });
        Assert.Equal("task", WorkItemApplication.ResolveType(null, null, settings));
        Assert.Equal("review", WorkItemApplication.ResolveType(null, "revisar-pr", settings));
        Assert.Equal("closing", WorkItemApplication.ResolveType("closing", "revisar-pr", settings));
        Assert.Equal("fix", WorkItemApplication.ResolveType("bug", null, settings));
        Assert.Equal("implementation", WorkItemApplication.ResolveType("User Story", null, settings));
        var e = Assert.Throws<WorkItemTypeInvalidException>(() => WorkItemApplication.ResolveType("epico", null, settings));
        Assert.Contains("pull-request", e.Options);
        Assert.Equal("epico", WorkItemApplication.ResolveType("epico", null, null)); // sem catálogo: só o formato
    }

    // ── from-template (serviço com stubs) ────────────────────────────────────────────────────────

    [Fact]
    public async Task From_template_builds_the_request_with_the_card_pr_repository()
    {
        var tools = Tools(prs: [Pr("acbonfim/edv-solvace-apps", 2), Pr("revamp-Users", 3)]);
        var request = await tools.BuildCreateRequestAsync("77434", new CreateFromTemplateRequest
        {
            TemplateKey = "pr-para-branch",
            Answers = new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement("edge") }
        }, CancellationToken.None);
        Assert.Equal("pr-para-branch", request.TemplateKey);
        Assert.Equal("pull-request", request.Type);
        Assert.Equal("Abrir PR para uma branch", request.Title);
        Assert.Equal("edv-solvace-apps", request.Tasks[0].Params!["repository"]);
        Assert.Equal("hotfix/77434-edge", request.Tasks[0].Params!["branch"]);
        Assert.Equal("claude", request.Tasks[0].Executor);

        var missing = await Assert.ThrowsAsync<WorkItemTemplateException>(() => tools.BuildCreateRequestAsync("77434",
            new CreateFromTemplateRequest { TemplateKey = "pr-para-branch" }, CancellationToken.None));
        Assert.Equal(["target"], missing.Missing);
        var notFound = await Assert.ThrowsAsync<WorkItemTemplateException>(() => tools.BuildCreateRequestAsync("77434",
            new CreateFromTemplateRequest { TemplateKey = "nao-existe" }, CancellationToken.None));
        Assert.Equal(WorkItemTemplateException.NotFound, notFound.Code);
    }

    [Fact]
    public async Task Catalog_annotates_applicability_and_resolves_the_asks()
    {
        var tools = Tools(prs: [Pr("edv-solvace-apps", 1)]);
        var catalog = await tools.CatalogAsync("77434", CancellationToken.None);
        Assert.Equal("edv-solvace-apps", (string?)catalog["repository"]);
        Assert.Equal("revamp-frontend", (string?)catalog["repoKind"]);
        Assert.Equal(11, catalog["itemTypes"]!.AsArray().Count);
        var templates = catalog["templates"]!.AsArray().OfType<JsonObject>().ToDictionary(t => (string)t["key"]!);
        Assert.False((bool)templates["pr-hotfix-version"]["applicable"]!);
        Assert.NotNull(templates["pr-hotfix-version"]["reason"]);
        Assert.True((bool)templates["pr-para-branch"]["applicable"]!);
        Assert.Equal("Resolver conflito do PR para <Destino com conflito>", (string?)templates["resolver-conflito-promocao"]["title"]);
        var target = templates["pr-para-branch"]["ask"]!.AsArray().OfType<JsonObject>().Single(a => (string?)a["key"] == "target");
        Assert.Equal(["release-version", "edge", "master"], target["options"]!.AsArray().Select(x => (string?)x));
        // sub-tarefas mantêm o marcador do ask para a tela pré-visualizar
        Assert.Contains("{suffix:@target}", (string?)templates["pr-para-branch"]["tasks"]![0]!["params"]!["branch"]);
    }

    [Fact]
    public async Task Without_prs_the_repository_comes_from_the_plan_pr_steps_and_then_the_default()
    {
        var planId = Guid.NewGuid();
        var withPlan = Tools(prs: [], plans: [new ExecutionPlanSummaryResponse { Id = planId, Phase = ExecutionPhase.Correction, Status = "running", CreatedAt = DateTimeOffset.UtcNow }],
            plan: new ExecutionPlanResponse
            {
                Id = planId,
                Steps = [new ExecutionStepResponse { Key = "corrigir-edv-solvace", Order = 1, Kind = "code", Repository = "edv-solvace" },
                         new ExecutionStepResponse { Key = "pr-edv-solvace-apps", Order = 2, Kind = "pr" }]
            });
        Assert.Equal(["edv-solvace-apps", "edv-solvace"], await withPlan.CardRepositoriesAsync("77434", CancellationToken.None));

        var none = Tools(prs: []);
        var catalog = await none.CatalogAsync("77434", CancellationToken.None);
        Assert.Equal("edv-solvace", (string?)catalog["repository"]); // DefaultRepository
    }

    // ── Stubs ────────────────────────────────────────────────────────────────────────────────────

    private static PullRequestGithubResponse Pr(string repo, int minutes) =>
        new() { RepositoryId = repo, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(minutes), Status = "OPEN", Number = minutes };

    private static WorkItemToolsService Tools(List<PullRequestGithubResponse> prs, List<ExecutionPlanSummaryResponse>? plans = null,
        ExecutionPlanResponse? plan = null)
    {
        var oldTemplates = """
        [{"key": "pr-hotfix-version", "title": "PR hotfix", "type": "pull-request", "when": {"repoKinds": ["legacy", "revamp-backend"]},
          "tasks": [{"title": "Criar {branch}{suffix:hotfix-version}", "kind": "branch", "params": {"repository": "{repository}"}}]}]
        """;
        var templates = JsonNode.Parse(oldTemplates)!.AsArray();
        foreach (var t in JsonNode.Parse(SeedSpecialistTools0085c.NewTemplates)!.AsArray())
            templates.Add(t!.DeepClone());
        var options = new Dictionary<string, string>
        {
            [SkillsConfigurationKeys.BranchStrategy] = Strategy,
            [SkillsConfigurationKeys.PromotionProfiles] = Profiles,
            [SkillsConfigurationKeys.BranchFlowByArea] = """[{"areaContains": "Product Development Team", "flow": "producao"}]""",
            [SkillsConfigurationKeys.DefaultRepository] = "edv-solvace",
            [SkillsConfigurationKeys.WorkItemTemplates] = templates.ToJsonString(),
            [SkillsConfigurationKeys.WorkItemTaskKinds] = """[{"kind": "branch", "executor": "claude", "params": []}, {"kind": "manual", "executor": "user"}]"""
        };
        var resolver = Stub<IPluginConfigurationResolver>((m, _) => m.Name == nameof(IPluginConfigurationResolver.GetEffectiveConfigurationAsync)
            ? Task.FromResult(new PluginConfiguration([options])) : null);
        var azure = Stub<IAzureService>((m, _) => m.Name == nameof(IAzureService.GetCardAsync)
            ? Task.FromResult<AzureWorkItem?>(new AzureWorkItem { Error = null, Fields = new WorkItemFields { WorkItemType = "Bug", AreaPath = @"Solvace\Product Development Team", State = "Active" } })
            : null);
        var rules = new SkillsRulesService(resolver, azure, new MemoryCache(new MemoryCacheOptions()), NullLogger<SkillsRulesService>.Instance);
        var github = Stub<IPullRequestGithubApplication>((m, _) => m.Name == nameof(IPullRequestGithubApplication.ListByCard)
            ? Task.FromResult<IReadOnlyList<PullRequestGithubResponse>>(prs) : null);
        var planApp = Stub<IExecutionPlanApplication>((m, _) => m.Name switch
        {
            nameof(IExecutionPlanApplication.GetByCardAsync) => Task.FromResult(plans ?? []),
            nameof(IExecutionPlanApplication.GetAsync) when plan is not null => Task.FromResult(plan),
            _ => null
        });
        return new WorkItemToolsService(rules, github, planApp, NullLogger<WorkItemToolsService>.Instance);
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class StubProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args) ?? throw new NotSupportedException($"stub: {targetMethod!.Name}");
    }
}
