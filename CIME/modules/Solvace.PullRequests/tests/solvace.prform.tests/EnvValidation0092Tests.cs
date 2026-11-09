using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0092: validação em ambiente — ambientes configurados, modelo com uma sub-tarefa por ambiente e o tipo de arquivo das evidências.</summary>
public class EnvValidation0092Tests
{
    private static WorkItemTemplateContext Ctx(string? type = "bug", string? environmentsJson = null) => new()
    {
        Card = "77434",
        Type = type,
        MainBranch = "hotfix/77434",
        Environments = ValidationEnvironments.Parse(environmentsJson),
        EnvironmentDefaults = ValidationEnvironments.Defaults(environmentsJson)
    };

    private static JsonObject Template() => JsonNode.Parse(SeedEnvValidation0092.NewTemplate)!.AsObject();

    [Fact]
    public void Seeded_kind_type_and_template_are_known_and_use_known_markers()
    {
        var kind = JsonNode.Parse(SeedEnvValidation0092.NewKind)!.AsObject();
        Assert.Equal(WorkTaskKind.EnvValidation, (string?)kind["kind"]);
        Assert.Contains(WorkTaskKind.EnvValidation, WorkTaskKind.Known);
        Assert.Equal("claude", (string?)kind["executor"]);

        var type = JsonNode.Parse(SeedEnvValidation0092.NewItemType)!.AsObject();
        Assert.Equal(WorkItemType.Validation, (string?)type["key"]);
        Assert.Contains(WorkItemType.Validation, WorkItemType.Defaults);

        var template = Template();
        Assert.Equal(WorkItemMcpToolsKey, (string?)template["key"]);
        Assert.Equal(WorkItemType.Validation, (string?)template["type"]);
        foreach (var text in Strings(template))
            Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"marcador desconhecido em \"{text}\"");

