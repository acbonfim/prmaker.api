using System.Text.Json.Nodes;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0089: ferramenta "Resumo do pedido" (sub-tarefa card-brief) e evidências do card nos arquivos do item.</summary>
public class CardBrief0089Tests
{
    [Fact]
    public void Seeded_kind_is_known_and_template_uses_it_with_known_markers()
    {
        var kind = JsonNode.Parse(SeedCardBrief0089.NewKind)!.AsObject();
        Assert.Equal(WorkTaskKind.CardBrief, (string?)kind["kind"]);
        Assert.Contains(WorkTaskKind.CardBrief, WorkTaskKind.Known);
        Assert.Equal("claude", (string?)kind["executor"]);

        var template = JsonNode.Parse(SeedCardBrief0089.NewTemplate)!.AsObject();
        Assert.Equal("resumo-do-pedido", (string?)template["key"]);
        Assert.Equal(["bug", "user-story"], template["when"]!["type"]!.AsArray().Select(x => (string?)x));
        foreach (var text in Strings(template))
            Assert.True(WorkItemTemplateRenderer.UnknownMarkers(text).Count == 0, $"marcador desconhecido em \"{text}\"");
    }

    [Fact]
    public void Adjusted_template_makes_focus_optional_and_describes_the_rationale()
    {
        var seeded = JsonNode.Parse(SeedCardBrief0089.NewTemplate)!.AsObject();
        var adjusted = JsonNode.Parse(CardBriefOptionalFocus0089b.NewTemplate)!.AsObject();
        var asks = adjusted["ask"]!.AsArray().OfType<JsonObject>().ToDictionary(a => (string)a["key"]!);
        Assert.False((bool)asks["focus"]["required"]!);
        Assert.True((bool)asks["timeline"]["required"]!);
        seeded["ask"]![0]!["required"] = false;
        seeded["description"] = CardBriefOptionalFocus0089b.TemplateDescription;
        Assert.True(JsonNode.DeepEquals(seeded, adjusted));
        Assert.Contains("estado atual", CardBriefOptionalFocus0089b.TemplateDescription);
        Assert.Empty(WorkItemTemplateRenderer.UnknownMarkers(CardBriefOptionalFocus0089b.TemplateDescription));

        var kind = JsonNode.Parse(SeedCardBrief0089.NewKind)!.AsObject();
        kind["description"] = CardBriefOptionalFocus0089b.KindDescription;
        Assert.True(JsonNode.DeepEquals(kind, JsonNode.Parse(CardBriefOptionalFocus0089b.NewKind)));
    }

    [Fact]
    public void Evidence_file_name_is_stable_per_devops_key_and_tells_the_origin()
    {
        CardEvidence E(string key, string file, string source, string? detail = null) => new(key, "https://x", file, source, detail, null, null, null);

        var pasted1 = WorkItemFileApplication.EvidenceFileName(E("img:repro:1", "image.png", "campo", "Repro Steps"));
        var pasted2 = WorkItemFileApplication.EvidenceFileName(E("img:repro:2", "image.png", "campo", "Repro Steps"));
        Assert.NotEqual(pasted1, pasted2);
        Assert.Equal(pasted1, WorkItemFileApplication.EvidenceFileName(E("img:repro:1", "image.png", "campo", "Repro Steps")));
        Assert.Matches("^ev-[0-9a-f]{6}-descricao-image\\.png$", pasted1);
        Assert.Matches("^ev-[0-9a-f]{6}-discussion-print\\.jpg$", WorkItemFileApplication.EvidenceFileName(E("c:9", "print.jpg", "discussion", "Ana")));
        Assert.Matches("^ev-[0-9a-f]{6}-anexo-log\\.txt$", WorkItemFileApplication.EvidenceFileName(E("a:1", "log.txt", "anexo")));
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };
}
