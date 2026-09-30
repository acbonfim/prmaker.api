using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "ArchitectureProjects",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Repository = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Keywords = table.Column<string>(type: "jsonb", nullable: false),
                    SourceCommit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceMappedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeArticles",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Subcategory = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeSyncStates",
                schema: "knowledge",
                columns: table => new
                {
                    Environment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFullSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ArticleCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeSyncStates", x => x.Environment);
                });

            migrationBuilder.CreateTable(
                name: "ArchitectureSections",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchitectureSections_ArchitectureProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "knowledge",
                        principalTable: "ArchitectureProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchitectureSectionVersions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureSectionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchitectureSectionVersions_ArchitectureSections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "knowledge",
                        principalTable: "ArchitectureSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureProjects_Key",
                schema: "knowledge",
                table: "ArchitectureProjects",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureSections_ProjectId_Key",
                schema: "knowledge",
                table: "ArchitectureSections",
                columns: new[] { "ProjectId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureSectionVersions_SectionId_Version",
                schema: "knowledge",
                table: "ArchitectureSectionVersions",
                columns: new[] { "SectionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_Environment_ArticleNumber",
                schema: "knowledge",
                table: "KnowledgeArticles",
                columns: new[] { "Environment", "ArticleNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_Environment_SourceId",
                schema: "knowledge",
                table: "KnowledgeArticles",
                columns: new[] { "Environment", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchitectureSectionVersions",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "KnowledgeArticles",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "KnowledgeSyncStates",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ArchitectureSections",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ArchitectureProjects",
                schema: "knowledge");
        }
    }
}
