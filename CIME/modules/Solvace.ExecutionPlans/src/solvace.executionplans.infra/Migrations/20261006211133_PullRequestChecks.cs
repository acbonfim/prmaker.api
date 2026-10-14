using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChecksChangedAt",
                schema: "execution",
                table: "ExecutionLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChecksFailed",
                schema: "execution",
                table: "ExecutionLinks",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChecksHeadSha",
                schema: "execution",
                table: "ExecutionLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChecksStatus",
                schema: "execution",
                table: "ExecutionLinks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecksChangedAt",
                schema: "execution",
                table: "ExecutionLinks");

            migrationBuilder.DropColumn(
                name: "ChecksFailed",
                schema: "execution",
                table: "ExecutionLinks");

            migrationBuilder.DropColumn(
                name: "ChecksHeadSha",
                schema: "execution",
                table: "ExecutionLinks");

            migrationBuilder.DropColumn(
                name: "ChecksStatus",
                schema: "execution",
                table: "ExecutionLinks");
        }
    }
}
