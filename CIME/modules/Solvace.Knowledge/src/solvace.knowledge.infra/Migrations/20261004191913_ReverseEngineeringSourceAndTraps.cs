using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class ReverseEngineeringSourceAndTraps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrapsMigratedAt",
                schema: "knowledge",
                table: "ReverseModules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemId",
                schema: "knowledge",
                table: "ArchitectureSuggestions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReverseTraps",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Items = table.Column<string>(type: "jsonb", nullable: false),
                    Cards = table.Column<string>(type: "jsonb", nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseTraps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseTraps_ModuleKey_IsDeleted",
                schema: "knowledge",
                table: "ReverseTraps",
                columns: new[] { "ModuleKey", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReverseTraps",
                schema: "knowledge");

            migrationBuilder.DropColumn(
                name: "TrapsMigratedAt",
                schema: "knowledge",
                table: "ReverseModules");

            migrationBuilder.DropColumn(
                name: "ItemId",
                schema: "knowledge",
                table: "ArchitectureSuggestions");
        }
    }
}
