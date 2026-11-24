using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;
using solvace.prform.infra.Migrations;
using solvace.prform.PluginSettings;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (ajustes 2): história/bug do épico → card do DevOps — mapeamento <c>DeskStoryFieldMap</c> (padrão = seed, configuração do
/// admin, tipo ausente, texto sem campo vira seção da descrição), markdown → HTML, prioridade/severidade, campos extras, épico e
/// board; seed idempotente e catálogo.
/// </summary>
public class EpicStoryConversion0099Tests
{
    private static EpicStoryResponse Story(string type, Action<EpicStoryResponse>? change = null)
    {
        var s = new EpicStoryResponse
        {
            Number = 77, EpicNumber = 5, EpicKey = "E5", Type = type, Title = "Exportar relatório 5S",
            Description = "Como **gestor** quero exportar.", AcceptanceCriteria = "- [ ] exporta em PDF\n- [x] mantém filtros",
            ReproSteps = "1. Abrir a tela\n2. Clicar em exportar", Priority = 2, Severity = "2 - High", Tags = ["5S", "Mesa"],
            Fields = new Dictionary<string, JsonElement> { ["System.AreaPath"] = JsonSerializer.SerializeToElement(@"Solvace\Revamp") }
        };
        change?.Invoke(s);
        return s;
    }

    [Fact]
    public void Default_map_is_the_seed()
    {
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SeedDeskStoryFieldMap0099.FieldMap), JsonNode.Parse(DeskStoryFieldMap.DefaultJson)));
        var us = DeskStoryFieldMap.For(null, "user-story");
        Assert.Equal(new DeskStoryTypeMap("User Story", "System.Description", "Microsoft.VSTS.Common.AcceptanceCriteria", null,
            "Microsoft.VSTS.Common.Priority", null), us);
        var bug = DeskStoryFieldMap.For("", "bug");
        Assert.Equal(new DeskStoryTypeMap("Bug", "System.Description", null, "Microsoft.VSTS.TCM.ReproSteps",
            "Microsoft.VSTS.Common.Priority", "Microsoft.VSTS.Common.Severity"), bug);
    }

    [Fact]
    public void Invalid_or_partial_config_falls_back_per_type()
    {
        Assert.Equal("User Story", DeskStoryFieldMap.For("{não é json", "user-story").Type);
        // Só o bug configurado: a US usa o padrão; o bug usa o do admin.
        var json = """{"Bug": {"type": "Defect", "reproSteps": "Custom.Steps", "priority": ""}}""";
        Assert.Equal("User Story", DeskStoryFieldMap.For(json, "user-story").Type);
        var bug = DeskStoryFieldMap.For(json, "bug");
        Assert.Equal("Defect", bug.Type);
        Assert.Equal("System.Description", bug.Description);
        Assert.Equal("Custom.Steps", bug.ReproSteps);
        Assert.Null(bug.Priority); // "" desliga
    }

    [Fact]
    public void User_story_maps_text_to_html_and_extra_fields()
    {
        var req = DeskStoryFieldMap.BuildRequest(Story("user-story", s => s.ReproSteps = null), DeskStoryFieldMap.For(null, "user-story"),
            new ConvertEpicStoryRequest { Column = "Doing", Lane = "Expedite" }, "Time Revamp", "Stories");
        Assert.Equal("User Story", req.Type);
        Assert.Equal("Exportar relatório 5S", req.Title);
        Assert.Equal(5, req.Epic);
        Assert.Equal(["5S", "Mesa"], req.Tags);
        Assert.Equal("Time Revamp", req.Team);
        Assert.Equal("Stories", req.Board);
        Assert.Equal("Doing", req.Column);
        Assert.Equal("Expedite", req.Lane);
        var f = req.Fields!;
        Assert.Contains("<strong>gestor</strong>", f["System.Description"].GetString());
        var ac = f["Microsoft.VSTS.Common.AcceptanceCriteria"].GetString()!;
        Assert.Contains("☐", ac);
        Assert.Contains("☑", ac);
        Assert.DoesNotContain("<input", ac);
        Assert.Equal(2, f["Microsoft.VSTS.Common.Priority"].GetInt32());
        Assert.False(f.ContainsKey("Microsoft.VSTS.Common.Severity")); // US não tem severidade por padrão
        Assert.Equal(@"Solvace\Revamp", f["System.AreaPath"].GetString());
    }

    [Fact]
    public void Bug_maps_repro_steps_severity_and_folds_unmapped_text_into_the_description()
    {
        var req = DeskStoryFieldMap.BuildRequest(Story("bug"), DeskStoryFieldMap.For(null, "bug"), null, null, null);
        Assert.Equal("Bug", req.Type);
        var f = req.Fields!;
        Assert.Contains("<ol>", f["Microsoft.VSTS.TCM.ReproSteps"].GetString());
        Assert.Equal("2 - High", f["Microsoft.VSTS.Common.Severity"].GetString());
        var description = f["System.Description"].GetString()!;
        Assert.Contains("Critérios de aceite", description); // bug sem campo de critérios: vira seção da descrição
        Assert.Contains("exporta em PDF", description);
        Assert.Null(req.Team);
        Assert.Null(req.Board);
        Assert.Null(req.Column);
    }

    [Fact]
    public void Explicit_fields_win_over_priority_and_board_needs_the_epic_team()
    {
        var story = Story("user-story", s => s.Fields["Microsoft.VSTS.Common.Priority"] = JsonSerializer.SerializeToElement(1));
        var req = DeskStoryFieldMap.BuildRequest(story, DeskStoryFieldMap.For(null, "user-story"),
            new ConvertEpicStoryRequest { Team = "Outro time" }, "Time Revamp", "Stories");
        Assert.Equal(1, req.Fields!["Microsoft.VSTS.Common.Priority"].GetInt32());
        Assert.Equal("Outro time", req.Team);
        Assert.Null(req.Board); // board do épico é do time do épico
    }

    [Fact]
    public void Story_without_text_sends_only_title_and_type()
    {
        var story = new EpicStoryResponse { Number = 1, EpicNumber = 5, Type = EpicStoryType.UserStory, Title = "Só o título" };
        var req = DeskStoryFieldMap.BuildRequest(story, DeskStoryFieldMap.For(null, "user-story"), null, null, null);
        Assert.Null(req.Fields);
        Assert.Null(req.Tags);
        Assert.Equal("Só o título", req.Title);
    }

    [Fact]
    public void Seed_only_adds_when_missing_and_down_removes_only_the_untouched_value()
    {
        var up = SeedDeskStoryFieldMap0099.UpSql;
        Assert.Contains("'{0,DeskStoryFieldMap}'", up);
        Assert.Contains("IF cur IS NULL OR btrim(cur) = ''", up);
        Assert.Contains("Skills Configurations", up);
        Assert.DoesNotContain("{{", up);
    }

    [Fact]
    public void Key_is_cataloged_with_the_code_default()
    {
        var entry = PluginSettingsCatalog.Fields.Single(f => f.Key == SkillsConfigurationKeys.DeskStoryFieldMap);
        Assert.Equal(SettingTypes.Json, entry.Type);
        Assert.Equal("mesa", entry.Group);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(DeskStoryFieldMap.DefaultJson), JsonNode.Parse(entry.Default!)));
    }

    [Fact]
    public void Story_tools_act_as_the_requester()
    {
        Assert.Contains("prmake_epic_story_save", CaseSpecialistMiddleware.ActingTools);
        Assert.Contains("prmake_epic_story_convert", CaseSpecialistMiddleware.ActingTools);
        Assert.True(CaseSpecialistMiddleware.IsAllowed("prmake_epic_stories"));
    }
}
