using System.Text.Json.Nodes;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0085 r5 (1.4/1.5): modelos semeados sem "correção" para User Story e script sempre com chamado.</summary>
public class SeedUsTermsTests
{
    private static IEnumerable<JsonObject> NewVersions() =>
        SeedUsTerms0085d.Replacements.Values.Select(v => JsonNode.Parse(v.New)!.AsObject())
            .Concat(JsonNode.Parse(SeedUsTerms0085d.NewTemplates)!.AsArray().OfType<JsonObject>());

    [Fact]
    public void Replacements_start_from_the_seeded_templates()
    {
        var seeded = JsonNode.Parse(SeedSpecialistTools0085c.NewTemplates)!.AsArray().OfType<JsonObject>()
            .ToDictionary(t => (string)t["key"]!, t => t.ToJsonString());
        foreach (var (key, (old, _)) in SeedUsTerms0085d.Replacements)
            Assert.Equal(seeded[key], old);
        Assert.Contains("Leva a correção do card", SeedUsTerms0085d.Descriptions["pr-release-version"].Old);
    }

    [Fact]
    public void Script_template_has_the_ticket_pair_for_bug_and_user_story()
    {
        var t = JsonNode.Parse(SeedUsTerms0085d.Replacements["script-com-chamado"].New)!.AsObject();
        Assert.Equal(["bug", "user-story"], t["when"]!["type"]!.AsArray().Select(x => (string?)x));
        var tasks = t["tasks"]!.AsArray().Select(x => ((string?)x!["kind"], (string?)x["executor"])).ToList();
        Assert.Equal([("sql-query", null), ("sql-script", null), ("ticket", "claude"), ("ticket", "user"), ("sql-query", null)], tasks);
    }

    [Fact]
    public void User_story_templates_never_talk_about_correction_or_root_cause()
    {
        foreach (var t in NewVersions())
        {
            var types = t["when"]?["type"]?.AsArray().Select(x => (string?)x).ToList() ?? [];
            foreach (var text in Strings(t))
                Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"{t["key"]}: marcador desconhecido em \"{text}\"");
            if (!types.Contains("user-story")) continue;
            var all = t.ToJsonString().ToLowerInvariant();
            Assert.DoesNotContain("correção", all);
            Assert.DoesNotContain("root cause", all);
            Assert.DoesNotContain("rootcause", all);
        }
        foreach (var (_, (_, desc)) in SeedUsTerms0085d.Descriptions)
            Assert.DoesNotContain("correção", desc);
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };
}
