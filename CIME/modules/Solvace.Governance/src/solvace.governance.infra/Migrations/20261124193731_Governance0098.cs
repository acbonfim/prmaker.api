using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.governance.infra.Migrations
{
    /// <inheritdoc />
    public partial class Governance0098 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "governance");

            migrationBuilder.CreateTable(
                name: "GovernanceEvents",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CardNumber = table.Column<int>(type: "integer", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StepKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkItemNumber = table.Column<int>(type: "integer", nullable: true),
                    TaskNumber = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Rules = table.Column<string[]>(type: "text[]", nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Machine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceHooks",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Event = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Matcher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Config = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Builtin = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceHooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceRules",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Effect = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Builtin = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "jsonb", nullable: false),
                    Match = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceSettings",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    HookFailOpen = table.Column<bool>(type: "boolean", nullable: false),
                    HookTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    HookCacheSeconds = table.Column<int>(type: "integer", nullable: false),
                    ExemptRoles = table.Column<string>(type: "jsonb", nullable: false),
                    RecordSources = table.Column<string>(type: "jsonb", nullable: false),
                    Milestones = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceEvents_CardNumber_StartedAt",
                schema: "governance",
                table: "GovernanceEvents",
                columns: new[] { "CardNumber", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceEvents_Outcome_StartedAt",
                schema: "governance",
                table: "GovernanceEvents",
                columns: new[] { "Outcome", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceEvents_StartedAt",
                schema: "governance",
                table: "GovernanceEvents",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceEvents_UserId_StartedAt",
                schema: "governance",
                table: "GovernanceEvents",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceHooks_Key",
                schema: "governance",
                table: "GovernanceHooks",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceRules_Key",
                schema: "governance",
                table: "GovernanceRules",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernanceEvents",
                schema: "governance");

            migrationBuilder.DropTable(
                name: "GovernanceHooks",
                schema: "governance");

            migrationBuilder.DropTable(
                name: "GovernanceRules",
                schema: "governance");

            migrationBuilder.DropTable(
                name: "GovernanceSettings",
                schema: "governance");
        }
    }
}
