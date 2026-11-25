using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class BaseQuestionHints0102 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hints",
                schema: "execution",
                table: "BaseQuestions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hints",
                schema: "execution",
                table: "BaseQuestions");
        }
    }
}
