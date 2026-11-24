using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0100: índice de pipelines (incremental, compartilhado) e o texto do <c>prmake_aws_status</c>.</summary>
public class AwsPipelineIndex0100Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T19:00:00Z");

    private static AwsPipelineDto Dto(string name, string updated = "2026-11-20T10:00:00.123000-03:00") => new()
    {
        Name = name,
        Updated = updated,
        Type = "V2",
        Repositories = [new AwsPipelineRepositoryDto { Repository = "electradv/edv-solvace-apps", Branch = "qa", Action = "Source" }],
        FilePathsInclude = ["projects/action-plan/**", "libs/**"],
        FilePathsExclude = ["**/*.md"],
        Tier = "qa",
        CodebuildEnv = "qa",
        DeployTargets = [new AwsDeployTargetDto { Action = "Deploy", Stage = "Deploy", Provider = "S3", Target = "action-plan-qa.solvacelabs" }],
        Stages = ["Source", "Build", "Deploy"],
    };

    [Fact]
    public void Incremental_upsert_keeps_what_did_not_come()
    {
        var plan = AwsPipelineIndex.Build(["a", "b", "c"], [Dto("b"), Dto("d")], [], full: false);
        Assert.Equal(["b", "d"], plan.Upserts.Select(u => u.Name).OrderBy(n => n));
        Assert.Empty(plan.Remove);
    }

    [Fact]
    public void Removed_names_leave_the_index_unless_they_also_came()
    {
        var plan = AwsPipelineIndex.Build(["a", "b", "c"], [Dto("b")], ["a", "b", "zz"], full: false);
        Assert.Equal(["a"], plan.Remove);
    }

    [Fact]
    public void Full_removes_everything_that_did_not_come()
    {
        var plan = AwsPipelineIndex.Build(["a", "b", "c"], [Dto("b"), Dto("new")], [], full: true);
        Assert.Equal(["a", "c"], plan.Remove);
        Assert.Equal(2, plan.Upserts.Count);
    }

    [Fact]
    public void Oversized_or_nameless_pipelines_are_skipped_and_duplicates_collapse()
    {
        var huge = Dto("huge");
        huge.FilePathsInclude = Enumerable.Range(0, 2000).Select(i => $"projects/very/long/path/{i}/**").ToList();
        var plan = AwsPipelineIndex.Build([], [Dto("x", "1"), Dto("x", "2"), huge, Dto(" ")], [], full: false);
        Assert.Equal(["huge", "(sem nome)"], plan.Skipped);
        Assert.Equal("2", Assert.Single(plan.Upserts).Updated);
    }

    [Fact]
    public void Batch_limit_is_enforced() =>
        Assert.Throws<DomainException>(() =>
            AwsPipelineIndex.Build([], Enumerable.Range(0, AwsPipelineIndex.MaxBatch + 1).Select(i => Dto($"p{i}")).ToList(), [], false));

    [Fact]
    public void Row_round_trips_the_dto_and_keeps_updated_verbatim()
    {
        var row = new AwsPipeline(Dto("edv-solvace-action-plan-qa"), "367983645102", "us-east-1", Now);
        Assert.Equal("|electradv/edv-solvace-apps|", row.RepositoryKeys);
        Assert.Contains(AwsPipeline.RepositoryKey(" ElectraDV/edv-solvace-apps "), row.RepositoryKeys);
        var dto = row.ToDto();
        Assert.Equal("2026-11-20T10:00:00.123000-03:00", dto.Updated);
        Assert.Equal(["projects/action-plan/**", "libs/**"], dto.FilePathsInclude);
        Assert.Equal(["**/*.md"], dto.FilePathsExclude);
        Assert.Equal("S3", Assert.Single(dto.DeployTargets).Provider);
        Assert.Equal("qa", Assert.Single(dto.Repositories).Branch);
        Assert.Equal(Now, row.SeenAt);
    }

    [Fact]
    public void Snapshot_over_two_megabytes_is_refused()
    {
        var big = "{\"x\":\"" + new string('a', AwsCardSnapshot.MaxDataBytes) + "\"}";
        Assert.Throws<DomainException>(() => new AwsCardSnapshot("78720", big, Now, "Alex", null, 1, Now));
    }

    [Fact]
    public void Status_text_without_snapshot_tells_how_to_collect()
    {
        var text = AwsStatusText.Format("78720", null, null, null);
        Assert.Contains("Sem retrato da AWS para o card 78720", text);
        Assert.Contains("Atualizar", text);
        Assert.Contains("bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect 78720", text);
    }

    [Fact]
    public void Status_text_lists_each_environment_with_its_pipelines()
    {
        const string json = """
        {
          "version": 1, "card": 78720, "collectedAt": "2026-11-24T19:30:00Z",
          "environments": [
            { "key": "qa", "label": "QA", "verdict": "deployed", "message": "Nosso código está em QA",
              "pipelines": [{ "name": "edv-solvace-action-plan-qa", "status": "Succeeded", "lastRunAt": "2026-11-24T18:02:00Z",
                              "deployedSha": "38f2a1a9c0ffee", "deployed": true, "inBranch": true }] },
            { "key": "sandboxefeso", "label": "Sandboxefeso", "verdict": "absent", "pipelines": [] }
          ],
          "denied": ["ecs", "eks"]
        }
        """;
        var text = AwsStatusText.Format("78720", json, Now, "Alex Carlos");
        Assert.Contains("coletado em 24/11/2026 19:00 UTC por Alex Carlos", text);
        Assert.Contains("- QA: Nosso código está em QA", text);
        Assert.Contains("edv-solvace-action-plan-qa: Succeeded, última 24/11/2026 18:02 UTC, commit 38f2a1a9, com o nosso código", text);
        Assert.Contains("- Sandboxefeso: Ainda não está em Sandboxefeso", text);
        Assert.Contains("Sem permissão nesta conta: ecs, eks.", text);
    }

    [Fact]
    public void Status_text_mentions_an_active_collection()
    {
        var r = new AwsRefresh("78720", Guid.NewGuid(), "Bia", AwsRefreshSource.Button, 0, null, Now);
        Assert.Contains("Coleta na fila (pedida por Bia)", AwsStatusText.Format("78720", null, null, null, r));
    }
}
