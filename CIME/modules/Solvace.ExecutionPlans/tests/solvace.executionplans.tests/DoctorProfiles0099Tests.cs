using System.Text.Json;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0099 (ajustes 2): doctor por perfil — PO / gestor de problemas (management) só precisa de Claude Code, skills do PRMake
/// (+ api-key/MCP), executor e as ferramentas das skills; workspace, repositórios, git, AWS, CodeArtifact e bancos ficam só no
/// perfil dev (aparecem como "não necessário para o seu perfil", nunca como erro).
/// </summary>
public class DoctorProfiles0099Tests
{
    private static ExecutionWorkerResponse Worker(params (string Name, bool Ok)[] checks) => new()
    {
        Doctor = JsonSerializer.SerializeToElement(checks.Select(c => new { name = c.Name, ok = c.Ok, severity = "error" }))
    };

    private static readonly (string, bool)[] Typical =
    [
        ("Claude Code", true), ("Login do Claude Code", true), ("MCP do PRMake", true), ("Skills do PRMake", true),
        ("api-key do usuário", true), ("Credencial do executor", true), ("Workspace", true), ("Mapa de repositórios", false),
        ("Ferramenta git", false), ("Ferramenta bash", true), ("Ferramenta jq", true), ("Ferramenta python3", true),
        ("CLI da AWS (aba AWS)", false), ("Acesso aos bancos (SQL Server)", false), ("Banco prod", false)
    ];

    [Fact]
    public void Management_ignores_dev_only_failures()
    {
        var w = DoctorProfiles.Apply(Worker(Typical), DoctorProfiles.Management);
        Assert.Equal("management", w.Profile);
        Assert.Equal(0, w.ProfileDoctorProblems);
        Assert.Equal(["dev"], w.DoctorRequiredFor!["Ferramenta git"]);
        Assert.Equal(["dev"], w.DoctorRequiredFor["Mapa de repositórios"]);
        Assert.Equal(["dev"], w.DoctorRequiredFor["CLI da AWS (aba AWS)"]);
        Assert.Equal(["dev"], w.DoctorRequiredFor["Banco prod"]);
        Assert.Equal(["dev", "management"], w.DoctorRequiredFor["Claude Code"]);
        Assert.Equal(["dev", "management"], w.DoctorRequiredFor["Skills do PRMake"]);
        Assert.Equal(["dev", "management"], w.DoctorRequiredFor["Ferramenta python3"]);
    }

    [Fact]
    public void Dev_counts_every_failure()
    {
        var w = DoctorProfiles.Apply(Worker(Typical), DoctorProfiles.Dev);
        Assert.Equal("dev", w.Profile);
        Assert.Equal(5, w.ProfileDoctorProblems);
    }

    [Fact]
    public void Management_still_fails_on_what_it_needs()
    {
        var w = DoctorProfiles.Apply(Worker(("Claude Code", false), ("Skills do PRMake", false), ("Ferramenta git", false)), DoctorProfiles.Management);
        Assert.Equal(2, w.ProfileDoctorProblems);
    }

    [Fact]
    public void Without_doctor_the_map_still_lists_the_management_checks()
    {
        var w = DoctorProfiles.Apply(new ExecutionWorkerResponse(), DoctorProfiles.Management);
        Assert.Equal(0, w.ProfileDoctorProblems);
        Assert.All(DoctorProfiles.ManagementChecks, name => Assert.Contains("management", w.DoctorRequiredFor![name]));
    }
}
