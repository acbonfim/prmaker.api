using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchitectureSuggestions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureSuggestions_Status_CreatedAt",
                schema: "knowledge",
                table: "ArchitectureSuggestions",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchitectureSuggestions",
                schema: "knowledge");
        }
    }
}
