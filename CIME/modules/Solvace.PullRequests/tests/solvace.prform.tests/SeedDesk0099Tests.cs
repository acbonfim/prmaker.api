using System.Text.Json.Nodes;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (Mesa de gestão): tipos, kinds e modelos semeados — marcadores conhecidos, renderização completa num épico, tipos e
/// kinds existentes, params com os nomes da skill tarefas-de-gestao — e as regras de gestão para a chave E….
/// </summary>
public class SeedDesk0099Tests
{
    private static readonly string[] BaseKinds =
        ["branch", "cherry-pick", "code", "pr", "devops-state", "sql-script", "validation", "comment", "manual", "ticket", "notify", "task", "decision", "research"];

    private static JsonArray Arr(string json) => JsonNode.Parse(json)!.AsArray();

    private static WorkItemTemplateContext EpicCtx() => new() { Card = "E1234", Type = "epic" };

    [Fact]
    public void Item_types_kinds_and_templates_are_the_contract()
    {
        Assert.Equal(["report", "duplicates", "understanding", "rca", "devops", "planning"], Arr(SeedDesk0099.ItemTypes).Select(t => (string?)t!["key"]));
        Assert.Equal(["devops-query", "devops-create", "devops-update", "devops-move", "report", "duplicates", "understanding", "rca-doc", "pdf"],
            Arr(SeedDesk0099.NewKinds).Select(t => (string?)t!["kind"]));
        Assert.Equal(["relatorio-bugs", "itens-duplicados", "entendimento-card", "rca-pdf", "criar-historias"],
            Arr(SeedDesk0099.NewTemplates).Select(t => (string?)t!["key"]));
        Assert.All(Arr(SeedDesk0099.ItemTypes), t => Assert.True(((string)t!["key"]!).Length <= 20));
    }

    [Fact]
    public void Templates_render_completely_for_an_epic()
    {
        var types = Arr(SeedDesk0099.ItemTypes).Select(t => (string?)t!["key"]).ToHashSet();
        var kinds = Arr(SeedDesk0099.NewKinds).Select(t => (string?)t!["kind"]).Concat(BaseKinds).ToHashSet();
        var answers = new Dictionary<string, string>
        {
            ["title"] = "Bugs 5S", ["states"] = "Active", ["areaPath"] = @"Solvace\Revamp", ["createdFrom"] = "@today - 90", ["focus"] = "x",
            ["types"] = "Bug", ["windowDays"] = "120", ["min"] = "0.55", ["cards"] = "78123", ["card"] = "78123", ["language"] = "pt",
            ["type"] = "User Story", ["parentId"] = "", ["column"] = "Doing"
        };
        foreach (var t in Arr(SeedDesk0099.NewTemplates).OfType<JsonObject>())
        {
            var key = (string)t["key"]!;
            Assert.Contains((string?)t["type"], types);
            Assert.Contains("epic", t["when"]!["type"]!.AsArray().Select(x => (string?)x));
            var (applicable, reason) = WorkItemTemplateRenderer.Evaluate(t, EpicCtx());
            Assert.True(applicable, $"{key}: {reason}");
            var item = WorkItemTemplateRenderer.Render((JsonObject)t.DeepClone(), EpicCtx(), answers);
            Assert.DoesNotContain('{', item.Title);
            foreach (var task in item.Tasks)
            {
                Assert.Contains(task.Kind, kinds);
                Assert.DoesNotContain('{', task.Title);
                Assert.All(task.Params.Values, v => Assert.DoesNotContain('{', v));
            }
        }
    }

    [Fact]
    public void Epic_only_templates_do_not_apply_to_cards()
    {
        var report = Arr(SeedDesk0099.NewTemplates).OfType<JsonObject>().Single(t => (string?)t["key"] == "relatorio-bugs");
        var (applicable, reason) = WorkItemTemplateRenderer.Evaluate(report, new WorkItemTemplateContext { Card = "78123", Type = "bug" });
        Assert.False(applicable);
        Assert.Contains("Épico", reason);
    }