        Assert.Equal(ValidationEnvironments.DefaultJson, SeedEnvValidation0092.Environments);
        Assert.NotNull(JsonNode.Parse(SeedEnvValidation0092.Environments));
    }

    private const string WorkItemMcpToolsKey = solvace.prform.Execution.WorkItemMcpTools.EnvValidationTemplate;

    [Fact]
    public void Default_environments_include_the_known_ones_and_demo_and_production_are_read_only()
    {
        var envs = ValidationEnvironments.Parse(null);
        Assert.Equal(["dev", "qa", "performancetest", "sandboxefeso", "sandboxrt", "demo", "gerdau", "mondelez"], envs.Select(e => e.Key));
        Assert.True(envs.Single(e => e.Key == "demo").ReadOnly);
        Assert.True(envs.Single(e => e.Key == "mondelez").ReadOnly);
        Assert.False(envs.Single(e => e.Key == "qa").ReadOnly);
        Assert.Equal("admin@ewcm.com.br", envs.Single(e => e.Key == "dev").User);
        Assert.Equal("Demonstração · demo.solvacelabs.com · somente leitura", ValidationEnvironments.OptionLabel(envs.Single(e => e.Key == "demo")));
    }

    [Fact]
    public void Production_follows_productionReadOnly_unless_the_environment_says_otherwise()
    {
        const string json = """
            { "environments": [
                { "key": "x", "label": "X", "url": "https://x.solvacelabs.com/", "kind": "production" },
                { "key": "y", "label": "Y", "url": "https://y.solvacelabs.com/", "kind": "production", "readOnly": false },
                { "key": "", "url": "https://sem-chave/" } ],
              "productionReadOnly": true }
            """;
        var envs = ValidationEnvironments.Parse(json);
        Assert.Equal(["x", "y"], envs.Select(e => e.Key));
        Assert.True(envs[0].ReadOnly);
        Assert.False(envs[1].ReadOnly);
        Assert.Equal("Produção Gerdau", ValidationEnvironments.Parse("não é json").Single(e => e.Key == "gerdau").Label);
    }

    [Fact]
    public void Catalog_resolves_environment_options_and_the_default_by_card_type()
    {
        var template = Template();
        var asks = WorkItemTemplateRenderer.ResolveAsks(template, Ctx());
        var envAsk = asks.OfType<JsonObject>().Single(a => (string?)a["key"] == "environments");
        var options = envAsk["options"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal("dev", (string?)options[0]["value"]);
        Assert.Equal("Desenvolvimento · devsp.solvacelabs.com", (string?)options[0]["label"]);
        Assert.Equal("dev, qa", (string?)envAsk["default"]);

        var custom = """{ "environments": [{ "key": "qa", "label": "QA", "url": "https://qa.solvacelabs.com/" }], "defaults": { "user-story": "qa" } }""";
        var usAsk = WorkItemTemplateRenderer.ResolveAsks(Template(), Ctx("user-story", custom)).OfType<JsonObject>().Single(a => (string?)a["key"] == "environments");
        Assert.Equal("qa", (string?)usAsk["default"]);

        WorkItemTemplateRenderer.RenderForCatalog(template, Ctx());
        Assert.Equal("{ask:environments}", (string?)template["tasks"]![0]!["repeat"]);
    }

    [Fact]
    public void Render_creates_one_task_per_environment_with_label_and_params()
    {
        var rendered = WorkItemTemplateRenderer.Render(Template(), Ctx(), new Dictionary<string, string>
        {
            ["environments"] = "qa, mondelez; https://sandboxnovelis.solvacelabs.com/, qa",
            ["plant"] = "COR",
            ["scenario"] = "Salvar o 5W2H com anexo"
        });

        Assert.Equal("Validar em ambiente (qa, mondelez; https://sandboxnovelis.solvacelabs.com/, qa)", rendered.Title);
        Assert.Equal(WorkItemType.Validation, rendered.Type);
        Assert.Equal(["Validar em QA", "Validar em Produção Mondelez", "Validar em https://sandboxnovelis.solvacelabs.com/"], rendered.Tasks.Select(t => t.Title));
        Assert.All(rendered.Tasks, t => Assert.Equal(WorkTaskKind.EnvValidation, t.Kind));
        Assert.Equal("mondelez", rendered.Tasks[1].Params["environment"]);
        Assert.Equal("COR", rendered.Tasks[1].Params["plant"]);
        Assert.Equal(string.Empty, rendered.Tasks[1].Params["user"]);
        Assert.Equal("Salvar o 5W2H com anexo", rendered.Tasks[0].Params["scenario"]);
    }

    [Fact]
    public void Render_uses_the_suggested_environments_when_none_is_given()
    {
        var rendered = WorkItemTemplateRenderer.Render(Template(), Ctx(), null);
        Assert.Equal(["dev", "qa"], rendered.Tasks.Select(t => t.Params["environment"]));
    }

    [Fact]
    public void Template_without_repeat_renders_as_before()
    {
        var template = JsonNode.Parse("""
            { "key": "x", "title": "X {card}", "tasks": [{ "title": "Fazer {each}", "kind": "manual" }] }
            """)!.AsObject();
        var rendered = WorkItemTemplateRenderer.Render(template, Ctx(), null);
        Assert.Equal("Fazer {each}", Assert.Single(rendered.Tasks).Title);
    }

    [Fact]
    public void Environment_param_of_the_kind_becomes_a_select_with_the_environments()
    {
        var kinds = new JsonArray(JsonNode.Parse(SeedEnvValidation0092.NewKind));
        WorkItemParamInputs.Apply(kinds, null, ValidationEnvironments.Parse(null));
        var param = kinds[0]!["params"]!.AsArray().OfType<JsonObject>().Single(p => (string?)p["key"] == "environment");
        Assert.Equal("select", (string?)param["input"]);
        Assert.Equal(8, param["options"]!.AsArray().Count);
    }

    [Fact]
    public void Evidence_is_a_work_item_file_kind()
    {
        Assert.Equal(WorkItemFileKind.Evidence, WorkItemFileKind.Normalize("evidence", "01-login.jpg"));
        Assert.Equal(WorkItemFileKind.Attachment, WorkItemFileKind.Normalize(null, "01-login.jpg"));
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };
}
