using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Relations",
                schema: "knowledge",
                table: "ArchitectureProjects",
                type: "jsonb",
                nullable: true,
                // Projetos existentes: sem relações (nunca NULL) — 0034.
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Relations",
                schema: "knowledge",
                table: "ArchitectureProjects");
        }
    }
}
