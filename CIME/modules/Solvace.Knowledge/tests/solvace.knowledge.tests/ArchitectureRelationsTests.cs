using solvace.knowledge.domain.Entities;
using Xunit;

namespace solvace.knowledge.tests;

public class ArchitectureRelationsTests
{
    private static ArchitectureProject Project() => new("revamp-users", "teste", DateTimeOffset.UtcNow);

    [Fact]
    public void Normalizes_and_deduplicates_relations()
    {
        var p = Project();
        p.SetRelations([
            new ArchitectureRelation { Target = " Revamp-Notification ", Kind = "QUEUE", Detail = "envia NOTIFICATION_WORKER" },
            new ArchitectureRelation { Target = "revamp-notification", Kind = "queue", Detail = "envia NOTIFICATION_WORKER", Evidence = "a.cs:1" },
            new ArchitectureRelation { Target = "ext:s3", Kind = "inexistente" },
            new ArchitectureRelation { Target = "  ", Kind = "http" }
        ]);
        Assert.Equal(2, p.Relations.Count);
        Assert.Equal("revamp-notification", p.Relations[0].Target);
        Assert.Equal("queue", p.Relations[0].Kind);
        Assert.Equal("other", p.Relations[1].Kind);
    }

    [Fact]
    public void Ignores_relation_to_itself_and_null_keeps_current()
    {
        var p = Project();
        p.SetRelations([new ArchitectureRelation { Target = "revamp-users", Kind = "event" }, new ArchitectureRelation { Target = "revamp-post", Kind = "event" }]);
        Assert.Single(p.Relations);
        p.SetRelations(null);
        Assert.Single(p.Relations);
        p.SetRelations([]);
        Assert.Empty(p.Relations);
    }

    [Fact]
    public void Rejects_more_than_the_limit()
    {
        var many = Enumerable.Range(0, ArchitectureProject.MaxRelations + 1)
            .Select(i => new ArchitectureRelation { Target = $"p{i}", Kind = "http" });
        Assert.ThrowsAny<Exception>(() => Project().SetRelations(many));
    }
}
