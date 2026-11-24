using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;
using solvace.prform.AwsDeploy;
using solvace.prform.infra.Migrations;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0100: seed da aba AWS (ambientes, acompanhamento, tipo aws-deploy-map) e a leitura da configuração.</summary>
public class AwsDeploy0100Tests
{
    [Fact]
    public void Seed_environments_are_the_same_as_the_code_default()
    {
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SeedAwsDeploy0100.Environments), JsonNode.Parse(AwsDeploySettings.DefaultEnvironmentsJson)));
        Assert.Equal(SeedAwsDeploy0100.FollowMinutes, AwsDeploySettings.DefaultFollowMinutes.ToString());
        Assert.Equal(SeedAwsDeploy0100.FollowMax, AwsDeploySettings.DefaultFollowMax.ToString());
    }

    [Fact]
    public void Seed_kind_is_aws_deploy_map()
    {
        var kind = JsonNode.Parse(SeedAwsDeploy0100.NewKind)!.AsObject();
        Assert.Equal(WorkTaskKind.AwsDeployMap, (string?)kind["kind"]);
        Assert.Equal("Mapear deploy na AWS", (string?)kind["label"]);
        Assert.Equal("claude", (string?)kind["executor"]);
        Assert.Contains(WorkTaskKind.AwsDeployMap, WorkTaskKind.Known);
    }

    [Fact]
    public void Defaults_when_nothing_is_configured()
    {
        var config = AwsDeploySettings.From(new Dictionary<string, string>());
        Assert.Equal(["dev", "qa", "sandboxefeso", "sandboxrt", "prod"], config.Environments.Select(e => e.Key));
        Assert.Equal("efeso.solvacelabs.com", config.Environments[^1].Host);
        Assert.Equal("Produção", config.Environments[^1].Label);
        Assert.Equal(2, config.RefreshFollowMinutes);
        Assert.Equal(15, config.RefreshFollowMax);
    }

    [Fact]
    public void Configured_environments_are_ordered_and_cleaned()
    {
        var config = AwsDeploySettings.From(new Dictionary<string, string>
        {
            [SkillsConfigurationKeys.AwsDeployEnvironments] =
                """[{"key":"QA","label":"QA","host":"QA.solvacelabs.com","order":2,"tier":"QA"},{"key":"dev","host":"devsp.solvacelabs.com","order":1},{"key":"sem-host"}]""",
            [SkillsConfigurationKeys.AwsDeployFollowMinutes] = "999",
            [SkillsConfigurationKeys.AwsDeployFollowMax] = "abc",
        });
        Assert.Equal(["dev", "qa"], config.Environments.Select(e => e.Key));
        Assert.Equal("dev", config.Environments[0].Label);
        Assert.Equal("qa.solvacelabs.com", config.Environments[1].Host);
        Assert.Equal("qa", config.Environments[1].Tier);
        Assert.Equal(60, config.RefreshFollowMinutes);
        Assert.Equal(15, config.RefreshFollowMax);
    }

    [Fact]
    public void Invalid_environments_fall_back_to_the_default() =>
        Assert.Equal(5, AwsDeploySettings.From(new Dictionary<string, string> { [SkillsConfigurationKeys.AwsDeployEnvironments] = "{oops" })
            .Environments.Count);
}
