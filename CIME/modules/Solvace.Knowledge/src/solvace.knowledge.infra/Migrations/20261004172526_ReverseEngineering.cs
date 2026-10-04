using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class ReverseEngineering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReverseAssets",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Screens = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReverseCardContexts",
                schema: "knowledge",
                columns: table => new
                {
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Modules = table.Column<string>(type: "jsonb", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsultedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsultedRefs = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseCardContexts", x => x.CardNumber);
                });

            migrationBuilder.CreateTable(
                name: "ReverseIndexEntries",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    Tables = table.Column<string>(type: "jsonb", nullable: false),
                    Refs = table.Column<string>(type: "jsonb", nullable: false),
                    Evidence = table.Column<string>(type: "jsonb", nullable: false),
                    Modules = table.Column<string>(type: "jsonb", nullable: false),
                    Removed = table.Column<bool>(type: "boolean", nullable: false),
                    SectionVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseIndexEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReverseModules",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Aliases = table.Column<string>(type: "jsonb", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sources = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReverseRevisions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Lint = table.Column<string>(type: "jsonb", nullable: true),
                    Coverage = table.Column<string>(type: "jsonb", nullable: true),
                    CoverageRatio = table.Column<double>(type: "double precision", nullable: true),
                    Session = table.Column<string>(type: "jsonb", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    BaseVersion = table.Column<int>(type: "integer", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: true),
                    Progress = table.Column<string>(type: "jsonb", nullable: true),
                    ProgressAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseAssets_ModuleKey_IsDeleted",
                schema: "knowledge",
                table: "ReverseAssets",
                columns: new[] { "ModuleKey", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseIndexEntries_ModuleKey_DocType",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                columns: new[] { "ModuleKey", "DocType" });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseIndexEntries_ModuleKey_ItemId",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                columns: new[] { "ModuleKey", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseModules_Key",
                schema: "knowledge",
                table: "ReverseModules",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReverseRevisions_ModuleKey_DocType_Number",
                schema: "knowledge",
                table: "ReverseRevisions",
                columns: new[] { "ModuleKey", "DocType", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReverseRevisions_Status_UpdatedAt",
                schema: "knowledge",
                table: "ReverseRevisions",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReverseAssets",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ReverseCardContexts",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ReverseIndexEntries",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ReverseModules",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ReverseRevisions",
                schema: "knowledge");
        }
    }
}
