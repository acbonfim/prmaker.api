using System.Reflection;
using System.Text.Json;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;
using solvace.azure.domain.Requests;
using solvace.prform.application.Contracts;
using solvace.prform.application.UserIntegrations;
using Xunit;

namespace solvace.prform.tests;

/// <summary>Bug Type (picklist do DevOps) na classificação do card.</summary>
public class DevOpsBugTypeTests
{
    private static readonly string[] BugTypes = ["User login - access", "Performance", "Report"];

    private sealed class Azure
    {
        public IReadOnlyDictionary<string, object>? Patched;
        public IReadOnlyList<string>? AllowedValues = BugTypes;

        public IAzureService Service => Stub<IAzureService>((m, args) => m.Name switch
        {
            nameof(IAzureService.GetCardFullAsync) => Task.FromResult<AzureCardFullResponse?>(new AzureCardFullResponse
            {
                Id = 78262,
                Fields = new Dictionary<string, JsonElement> { ["System.WorkItemType"] = JsonSerializer.SerializeToElement("Bug") }
            }),
            nameof(IAzureService.GetFieldNamesAsync) => Task.FromResult(new AzureDevOpsFieldNames(
                "Custom.ResolutionType", "Custom.GeneralClassification", "Custom.Classification",
                "Microsoft.VSTS.Scheduling.RemainingWork", "Microsoft.VSTS.Scheduling.OriginalEstimate",
                "Microsoft.VSTS.Scheduling.CompletedWork", "Custom.RCATechnicalCategorytext", "Custom.BugType")),
            nameof(IAzureService.GetFieldAllowedValuesAsync) => Task.FromResult(AllowedValues),
            nameof(IAzureService.PatchFieldsAsync) => Patch((IReadOnlyDictionary<string, object>)args![1]!),
            _ => null
        });

        private Task<long> Patch(IReadOnlyDictionary<string, object> fields)
        {
            Patched = fields;
            return Task.FromResult(7L);
        }
    }

    private static DevOpsActionsService Service(Azure azure) => new(azure.Service,
        // Sem o "AI Configurations": valem as classificações padrão do código.
        Stub<IPluginConfigurationResolver>((_, _) => throw new InvalidOperationException("sem plugin")),
        Stub<IPullRequestApplication>((_, _) => null));

    [Fact]
    public async Task Preset_with_bug_type_writes_the_four_fields_with_the_devops_spelling()
    {
        var azure = new Azure();
        var result = await Service(azure).ClassifyAsync("78262", new ClassifyCardRequest { Preset = "code-fix", BugType = "user login - ACCESS" });

        Assert.Equal("Code Fix", azure.Patched!["Custom.ResolutionType"]);
        Assert.Equal("User login - access", azure.Patched["Custom.BugType"]);
        Assert.Equal(4, azure.Patched.Count);
        Assert.Contains("Bug Type: User login - access", result.Message);
    }

    [Fact]
    public async Task Bug_type_alone_writes_only_bug_type()
    {
        var azure = new Azure();
        await Service(azure).ClassifyAsync("78262", new ClassifyCardRequest { BugType = "Report" });

        Assert.Equal(new[] { "Custom.BugType" }, azure.Patched!.Keys);
    }

    [Fact]
    public async Task Preset_without_bug_type_keeps_working()
    {
        var azure = new Azure();
        await Service(azure).ClassifyAsync("78262", new ClassifyCardRequest { Preset = "code-fix" });

        Assert.Equal(3, azure.Patched!.Count);
        Assert.False(azure.Patched.ContainsKey("Custom.BugType"));
    }

    [Fact]
    public async Task Bug_type_outside_the_picklist_is_refused_with_the_options()
    {
        var azure = new Azure();
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() =>
            Service(azure).ClassifyAsync("78262", new ClassifyCardRequest { Preset = "code-fix", BugType = "Login" }));

        Assert.Equal(400, e.StatusCode);
        Assert.Contains("User login - access", e.Message);
        Assert.Null(azure.Patched);
    }

    [Fact]
    public async Task Nothing_to_write_is_refused()
    {
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() =>
            Service(new Azure()).ClassifyAsync("78262", new ClassifyCardRequest { ResolutionType = "Code Fix" }));

        Assert.Equal(400, e.StatusCode);
    }

    [Fact]
    public async Task Bug_types_come_from_the_devops_picklist_and_empty_without_the_field()
    {
        Assert.Equal(BugTypes, await Service(new Azure()).GetBugTypesAsync());
        Assert.Empty(await Service(new Azure { AllowedValues = null }).GetBugTypesAsync());
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, WorkItemTemplateRendererTests.StubProxy>();
        ((WorkItemTemplateRendererTests.StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
