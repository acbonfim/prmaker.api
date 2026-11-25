using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0075: imagem <c>anexo:&lt;nome&gt;</c> — arquivo dos planos do card e, sem ele, dos itens de trabalho.</summary>
public class CardFileByNameTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    private static ExecutionArtifact Artifact(string name, DateTimeOffset at, Guid? noteId = null) =>
        new(Guid.NewGuid(), name, "image", "claude", at, noteId: noteId);

    private static WorkItemFile ItemFile(string name, int number, DateTimeOffset at) =>
        new(Guid.NewGuid(), number, name, "image", "claude", at);

    [Fact]
    public void Plan_file_comes_first_skill_file_before_comment_attachment()
    {
        var skill = Artifact("repro-1.png", Now.AddHours(-2));
        var comment = Artifact("repro-1.png", Now, noteId: Guid.NewGuid());
        Assert.Same(skill, CardFileByName.PickArtifact([comment, skill], "repro-1.png"));
    }

    [Fact]
    public void Without_plan_file_falls_back_to_the_newest_work_item_file_ignoring_case()
    {
        Assert.Null(CardFileByName.PickArtifact([Artifact("outro.png", Now)], "Repro-1.PNG"));

        var old = ItemFile("repro-1.png", 1, Now.AddDays(-1));
        var recent = ItemFile("REPRO-1.png", 3, Now);
        var other = ItemFile("repro-2.png", 2, Now.AddHours(1));
        Assert.Same(recent, CardFileByName.PickWorkItemFile([old, recent, other], " Repro-1.PNG "));
    }

    [Fact]
    public void Missing_or_empty_name_finds_nothing()
    {
        Assert.Null(CardFileByName.PickWorkItemFile([ItemFile("a.png", 1, Now)], "b.png"));
        Assert.Null(CardFileByName.PickWorkItemFile([ItemFile("a.png", 1, Now)], "  "));
        Assert.Null(CardFileByName.PickArtifact([Artifact("a.png", Now)], null));
    }
}
