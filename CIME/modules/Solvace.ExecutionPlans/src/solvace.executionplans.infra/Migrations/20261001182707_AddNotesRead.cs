using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotesReadAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotesReadNumber",
                schema: "execution",
                table: "ExecutionPlans",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotesReadAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "NotesReadNumber",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
