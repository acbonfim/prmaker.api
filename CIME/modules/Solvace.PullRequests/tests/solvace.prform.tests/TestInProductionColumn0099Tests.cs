using System.Reflection;
using System.Text.Json;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.azure.domain.Options;
using solvace.prform.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0099: "Test in production" muda estado E coluna do board (estado Test QA na coluna QA Done).</summary>
public class TestInProductionColumn0099Tests
{
    private const string ColumnField = "WEF_ABC_Kanban.Column";

    private sealed class Azure
    {
        public bool OnBoard = true;
        public IReadOnlyDictionary<string, object>? Patched;

        public IAzureService Service => Stub<IAzureService>((m, args) => m.Name switch
        {
            nameof(IAzureService.GetCardFullAsync) => Task.FromResult<AzureCardFullResponse?>(Card()),
            nameof(IAzureService.PatchFieldsAsync) => Patch((IReadOnlyDictionary<string, object>)args![1]!),
            _ => null
        });

        private AzureCardFullResponse Card()
        {
            var fields = new Dictionary<string, JsonElement>
            {
                ["System.WorkItemType"] = JsonSerializer.SerializeToElement("Bug"),
                ["System.State"] = JsonSerializer.SerializeToElement("In Development (done)"),
                ["System.AreaPath"] = JsonSerializer.SerializeToElement("Solvace\\Sustain"),
            };
            if (OnBoard)
            {
                fields[ColumnField] = JsonSerializer.SerializeToElement("Ready for QA");
                fields["WEF_ABC_System.ExtensionMarker"] = JsonSerializer.SerializeToElement(true);
                fields["WEF_OLD_Kanban.Column"] = JsonSerializer.SerializeToElement("Ready for QA");
            }
            return new AzureCardFullResponse { Id = 78793, Fields = fields, Alerts = new AzureCardAlerts() };
        }

        private Task<long> Patch(IReadOnlyDictionary<string, object> fields)
        {
            Patched = fields;
            return Task.FromResult(5L);
        }
    }

    private static DevOpsActionsService Service(Azure azure, string? column) => new(azure.Service,
        Stub<IPluginConfigurationResolver>((m, _) => m.Name == nameof(IPluginConfigurationResolver.GetEffectiveConfigurationAsync)
            ? Task.FromResult(new PluginConfiguration([new Dictionary<string, string>
            {
                [AIConfigurationKeys.BugTestInProductionArea] = "Solvace\\Product Improvement",
                [AIConfigurationKeys.BugTestInProductionState] = "Test QA",
                [AIConfigurationKeys.BugTestInProductionColumn] = column ?? "",
            }]))
            : null),
        Stub<IPullRequestApplication>((_, _) => null));

    [Fact]
    public async Task Moves_state_and_the_board_column_of_the_card_team()
    {
        var azure = new Azure();
        var result = await Service(azure, "QA Done").MoveToTestInProductionAsync("78793");

        Assert.Equal("Test QA", azure.Patched!["System.State"]);
        Assert.Equal("QA Done", azure.Patched[ColumnField]);
        Assert.False(azure.Patched.ContainsKey("WEF_OLD_Kanban.Column"));
        Assert.Contains("coluna QA Done", result.Message);
    }

    [Fact]
    public async Task Without_column_only_the_state_changes()
    {
        var azure = new Azure();
        var result = await Service(azure, null).MoveToTestInProductionAsync("78793");

        Assert.Equal("Test QA", azure.Patched!["System.State"]);
        Assert.DoesNotContain(azure.Patched.Keys, k => k.EndsWith("_Kanban.Column"));
        Assert.DoesNotContain("coluna", result.Message);
    }

    [Fact]
    public async Task Column_configured_but_card_off_any_board_is_refused()
    {
        var azure = new Azure { OnBoard = false };
        var e = await Assert.ThrowsAsync<DevOpsActionException>(() => Service(azure, "QA Done").MoveToTestInProductionAsync("78793"));

        Assert.Equal(409, e.StatusCode);
        Assert.Null(azure.Patched);
    }

    [Fact]
    public async Task Config_exposes_the_column()
    {
        var config = await Service(new Azure(), "QA Done").GetConfigAsync();
        Assert.Equal("QA Done", config.Bug.TestInProduction.Column);
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, WorkItemTemplateRendererTests.StubProxy>();
        ((WorkItemTemplateRendererTests.StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
