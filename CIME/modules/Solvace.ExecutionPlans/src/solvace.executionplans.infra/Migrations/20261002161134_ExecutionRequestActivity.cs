using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionRequestActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentActivity",
                schema: "execution",
                table: "ExecutionRequests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CurrentActivityAt",
                schema: "execution",
                table: "ExecutionRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentActivityTool",
                schema: "execution",
                table: "ExecutionRequests",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecentActivities",
                schema: "execution",
                table: "ExecutionRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentActivity",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "CurrentActivityAt",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "CurrentActivityTool",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "RecentActivities",
                schema: "execution",
                table: "ExecutionRequests");
        }
    }
}
