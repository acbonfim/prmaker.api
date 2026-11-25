using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.prform.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>Card 78323: fechamento do RCA da User Story — ações do DevOps na US e a ferramenta fechamento-rca-us.</summary>
public class RcaUsClosingTests
{
    private const string RootCauseField = "Custom.RCATechnicalCategorytext";

    private sealed class Azure
    {
        public string Type = "User Story";
        public bool UserStoryHasRootCause;
        public decimal? Remaining = 2m;
        public bool Classified = true;
        public IReadOnlyDictionary<string, object>? Patched;

        public IAzureService Service => Stub<IAzureService>((m, args) => m.Name switch
        {
            nameof(IAzureService.GetCardFullAsync) => Task.FromResult<AzureCardFullResponse?>(Card()),
            nameof(IAzureService.GetFieldNamesAsync) => Task.FromResult(new AzureDevOpsFieldNames(
                "Custom.ResolutionType", "Custom.GeneralClassification", "Custom.Classification",
                "Microsoft.VSTS.Scheduling.RemainingWork", "Microsoft.VSTS.Scheduling.OriginalEstimate",
                "Microsoft.VSTS.Scheduling.CompletedWork", RootCauseField, "Custom.BugType")),
            nameof(IAzureService.GetFieldAllowedValuesAsync) => Task.FromResult<IReadOnlyList<string>?>(
                (string)args![0]! == "User Story" && (string)args[1]! == RootCauseField && !UserStoryHasRootCause ? null : []),
            nameof(IAzureService.PatchFieldsAsync) => Patch((IReadOnlyDictionary<string, object>)args![1]!),
            _ => null
        });

        private AzureCardFullResponse Card()
        {
            var fields = new Dictionary<string, JsonElement>
            {
                ["System.WorkItemType"] = JsonSerializer.SerializeToElement(Type),
                ["System.State"] = JsonSerializer.SerializeToElement("In Development (doing)")
            };
            if (Remaining is { } r) fields["Microsoft.VSTS.Scheduling.RemainingWork"] = JsonSerializer.SerializeToElement(r);
            return new AzureCardFullResponse
            {
                Id = 78323,
                Fields = fields,
                Alerts = new AzureCardAlerts
                {
                    MissingRootCause = true,
                    MissingResolutionType = !Classified,
                    MissingGeneralClassification = !Classified,
                    MissingClassification = !Classified,
                    RemainingNotZero = Remaining is > 0
                }
            };
        }

        private Task<long> Patch(IReadOnlyDictionary<string, object> fields)
        {
            Patched = fields;
            return Task.FromResult(9L);
        }
    }

    private static DevOpsActionsService Service(Azure azure, Dictionary<string, string>? ai = null) => new(azure.Service,
        Stub<IPluginConfigurationResolver>((m, _) => m.Name == nameof(IPluginConfigurationResolver.GetEffectiveConfigurationAsync)
            ? Task.FromResult(new PluginConfiguration([ai ?? new Dictionary<string, string> { [AIConfigurationKeys.BugReadyForQaState] = "In Development (done)" }]))
            : null),
        Stub<IPullRequestApplication>((_, _) => null));

    [Fact]
    public async Task User_story_is_classified_without_bug_type()
    {
        var azure = new Azure();
        var result = await Service(azure).ClassifyAsync("78323", new ClassifyCardRequest { Preset = "code-fix" });

        Assert.Equal("Code Fix", azure.Patched!["Custom.ResolutionType"]);
        Assert.Equal(3, azure.Patched.Count);
        Assert.Contains("Code Fix", result.Message);
    }

    [Fact]
    public async Task Bug_type_on_a_user_story_is_refused()
    {
        var azure = new Azure();
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() =>
            Service(azure).ClassifyAsync("78323", new ClassifyCardRequest { Preset = "code-fix", BugType = "Report" }));

