using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AwsDeploy0100 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwsDeployMapApplied",
                schema: "execution",
                table: "WorkItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AwsCardSnapshots",
                schema: "execution",
                columns: table => new
                {
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CollectedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CollectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwsCardSnapshots", x => x.CardNumber);
                });

            migrationBuilder.CreateTable(
                name: "AwsPipelines",
                schema: "execution",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Account = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Region = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Updated = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Repositories = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    RepositoryKeys = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    FilePaths = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CodebuildEnv = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DeployTargets = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    Stages = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwsPipelines", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "AwsRefreshes",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    FollowCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwsRefreshes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwsRefreshes_CardNumber_CreatedAt",
                schema: "execution",
                table: "AwsRefreshes",
                columns: new[] { "CardNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwsRefreshes_RequestedByUserId_Status",
                schema: "execution",
                table: "AwsRefreshes",
                columns: new[] { "RequestedByUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwsCardSnapshots",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "AwsPipelines",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "AwsRefreshes",
                schema: "execution");

            migrationBuilder.DropColumn(
                name: "AwsDeployMapApplied",
                schema: "execution",
                table: "WorkItems");
        }
    }
}