    [Fact]
    public void Card_templates_do_not_apply_to_epics()
    {
        var noWhen = new JsonObject { ["key"] = "mover-card", ["title"] = "Mover" };
        Assert.False(WorkItemTemplateRenderer.Evaluate(noWhen, EpicCtx()).Applicable);
        Assert.True(WorkItemTemplateRenderer.Evaluate(noWhen, new WorkItemTemplateContext { Card = "78123", Type = "bug" }).Applicable);
        var cardOnly = new JsonObject { ["key"] = "x", ["when"] = new JsonObject { ["type"] = new JsonArray("bug", "user-story") } };
        Assert.False(WorkItemTemplateRenderer.Evaluate(cardOnly, EpicCtx()).Applicable);
    }

    [Fact]
    public void Kind_params_use_the_names_the_skill_expects()
    {
        var byKind = Arr(SeedDesk0099.NewKinds).OfType<JsonObject>()
            .ToDictionary(k => (string)k["kind"]!, k => k["params"]!.AsArray().Select(p => (string?)p!["key"]).ToHashSet());
        Assert.Superset(new HashSet<string?> { "name", "wiql", "fields" }, byKind["devops-query"]);
        Assert.Superset(new HashSet<string?> { "source", "fields", "cards", "newTags", "allowDuplicate", "confirmed", "column", "lane" }, byKind["devops-create"]);
        Assert.Superset(new HashSet<string?> { "ids", "fields", "addTags", "confirmed", "authorization" }, byKind["devops-update"]);
        Assert.Superset(new HashSet<string?> { "ids", "column", "lane", "done", "confirmed", "authorization" }, byKind["devops-move"]);
        Assert.Superset(new HashSet<string?> { "source", "title" }, byKind["report"]);
        Assert.Superset(new HashSet<string?> { "source", "windowDays", "min" }, byKind["duplicates"]);
        Assert.Superset(new HashSet<string?> { "card", "cards" }, byKind["understanding"]);
        Assert.Superset(new HashSet<string?> { "card", "language" }, byKind["rca-doc"]);
        Assert.Superset(new HashSet<string?> { "file", "name", "title" }, byKind["pdf"]);
    }

    [Fact]
    public void Default_item_types_include_the_desk_ones()
    {
        var keys = WorkItemToolsService.ItemTypes(new Dictionary<string, string>()).Select(t => (string?)t!["key"]).ToList();
        Assert.Contains("report", keys);
        Assert.Contains("planning", keys);
        Assert.Contains("task", keys);
    }

    [Fact]
    public void Seed_sql_appends_by_key_in_the_three_lists()
    {
        var sql = SeedDesk0099.UpSql;
        Assert.Contains("'WorkItemTypes'", sql);
        Assert.Contains("'WorkItemTaskKinds'", sql);
        Assert.Contains("'WorkItemTemplates'", sql);
        Assert.Contains("y ->> 'kind' = e ->> 'kind'", sql);
        Assert.Contains("y ->> 'key' = e ->> 'key'", sql);
    }

    [Theory]
    [InlineData("E12", "E12")]
    [InlineData("e12", "E12")]
    [InlineData("#E12", "E12")]
    [InlineData("AB#78123", "78123")]
    public void Normalize_card_keeps_epic_keys(string given, string expected) =>
        Assert.Equal(expected, SkillsRulesService.NormalizeCard(given));

    [Fact]
    public void Epic_rules_are_management_rules()
    {
        var r = new CardRules("E12", SkillsRulesService.EpicWorkItemType, false, null, null, null, null, string.Empty, string.Empty,
            new Dictionary<string, string>(), [], null, null, string.Empty, string.Empty, string.Empty, new Dictionary<string, string>(), null);
        var md = SkillsRulesService.Markdown(r);
        Assert.Contains("épico E12", md);
        Assert.Contains("Lote com mais de 10 cards", md);
        Assert.Contains("procure duplicado", md);
        Assert.DoesNotContain("Branch principal", md);
        Assert.Contains("épico", SkillsRulesService.SummaryMarkdown(r));
        Assert.Equal("epic", WorkItemToolsService.TypeKey(r));
    }
}
