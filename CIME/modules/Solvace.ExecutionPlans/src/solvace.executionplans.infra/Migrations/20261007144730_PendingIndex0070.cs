using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class PendingIndex0070 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_CreatedByUserId_Status",
                schema: "execution",
                table: "ExecutionPlans",
                columns: new[] { "CreatedByUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExecutionPlans_CreatedByUserId_Status",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
