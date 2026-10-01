using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddGuideAudienceAndFriendly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                schema: "knowledge",
                table: "ArchitectureSections",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Seções existentes: técnicas, continuam indo para o espelho das skills — 0038.
                defaultValue: "llm");

            migrationBuilder.AddColumn<string>(
                name: "BusinessArea",
                schema: "knowledge",
                table: "ArchitectureProjects",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "knowledge",
                table: "ArchitectureProjects",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                schema: "knowledge",
                table: "ArchitectureProjects",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "knowledge",
                table: "ArchitectureSections");

            migrationBuilder.DropColumn(
                name: "BusinessArea",
                schema: "knowledge",
                table: "ArchitectureProjects");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "knowledge",
                table: "ArchitectureProjects");

            migrationBuilder.DropColumn(
                name: "Tagline",
                schema: "knowledge",
                table: "ArchitectureProjects");
        }
    }
}
