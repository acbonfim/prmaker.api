using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class CardStoryDrafts0104 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceCard",
                schema: "execution",
                table: "EpicStories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowCardStoryDrafts",
                schema: "execution",
                table: "Epics",
                type: "boolean",
                nullable: false,
                // 0104: épicos existentes já permitem rascunhos pelos cards (o padrão da flag).
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceCard",
                schema: "execution",
                table: "EpicStories");

            migrationBuilder.DropColumn(
                name: "AllowCardStoryDrafts",
                schema: "execution",
                table: "Epics");
        }
    }
}