        Assert.Equal(400, e.StatusCode);
        Assert.Null(azure.Patched);
    }

    [Fact]
    public async Task User_story_remaining_is_zeroed()
    {
        var azure = new Azure();
        await Service(azure).ZeroRemainingAsync("78323");

        Assert.Equal(0, azure.Patched!["Microsoft.VSTS.Scheduling.RemainingWork"]);
    }

    [Fact]
    public async Task Ready_for_qa_moves_the_user_story_to_the_default_state_without_root_cause_field()
    {
        var azure = new Azure { Remaining = 0m };
        var result = await Service(azure).MoveToReadyForQaAsync("78323");

        Assert.Equal(DevOpsActionsService.DefaultUserStoryReadyForQaState, azure.Patched!["System.State"]);
        Assert.Contains(DevOpsActionsService.DefaultUserStoryReadyForQaState, result.Message);
    }

    [Fact]
    public async Task Ready_for_qa_of_the_user_story_uses_the_configured_state()
    {
        var azure = new Azure { Remaining = 0m };
        await Service(azure, new() { [AIConfigurationKeys.UserStoryReadyForQaState] = "Test QA" }).MoveToReadyForQaAsync("78323");

        Assert.Equal("Test QA", azure.Patched!["System.State"]);
    }

    [Fact]
    public async Task Ready_for_qa_of_the_user_story_needs_classification_and_remaining()
    {
        var azure = new Azure { Classified = false };
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() => Service(azure).MoveToReadyForQaAsync("78323"));

        Assert.Contains("Resolution Type", e.Message);
        Assert.Contains("Remaining", e.Message);
        Assert.DoesNotContain("Root Cause", e.Message);
        Assert.Null(azure.Patched);
    }

    [Fact]
    public async Task Root_cause_counts_as_pending_when_the_user_story_has_the_field()
    {
        var azure = new Azure { Remaining = 0m, UserStoryHasRootCause = true };
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() => Service(azure).MoveToReadyForQaAsync("78323"));

        Assert.Contains("Root Cause", e.Message);
    }

    [Fact]
    public async Task Root_cause_field_is_refused_on_a_user_story_without_it_and_allowed_on_bugs()
    {
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() => Service(new Azure()).EnsureRootCauseFieldAsync("78323"));
        Assert.Equal(409, e.StatusCode);
        Assert.Contains("discussion", e.Message);

        await Service(new Azure { UserStoryHasRootCause = true }).EnsureRootCauseFieldAsync("78323");
        await Service(new Azure { Type = "Bug" }).EnsureRootCauseFieldAsync("78262");
    }

    [Fact]
    public async Task Bug_only_actions_stay_refused_on_user_stories()
    {
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() => Service(new Azure()).MoveToDevTestInQaAsync("78323"));
        Assert.Equal(409, e.StatusCode);
    }

    // ── Ferramenta fechamento-rca-us ─────────────────────────────────────────────────────────────

    private static JsonObject Template() => JsonNode.Parse(SeedRcaUsClosing.NewTemplate)!.AsObject();

    private static WorkItemTemplateContext Ctx(string type) => new() { Card = "78323", Type = type, MainBranch = "feature/78323" };

    [Fact]
    public void Template_is_a_user_story_closing_with_known_markers_and_presets()
    {
        var template = Template();
        Assert.Equal("closing", (string?)template["type"]);
        Assert.True(WorkItemTemplateRenderer.Evaluate(template, Ctx("user-story")).Applicable);
        Assert.False(WorkItemTemplateRenderer.Evaluate(template, Ctx("bug")).Applicable);

        foreach (var text in Strings(template))
            Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"marcador desconhecido em \"{text}\"");

        var presets = DevOpsActionsService.DefaultClassificationPresets.Select(p => p.Key).ToHashSet();
        var options = template["ask"]!.AsArray().OfType<JsonObject>().Single(a => (string?)a["key"] == "classification")["options"]!
            .AsArray().Select(o => (string)o!).ToList();
        Assert.All(options, o => Assert.Contains(o, presets));

        var actions = template["tasks"]!.AsArray().OfType<JsonObject>()
            .Where(t => (string?)t["kind"] == "devops-action").Select(t => (string?)t["params"]!["action"]).ToList();
        Assert.Equal(["rootcause", "classify", "summary", "zero-remaining", "ready-for-qa"], actions);
        Assert.Equal("validation", (string?)template["tasks"]![0]!["kind"]);
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(kv => Strings(kv.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, WorkItemTemplateRendererTests.StubProxy>();
        ((WorkItemTemplateRendererTests.StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
