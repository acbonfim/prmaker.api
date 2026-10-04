using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class ReverseEngineeringDatabaseGlossary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestionDecisions",
                schema: "knowledge",
                table: "ReverseRevisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DismissedTerms",
                schema: "knowledge",
                table: "ReverseModules",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SuggestedTerms",
                schema: "knowledge",
                table: "ReverseModules",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Synonyms",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestionDecisions",
                schema: "knowledge",
                table: "ReverseRevisions");

            migrationBuilder.DropColumn(
                name: "DismissedTerms",
                schema: "knowledge",
                table: "ReverseModules");

            migrationBuilder.DropColumn(
                name: "SuggestedTerms",
                schema: "knowledge",
                table: "ReverseModules");

            migrationBuilder.DropColumn(
                name: "Synonyms",
                schema: "knowledge",
                table: "ReverseIndexEntries");
        }
    }
}
