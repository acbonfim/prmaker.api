using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class TaskControl78680 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependsOn",
                schema: "execution",
                table: "WorkItemTasks",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "Held",
                schema: "execution",
                table: "WorkItemTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HeldBy",
                schema: "execution",
                table: "WorkItemTasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Held",
                schema: "execution",
                table: "ExecutionSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HeldBy",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OrderPinned",
                schema: "execution",
                table: "ExecutionSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependsOn",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropColumn(
                name: "Held",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropColumn(
                name: "HeldBy",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropColumn(
                name: "Held",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "HeldBy",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "OrderPinned",
                schema: "execution",
                table: "ExecutionSteps");
        }
    }
}
