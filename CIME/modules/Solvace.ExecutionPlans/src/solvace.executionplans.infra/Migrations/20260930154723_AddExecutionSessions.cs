using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResumeHandledAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResumeRequestedAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeRequestedBy",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sessions",
                schema: "execution",
                table: "ExecutionPlans",
                type: "jsonb",
                nullable: true,
                // Planos existentes: lista vazia (nunca NULL) — 0033.
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResumeHandledAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "ResumeRequestedAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "ResumeRequestedBy",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "Sessions",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
