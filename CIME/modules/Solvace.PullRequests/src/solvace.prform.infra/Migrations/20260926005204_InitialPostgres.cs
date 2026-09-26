using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prform");

            migrationBuilder.CreateTable(
                name: "Forms",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Description = table.Column<string>(type: "text", nullable: false),
                    EnvironmentName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Handovers",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CardNumber = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Handovers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plugins",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    AdminOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsPersonal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PersonalFields = table.Column<string>(type: "text", nullable: true),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    FieldSettings = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plugins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PullRequests",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CardNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RootCause = table.Column<string>(type: "text", nullable: false),
                    BranchPrefix = table.Column<string>(type: "text", nullable: true),
                    BranchName = table.Column<string>(type: "text", nullable: true),
                    RepositoryId = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    SummaryCommentId = table.Column<int>(type: "integer", nullable: true),
                    SummaryUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SummaryPublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequests_Forms_FormId",
                        column: x => x.FormId,
                        principalSchema: "prform",
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PluginConfigurations",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PluginId = table.Column<int>(type: "integer", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PluginConfigurations_Plugins_PluginId",
                        column: x => x.PluginId,
                        principalSchema: "prform",
                        principalTable: "Plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPluginConfigurations",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PluginId = table.Column<int>(type: "integer", nullable: false),
                    UserExternalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPluginConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPluginConfigurations_Plugins_PluginId",
                        column: x => x.PluginId,
                        principalSchema: "prform",
                        principalTable: "Plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestsGithub",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PullRequestRegisterId = table.Column<int>(type: "integer", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchPrefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GithubPrNumber = table.Column<int>(type: "integer", nullable: true),
                    GithubPrId = table.Column<long>(type: "bigint", nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    StatusSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestsGithub", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestsGithub_PullRequests_PullRequestRegisterId",
                        column: x => x.PullRequestRegisterId,
                        principalSchema: "prform",
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PluginConfigurations_PluginId",
                schema: "prform",
                table: "PluginConfigurations",
                column: "PluginId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_CardNumber",
                schema: "prform",
                table: "PullRequests",
                column: "CardNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_FormId",
                schema: "prform",
                table: "PullRequests",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_CardNumber",
                schema: "prform",
                table: "PullRequestsGithub",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_PullRequestRegisterId",
                schema: "prform",
                table: "PullRequestsGithub",
                column: "PullRequestRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_RepositoryId_GithubPrNumber",
                schema: "prform",
                table: "PullRequestsGithub",
                columns: new[] { "RepositoryId", "GithubPrNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPluginConfigurations_PluginId_UserExternalId",
                schema: "prform",
                table: "UserPluginConfigurations",
                columns: new[] { "PluginId", "UserExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPluginConfigurations_UserExternalId",
                schema: "prform",
                table: "UserPluginConfigurations",
                column: "UserExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Handovers",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "PluginConfigurations",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "PullRequestsGithub",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "UserPluginConfigurations",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "PullRequests",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "Plugins",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "Forms",
                schema: "prform");
        }
    }
}
