using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class QuestionCancelReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "execution",
                table: "ExecutionQuestions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacedBy",
                schema: "execution",
                table: "ExecutionQuestions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "execution",
                table: "ExecutionQuestions");

            migrationBuilder.DropColumn(
                name: "ReplacedBy",
                schema: "execution",
                table: "ExecutionQuestions");
        }
    }
}
