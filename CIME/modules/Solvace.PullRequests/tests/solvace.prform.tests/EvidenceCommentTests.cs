using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.prform.Execution;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>Comentário com as evidências nas ferramentas validar-em-ambiente e validar-em-qa: texto sempre, discussion opcional.</summary>
public class EvidenceCommentTests
{
    private static WorkItemTemplateContext Ctx() => new()
    {
        Card = "77434",
        Type = "bug",
        MainBranch = "hotfix/77434",
        Environments = ValidationEnvironments.Parse(null),
        EnvironmentDefaults = ValidationEnvironments.Defaults(null)
    };

    /// <summary>O modelo da 0092 como a <see cref="SeedEvidenceComment"/> deixa: pergunta no fim e a sub-tarefa depois dos ambientes.</summary>
    private static JsonObject EnvTemplate()
    {
        var t = JsonNode.Parse(SeedEnvValidation0092.NewTemplate)!.AsObject();
        t["ask"]!.AsArray().Add(JsonNode.Parse(SeedEvidenceComment.NewAsk));
        t["tasks"]!.AsArray().Add(JsonNode.Parse(SeedEvidenceComment.EnvTask));
        return t;
    }

    [Fact]
    public void Seeded_kind_is_known_and_the_texts_use_known_markers()
    {
        var kind = JsonNode.Parse(SeedEvidenceComment.NewKind)!.AsObject();
        Assert.Equal(WorkTaskKind.EvidenceComment, (string?)kind["kind"]);
        Assert.Contains(WorkTaskKind.EvidenceComment, WorkTaskKind.Known);
        Assert.Equal("claude", (string?)kind["executor"]);

        var ask = JsonNode.Parse(SeedEvidenceComment.NewAsk)!.AsObject();
        Assert.Equal("não", (string?)ask["default"]);
        Assert.True((bool)ask["required"]!);
        foreach (var text in new[] { SeedEvidenceComment.EnvTask, SeedEvidenceComment.QaTask })
            Assert.Empty(WorkItemTemplateRenderer.UnknownMarkers(text));
        Assert.Equal("comentario-evidencias-77434-item-12.md", WorkTaskKind.EvidenceCommentFileName("77434", 12));
    }

    [Fact]
    public void Env_validation_ends_with_the_comment_and_does_not_publish_by_default()
    {
        var rendered = WorkItemTemplateRenderer.Render(EnvTemplate(), Ctx(), new Dictionary<string, string> { ["environments"] = "dev, qa" });

        Assert.Equal([WorkTaskKind.EnvValidation, WorkTaskKind.EnvValidation, WorkTaskKind.EvidenceComment], rendered.Tasks.Select(t => t.Kind));
        Assert.Equal("não", rendered.Tasks[^1].Params["discussion"]);
    }

    [Fact]
    public void Env_validation_publishes_when_the_person_says_so()
    {
        var rendered = WorkItemTemplateRenderer.Render(EnvTemplate(), Ctx(),
            new Dictionary<string, string> { ["environments"] = "qa", ["discussion"] = "sim" });
        Assert.Equal("sim", rendered.Tasks.Single(t => t.Kind == WorkTaskKind.EvidenceComment).Params["discussion"]);

        Assert.Throws<WorkItemTemplateException>(() => WorkItemTemplateRenderer.Render(EnvTemplate(), Ctx(),
            new Dictionary<string, string> { ["environments"] = "qa", ["discussion"] = "talvez" }));
    }

    [Fact]
    public void Comment_waits_for_the_validations_of_the_item()
    {
        var item = new WorkItem("77434", WorkItemType.Validation, "Validar", null, WorkItemOrigin.User, null, null,
            [
                new WorkTaskInput { Title = "Validar em QA", Kind = WorkTaskKind.EnvValidation },
                new WorkTaskInput { Title = "Comentário", Kind = WorkTaskKind.EvidenceComment }
            ], null, "tester", DateTimeOffset.UtcNow);
        var tasks = item.Tasks.OrderBy(t => t.Order).ToList();
        Assert.Equal([tasks[0]], WorkTaskDependencies.Implicit(tasks, tasks[1]));
    }

    [Theory]
    [InlineData("sim", "sim")]
    [InlineData("Yes", "sim")]
    [InlineData("nao", "não")]
    [InlineData("não", "não")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Mcp_answer_maps_to_the_template_options(string? given, string? expected) =>
        Assert.Equal(expected, WorkItemMcpTools.YesNo(given));
}
