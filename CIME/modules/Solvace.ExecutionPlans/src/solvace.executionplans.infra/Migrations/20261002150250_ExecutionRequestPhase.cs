using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionRequestPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NewSession",
                schema: "execution",
                table: "ExecutionRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                schema: "execution",
                table: "ExecutionRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewSession",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "Phase",
                schema: "execution",
                table: "ExecutionRequests");
        }
    }
}
