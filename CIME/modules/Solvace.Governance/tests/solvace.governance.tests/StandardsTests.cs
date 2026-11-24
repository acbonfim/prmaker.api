using System.Text.RegularExpressions;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.governance.domain.Standards;
using Xunit;

namespace solvace.governance.tests;

/// <summary>0101: parser da wiki de nomenclatura (cópia em features/0101/wiki) e conferências embutidas de SQL/EF.</summary>
public class StandardsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static string WikiDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "features", "0101", "wiki"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("features/0101/wiki"), "features", "0101", "wiki");
    }

    private static List<WikiPage> Pages() =>
        Directory.GetFiles(WikiDir(), "*.md").OrderBy(f => f)
            .Select(f => new WikiPage(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f), null)).ToList();

    private static List<GovernanceStandard> Standards(string status = StandardStatus.Active, string effect = GovernanceEffect.Block) =>
        WikiStandardsParser.Parse(Pages()).Select(p =>
        {
            var s = new GovernanceStandard(Guid.NewGuid(), p.Key);
            s.Update(p.Data with { Status = status, Effect = effect }, "teste", Now);
            return s;
        }).ToList();

    private static GovernanceSettings Enforce()
    {
        var s = new GovernanceSettings();
        s.Update(GovernanceMode.Enforce, 365, true, 1500, 60, [], GovernanceSource.All, s.Milestones, "t", Now);
        return s;
    }

    [Fact]
    public void Wiki_v2_has_104_rules_and_28_proposals()
    {
        var parsed = WikiStandardsParser.Parse(Pages());
        Assert.Equal(104, parsed.Count);
        Assert.Equal(28, parsed.Count(p => p.IsNew));
        Assert.Equal(parsed.Count, parsed.Select(p => p.Key).Distinct().Count());
        Assert.All(parsed, p => Assert.Equal(GovernanceEffect.Monitor, p.Data.Effect));
        Assert.Equal("v2.0", WikiStandardsParser.Version(Pages()));
        var fk7 = parsed.Single(p => p.Data.Code == "FK7");
        Assert.Equal(StandardStatus.Proposed, fk7.Data.Status);
        Assert.Equal("fk", fk7.Data.Topic);
        Assert.Equal(StandardStatus.Active, parsed.Single(p => p.Data.Code == "PK3").Data.Status); // (CHANGED) vale
        Assert.Equal(SqlStandardChecks.FkName, parsed.Single(p => p.Data.Code == "FK3").Data.Check!.Pattern);
        Assert.Contains("CreateDate", parsed.Single(p => p.Data.Code == "N1").Data.Examples!.Good);
        Assert.Contains("Create Date", parsed.Single(p => p.Data.Code == "N1").Data.Examples!.Bad);
        Assert.All(parsed, p => Assert.False(string.IsNullOrWhiteSpace(p.Data.TextPt)));
        // Todas as conferências citadas existem.
        Assert.All(parsed.Where(p => p.Data.Check!.Kind == StandardCheck.Builtin), p => Assert.Contains(p.Data.Check!.Pattern, SqlStandardChecks.All));
    }

    [Fact]
    public void The_wiki_own_ddl_examples_pass_every_check()
    {
        var blocks = Pages().SelectMany(p => Regex.Matches(p.Markdown, @"## DDL example[^\n]*\n+```sql\s*\n(?<sql>.*?)```", RegexOptions.Singleline)
            .Select(m => (Page: p.Path, Sql: m.Groups["sql"].Value))).ToList();
        Assert.True(blocks.Count >= 5, $"esperava os exemplos de DDL das subpáginas, achei {blocks.Count}");
        foreach (var (page, sql) in blocks)
        {
            var hits = SqlStandardChecks.Run(sql, SqlStandardChecks.All.Where(c => c is not SqlStandardChecks.CreateOrAlterProcedure).ToList());
            Assert.True(hits.Count == 0, $"{page}: {string.Join(" | ", hits.Select(h => $"{h.Check} L{h.Line}: {h.Excerpt}"))}");
        }
    }

    [Theory]
    [InlineData("CREATE TABLE actionPlan.ActionPlan (ActionPlanId INT NOT NULL CONSTRAINT PK_actionPlan_ActionPlan_Id PRIMARY KEY CLUSTERED)", SqlStandardChecks.PkName)]
    [InlineData("ALTER TABLE actionPlan.ActionPlan ADD CONSTRAINT FK_ActionPlan_Area FOREIGN KEY (AreaId, SiteId) REFERENCES area.Area (AreaId, SiteId);", SqlStandardChecks.FkName)]
    [InlineData("CREATE NONCLUSTERED INDEX IX_ActionPlan_AreaId ON actionPlan.ActionPlan (AreaId);", SqlStandardChecks.IndexName)]
    [InlineData("CREATE UNIQUE NONCLUSTERED INDEX IX_actionPlan_ActionPlan_01 ON actionPlan.ActionPlan (Code);", SqlStandardChecks.IndexName)]
    [InlineData("CREATE TABLE actionPlan.ActionPlan (ActionPlanId INT NOT NULL,\n IsActive BIT NOT NULL DEFAULT 1)", SqlStandardChecks.UnnamedConstraint)]
    [InlineData("ALTER TABLE actionPlan.ActionPlan ADD CONSTRAINT DF_IsActive DEFAULT 1 FOR IsActive;", SqlStandardChecks.DefaultName)]
    [InlineData("ALTER TABLE rca.Analysis ADD CONSTRAINT CK_Analysis_Dates CHECK (EndDate >= StartDate);", SqlStandardChecks.CheckName)]
    [InlineData("ALTER TABLE language.StandardLanguage ADD CONSTRAINT FK_language_StandardLanguage_LanguageId FOREIGN KEY (LanguageId) REFERENCES language.Language (LanguageId) ON DELETE CASCADE;", SqlStandardChecks.Cascade)]
    [InlineData("ALTER TABLE language.StandardLanguage WITH NOCHECK ADD CONSTRAINT FK_language_StandardLanguage_LanguageId FOREIGN KEY (LanguageId) REFERENCES language.Language (LanguageId);", SqlStandardChecks.NoCheck)]
    [InlineData("CREATE OR ALTER PROCEDURE language.spStandardLanguage AS SET NOCOUNT ON;", SqlStandardChecks.ProcedurePrefix)]
    [InlineData("CREATE PROCEDURE language.prStandardLanguage AS SET NOCOUNT ON;", SqlStandardChecks.CreateOrAlterProcedure)]
    [InlineData("CREATE OR ALTER VIEW language.StandardLanguageView AS SELECT LanguageId FROM language.Language;", SqlStandardChecks.ViewPrefix)]
    [InlineData("CREATE OR ALTER VIEW language.vwLanguage AS SELECT * FROM language.Language;", SqlStandardChecks.SelectStar)]
    [InlineData("CREATE TABLE finance.Invoice (InvoiceId INT NOT NULL CONSTRAINT PK_finance_Invoice PRIMARY KEY, Amount MONEY NOT NULL)", SqlStandardChecks.MoneyType)]
    [InlineData("CREATE SCHEMA ActionPlan AUTHORIZATION dbo;", SqlStandardChecks.SchemaName)]
    [InlineData("CREATE SCHEMA action_plan;", SqlStandardChecks.SchemaName)]
    [InlineData("CREATE TABLE TB_MST_LANGUAGE (Id INT)", SqlStandardChecks.TablePrefix)]
    [InlineData("CREATE TABLE Language (LanguageId INT)", SqlStandardChecks.UnqualifiedObject)]
    [InlineData("CREATE TABLE dbo.NewThing (NewThingId INT)", SqlStandardChecks.NewObjectInDbo)]
    [InlineData("CREATE TABLE actionPlan.Action_Plan (ActionPlanId INT)", SqlStandardChecks.TableName)]
    [InlineData("CREATE TABLE actionPlan.ActionPlan (ActionPlanId INT NOT NULL CONSTRAINT PK_actionPlan_ActionPlan PRIMARY KEY,\n Due_Date DATETIME2 NULL)", SqlStandardChecks.ObjectUnderscore)]
    [InlineData("CREATE TABLE language.Language (Id INT NOT NULL CONSTRAINT PK_language_Language PRIMARY KEY)", SqlStandardChecks.MeaninglessId)]
    public void Bad_ddl_is_found(string sql, string check)
    {
        var hits = SqlStandardChecks.Run(sql, SqlStandardChecks.All);
        Assert.Contains(hits, h => h.Check == check);
    }

    [Theory]
    [InlineData("SELECT * FROM TB_WCM_SITE WHERE SiteId = 3")]                      // consulta de análise (não é definição)
    [InlineData("UPDATE dbo.TB_PLANO SET Status = 2 WHERE PlanoId = 10")]            // correção no legado (dbo)
    [InlineData("ALTER TABLE dbo.TB_PLANO ADD Observacao_Extra NVARCHAR(200) NULL DEFAULT ''")] // legado: nomes ficam
    [InlineData("CREATE NONCLUSTERED INDEX IX_TB_PLANO_STATUS ON dbo.TB_PLANO (Status)")]
    [InlineData("IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_alert_Alert_01') SELECT COUNT(*) FROM alert.Alert")]
    public void Legacy_and_read_only_scripts_are_left_alone(string sql)
    {
        var hits = SqlStandardChecks.Run(sql, SqlStandardChecks.All.Where(c => c != SqlStandardChecks.CreateOrAlterProcedure).ToList());
        Assert.True(hits.Count == 0, string.Join(" | ", hits.Select(h => $"{h.Check}: {h.Hint}")));
    }

    [Fact]
    public void Ef_migration_defaults_cascade_and_index_names()
    {
        const string migration = """
            migrationBuilder.CreateIndex(
                name: "IX_ActionPlan_AreaId",
                schema: "actionPlan",
                table: "ActionPlan",
                column: "AreaId");
            table.ForeignKey(name: "FK_x", column: x => x.AreaId, principalTable: "Area", onDelete: ReferentialAction.Cascade);
            IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
            """;
        var hits = SqlStandardChecks.Run(migration, SqlStandardChecks.All, "migration");
        Assert.Contains(hits, h => h.Check == SqlStandardChecks.IndexName);
        Assert.Contains(hits, h => h.Check == SqlStandardChecks.EfCascade);
        Assert.Contains(hits, h => h.Check == SqlStandardChecks.EfUnnamedDefault);
    }

    [Fact]
    public void Checker_applies_effect_status_and_mode()
    {
        const string bad = "ALTER TABLE actionPlan.ActionPlan ADD CONSTRAINT FK_x FOREIGN KEY (AreaId) REFERENCES area.Area (AreaId) ON DELETE CASCADE;";
        var active = Standards();
        var r = StandardChecker.Check(new StandardCheckRequest { Text = bad }, active, Enforce());
        Assert.Equal(GovernanceDecisionKind.Block, r.Decision);
        Assert.Contains(r.Findings, f => f.Code == "FK3");
        Assert.Contains(r.Findings, f => f.Code is "FK5" or "R5" or "FK6");

        // Proposto (aguarda o DBA) só monitora, mesmo com efeito bloquear.
        var proposed = Standards(StandardStatus.Proposed);
        var p = StandardChecker.Check(new StandardCheckRequest { Text = bad }, proposed, Enforce());
        Assert.Equal(GovernanceDecisionKind.Allow, p.Decision);
        Assert.NotEmpty(p.Findings);
        Assert.Equal(GovernanceOutcome.Monitored, p.Outcome);

        // Fora do repositório a que o padrão se aplica: não confere.
        var scoped = Standards();
        foreach (var s in scoped)
            s.Update(new StandardData { Code = s.Code, Area = s.Area, Topic = s.Topic, Title = s.Title, TextPt = s.TextPt, Status = s.Status, Effect = s.Effect,
                Check = s.Check, AppliesTo = new StandardScope { Repositories = ["revamp-*"] } }, "t", Now);
        Assert.Empty(StandardChecker.Check(new StandardCheckRequest { Text = bad, Repository = "edv-solvace" }, scoped, Enforce()).Findings);
        Assert.NotEmpty(StandardChecker.Check(new StandardCheckRequest { Text = bad, Repository = "revamp-actionplan" }, scoped, Enforce()).Findings);
    }

    [Fact]
    public void Path_standard_for_application_structure()
    {
        var s = new GovernanceStandard(Guid.NewGuid(), "app-revamp-controllers");
        s.Update(new StandardData
        {
            Code = "ARQ-API", Area = StandardArea.Application, Topic = "layers", Title = "Controllers só na camada api",
            TextPt = "Controllers ficam em src/*.api/Controllers.", Status = StandardStatus.Active, Effect = GovernanceEffect.Warn,
            Check = new StandardCheck { Kind = StandardCheck.Path, Target = "**/*Controller.cs", Pattern = "**/*.api/Controllers/*Controller.cs", MustMatch = true }
        }, "t", Now);
        var bad = StandardChecker.Check(new StandardCheckRequest { Target = "path", Path = "src/actionplan.application/Controllers/XController.cs" }, [s], Enforce());
        Assert.Equal(GovernanceDecisionKind.Warn, bad.Decision);
        var good = StandardChecker.Check(new StandardCheckRequest { Target = "path", Path = "src/actionplan.api/Controllers/XController.cs" }, [s], Enforce());
        Assert.Empty(good.Findings);
        // Arquivo de que o padrão não trata.
        Assert.Empty(StandardChecker.Check(new StandardCheckRequest { Target = "path", Path = "src/actionplan.domain/Entities/Plan.cs" }, [s], Enforce()).Findings);
    }
}
