using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0093: reproduzir o bug no ambiente — por padrão o relatado pelo cliente, outro ambiente opcional.</summary>
public class EnvReproduction0093Tests
{
    private static WorkItemTemplateContext Ctx(string? type = "bug") => new()
    {
        Card = "77698",
        Type = type,
        MainBranch = "hotfix/77698",
        Environments = ValidationEnvironments.Parse(null),
        EnvironmentDefaults = ValidationEnvironments.Defaults(null)
    };

    private static JsonObject Template() => JsonNode.Parse(SeedEnvReproduction0093.NewTemplate)!.AsObject();

    [Fact]
    public void Seeded_kind_type_and_template_are_known_and_use_known_markers()
    {
        var kind = JsonNode.Parse(SeedEnvReproduction0093.NewKind)!.AsObject();
        Assert.Equal(WorkTaskKind.EnvReproduction, (string?)kind["kind"]);
        Assert.Contains(WorkTaskKind.EnvReproduction, WorkTaskKind.Known);
        Assert.Equal("claude", (string?)kind["executor"]);
        Assert.Equal(WorkItemType.Reproduction, (string?)JsonNode.Parse(SeedEnvReproduction0093.NewItemType)!["key"]);
        Assert.Contains(WorkItemType.Reproduction, WorkItemType.Defaults);

        var template = Template();
        Assert.Equal(solvace.prform.Execution.WorkItemMcpTools.EnvReproductionTemplate, (string?)template["key"]);
        Assert.Equal(["bug"], template["when"]!["type"]!.AsArray().Select(x => (string?)x));
        foreach (var text in Strings(template))
            Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"marcador desconhecido em \"{text}\"");
    }

    [Fact]
    public void Only_bugs_can_be_reproduced()
    {
        Assert.True(WorkItemTemplateRenderer.Evaluate(Template(), Ctx()).Applicable);
        Assert.False(WorkItemTemplateRenderer.Evaluate(Template(), Ctx("user-story")).Applicable);
    }

    [Fact]
    public void Catalog_offers_the_client_environment_first_and_as_default()
    {
        var ask = WorkItemTemplateRenderer.ResolveAsks(Template(), Ctx()).OfType<JsonObject>().Single(a => (string?)a["key"] == "environments");
        var options = ask["options"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal(ValidationEnvironments.ClientKey, (string?)options[0]["value"]);
        Assert.Equal(ValidationEnvironments.ClientLabel, (string?)options[0]["label"]);
        Assert.Equal(9, options.Count);
        Assert.Equal("cliente", (string?)ask["default"]);

        var validation = WorkItemTemplateRenderer.ResolveAsks(JsonNode.Parse(SeedEnvValidation0092.NewTemplate)!.AsObject(), Ctx())
            .OfType<JsonObject>().Single(a => (string?)a["key"] == "environments");
        Assert.DoesNotContain(validation["options"]!.AsArray().OfType<JsonObject>(), o => (string?)o["value"] == ValidationEnvironments.ClientKey);
    }

    [Fact]
    public void Default_reproduces_in_the_client_environment()
    {
        var rendered = WorkItemTemplateRenderer.Render(Template(), Ctx(), null);
        var task = Assert.Single(rendered.Tasks);
        Assert.Equal("Reproduzir o bug — Ambiente do cliente (relatado no card)", task.Title);
        Assert.Equal(WorkTaskKind.EnvReproduction, task.Kind);
        Assert.Equal("cliente", task.Params["environment"]);
        Assert.Equal(WorkItemType.Reproduction, rendered.Type);
    }

    [Fact]
    public void Another_environment_is_optional_and_can_be_added()
    {
        var rendered = WorkItemTemplateRenderer.Render(Template(), Ctx(), new Dictionary<string, string>
        {
            ["environments"] = "cliente, qa",
            ["plant"] = "COR",
            ["steps"] = "1. Abrir o RCA 2. Escolher a data"
        });
        Assert.Equal(["Reproduzir o bug — Ambiente do cliente (relatado no card)", "Reproduzir o bug — QA"], rendered.Tasks.Select(t => t.Title));
        Assert.Equal("qa", rendered.Tasks[1].Params["environment"]);
        Assert.Equal("1. Abrir o RCA 2. Escolher a data", rendered.Tasks[1].Params["steps"]);
        Assert.Equal("COR", rendered.Tasks[0].Params["plant"]);

        var onlyQa = WorkItemTemplateRenderer.Render(Template(), Ctx(), new Dictionary<string, string> { ["environments"] = "https://sandboxmondelez.solvacelabs.com/" });
        Assert.Equal("https://sandboxmondelez.solvacelabs.com/", Assert.Single(onlyQa.Tasks).Params["environment"]);
    }

    [Fact]
    public void Environment_param_of_the_reproduction_kind_offers_the_client()
    {
        var kinds = new JsonArray(JsonNode.Parse(SeedEnvReproduction0093.NewKind), JsonNode.Parse(SeedEnvValidation0092.NewKind));
        WorkItemParamInputs.Apply(kinds, null, ValidationEnvironments.Parse(null));
        JsonArray Options(int i) => kinds[i]!["params"]!.AsArray().OfType<JsonObject>().Single(p => (string?)p["key"] == "environment")["options"]!.AsArray();
        Assert.Equal(ValidationEnvironments.ClientKey, (string?)Options(0)[0]!["value"]);
        Assert.Equal("dev", (string?)Options(1)[0]!["value"]);
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };
}
