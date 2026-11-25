using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionSituation0075 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DevTestInQaAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevTestInQaError",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DevTestInQaTriedAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Handoff",
                schema: "execution",
                table: "ExecutionPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoffAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoffBy",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SituationAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SituationCommentId",
                schema: "execution",
                table: "ExecutionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationKind",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Approvals",
                schema: "execution",
                table: "ExecutionLinks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChangesRequested",
                schema: "execution",
                table: "ExecutionLinks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewersPending",
                schema: "execution",
                table: "ExecutionLinks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DevTestInQaAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DevTestInQaError",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DevTestInQaTriedAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "Handoff",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "HandoffAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "HandoffBy",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "SituationAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "SituationCommentId",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "SituationKind",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "Approvals",
                schema: "execution",
                table: "ExecutionLinks");

            migrationBuilder.DropColumn(
                name: "ChangesRequested",
                schema: "execution",
                table: "ExecutionLinks");

            migrationBuilder.DropColumn(
                name: "ReviewersPending",
                schema: "execution",
                table: "ExecutionLinks");
        }
    }
}
