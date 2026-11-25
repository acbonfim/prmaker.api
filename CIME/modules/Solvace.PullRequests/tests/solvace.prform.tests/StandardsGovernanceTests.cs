using System.Text.Json;
using ModelContextProtocol.Protocol;
using solvace.governance.domain.Entities;
using solvace.prform.Governance;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0101: padrões de aplicação gerados da engenharia reversa e a trava de script na gravação pelo MCP.</summary>
public class StandardsGovernanceTests
{
    private static ReverseStandardSource Item(string module, string id, string kind, string title, string body = "", params string[] evidence) =>
        new(module, id, kind, title, body, evidence);

    [Fact]
    public void Adr_tec_and_technical_index_become_application_standards()
    {
        var items = new List<ReverseStandardSource>
        {
            Item("revamp-actionplan", "ADR-001", "ADR", "CQRS com MediatR", "- **Decisão:** Comandos e consultas separados com MediatR.\n- **Onde:** `revamp-actionplan/src/ActionPlan.Application/Commands/CreatePlan.cs:10`"),
            Item("revamp-actionplan", "TEC-001", "TEC", ".NET 8"),
            Item("revamp-actionplan", "TEC-002", "TEC", "SQL Server (EF Core)"),
            Item("revamp-actionplan", "END-001", "END", "GET /plans", "", "revamp-actionplan/src/ActionPlan.Api/Controllers/PlanController.cs:12"),
            Item("revamp-actionplan", "END-002", "END", "POST /plans", "", "revamp-actionplan/src/ActionPlan.Api/Controllers/PlanController.cs:40"),
            Item("revamp-actionplan", "END-003", "END", "GET /items", "", "revamp-actionplan/src/ActionPlan.Api/Controllers/ItemController.cs:9"),
            Item("revamp-actionplan", "TAB-001", "TAB", "actionPlan.ActionPlan", "", "banco DEMO local · actionPlan.ActionPlan")
        };
        var parsed = ReverseStandardsGenerator.Generate(items);
        var adr = parsed.Single(p => p.Data.Code == "ADR-001");
        Assert.Equal(StandardArea.Application, adr.Data.Area);
        Assert.Contains("MediatR", adr.Data.TextPt);
        Assert.Equal("revamp-actionplan#ADR-001", adr.Data.Source!.Ref);
        Assert.Equal(["revamp-actionplan"], adr.Data.AppliesTo!.Repositories);
        Assert.Contains(".NET 8", parsed.Single(p => p.Data.Code == "STACK").Data.TextPt);
        var where = parsed.Single(p => p.Data.Code == "ONDE-END");
        Assert.Contains("revamp-actionplan/src/ActionPlan.Api/Controllers/", where.Data.TextPt);
        Assert.Contains("(3 de 3)", where.Data.TextPt);
        Assert.DoesNotContain(parsed, p => p.Data.Code == "ONDE-TAB"); // evidência de banco não é caminho
        Assert.All(parsed, p => Assert.Matches("^[a-z0-9][a-z0-9-]{1,79}$", p.Key));
        // Mesma entrada → mesmas chaves e hashes (reimportação estável).
        Assert.Equal(parsed.Select(p => p.Key + p.Hash), ReverseStandardsGenerator.Generate(items).Select(p => p.Key + p.Hash));
    }

    [Theory]
    [InlineData("prmake_file", "01_78720_cria-indice.sql", "CREATE INDEX x ON a.B (C)", "sql")]
    [InlineData("prmake_work_item_file", "20261124_AddPlan.cs", "migrationBuilder.CreateTable(...)", "migration")]
    [InlineData("prmake_file", "analise-inicial.md", "texto", null)]
    [InlineData("prmake_case_file", "script.sql", "CREATE INDEX x ON a.B (C)", "sql")]
    [InlineData("prmake_log", "x.sql", "CREATE INDEX", null)]
    public void Only_sql_and_migrations_saved_through_prmake_are_checked(string tool, string name, string content, string? target)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["card"] = JsonSerializer.SerializeToElement("78720"),
            ["name"] = JsonSerializer.SerializeToElement(name),
            ["content"] = JsonSerializer.SerializeToElement(content.Replace("(...)", "(\"MigrationBuilder\")"))
        };
        var r = GovernanceMcpFilter.StandardsTarget(tool, new CallToolRequestParams { Name = tool, Arguments = args });
        Assert.Equal(target, r?.Target);
    }
}
