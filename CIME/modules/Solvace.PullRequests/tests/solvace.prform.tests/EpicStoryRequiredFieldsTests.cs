using System.Text.Json;
using solvace.azure.application.Services;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// História #1109 do épico E1093: o DevOps recusou a User Story com o TF401320 porque o campo <c>Custom.Module</c> é obrigatório e
/// não tem padrão. Agora o PRMake confere os obrigatórios antes de criar o card e diz quais faltam. A conversão aceita os campos
/// preenchidos na hora, por cima dos da história.
/// </summary>
public class EpicStoryRequiredFieldsTests
{
    private static DevOpsFieldInfo Field(string reference, string name, bool required, string? defaultValue = null, bool readOnly = false,
        params string[] allowed) =>
        new(reference, name, allowed.Length > 0 ? "picklistString" : "string", required, readOnly, allowed, defaultValue, null);

    /// <summary>Recorte dos campos reais da User Story do projeto (GET DevOps/meta/fields?type=User Story).</summary>
    private static readonly DevOpsFieldInfo[] UserStory =
    [
        Field("System.Title", "Title", true),
        Field("System.AreaId", "Area ID", true),
        Field("System.State", "State", true, "New"),
        Field("Custom.Module", "Module", true, null, false, "Checklist", "Login"),
        Field("Custom.APPROVED", "APPROVED", true, "0"),
        Field("Microsoft.VSTS.CMMI.RequirementType", "Requirement Type", true, "Business", false, "Architetural", "Business", "RCA"),
        Field("Custom.APIImpactValidation", "API Impact Validation", true, "Not validated"),
        Field("System.Parent", "Parent", false, null, true),
        Field("Microsoft.VSTS.Common.Priority", "Priority", false, "2"),
    ];

    private static Dictionary<string, JsonElement> Fields(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Only_required_without_default_and_outside_system_are_missing()
    {
        var missing = DevOpsDeskMapping.MissingRequired(UserStory, Fields(("System.AreaPath", @"Solvace\Refinement - RCA")));
        Assert.Equal(["Custom.Module"], missing.Select(f => f.ReferenceName));
        Assert.Equal(["Checklist", "Login"], missing[0].AllowedValues);
    }

    [Fact]
    public void Filled_field_is_not_missing_and_blank_text_still_is()
    {
        Assert.Empty(DevOpsDeskMapping.MissingRequired(UserStory, Fields(("custom.module", "Login"))));
        Assert.Single(DevOpsDeskMapping.MissingRequired(UserStory, Fields(("Custom.Module", "  "))));
        Assert.Single(DevOpsDeskMapping.MissingRequired(UserStory, Fields(("Custom.Module", null))));
    }

    [Fact]
    public void Exception_names_the_fields_and_is_422()
    {
        var e = new DevOpsRequiredFieldsException("User Story", DevOpsDeskMapping.MissingRequired(UserStory, null));
        Assert.Equal(422, e.StatusCode);
        Assert.Equal("required_fields", e.Code);
        Assert.Contains("Module", e.Message);
        Assert.Single(e.Fields);
    }

    [Fact]
    public void Conversion_fields_override_the_story_fields()
    {
        var story = new EpicStoryResponse
        {
            Number = 1109, EpicNumber = 1093, EpicKey = "E1093", Type = "user-story", Title = "[RCA] - Permitir ao usuario SSO trocar a planta",
            Fields = new Dictionary<string, JsonElement> { ["System.AreaPath"] = JsonSerializer.SerializeToElement(@"Solvace\Refinement - RCA") }
        };
        var request = new ConvertEpicStoryRequest { Fields = Fields(("Custom.Module", "Login"), ("Microsoft.VSTS.CMMI.RequirementType", "RCA")) };

        var create = DeskStoryFieldMap.BuildRequest(story, DeskStoryFieldMap.For(null, "user-story"), request, null, null);

        Assert.Equal("Login", create.Fields!["Custom.Module"].GetString());
        Assert.Equal("RCA", create.Fields["Microsoft.VSTS.CMMI.RequirementType"].GetString());
        Assert.Equal(@"Solvace\Refinement - RCA", create.Fields["System.AreaPath"].GetString());
        Assert.Empty(DevOpsDeskMapping.MissingRequired(UserStory, create.Fields));
    }
}
