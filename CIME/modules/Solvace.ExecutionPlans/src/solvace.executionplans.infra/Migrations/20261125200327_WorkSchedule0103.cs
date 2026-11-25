using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class WorkSchedule0103 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledAt",
                schema: "execution",
                table: "WorkItemTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduledById",
                schema: "execution",
                table: "WorkItemTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Parked",
                schema: "execution",
                table: "WorkItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledAt",
                schema: "execution",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduledBy",
                schema: "execution",
                table: "WorkItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduledById",
                schema: "execution",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledAt",
                schema: "execution",
                table: "ExecutionSteps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduledById",
                schema: "execution",
                table: "ExecutionSteps",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTasks_ScheduledAt",
                schema: "execution",
                table: "WorkItemTasks",
                column: "ScheduledAt",
                filter: "\"ScheduledAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ScheduledAt",
                schema: "execution",
                table: "WorkItems",
                column: "ScheduledAt",
                filter: "\"ScheduledAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSteps_ScheduledAt",
                schema: "execution",
                table: "ExecutionSteps",
                column: "ScheduledAt",
                filter: "\"ScheduledAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItemTasks_ScheduledAt",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_ScheduledAt",
                schema: "execution",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionSteps_ScheduledAt",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropColumn(
                name: "ScheduledById",
                schema: "execution",
                table: "WorkItemTasks");

            migrationBuilder.DropColumn(
                name: "Parked",
                schema: "execution",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                schema: "execution",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledBy",
                schema: "execution",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledById",
                schema: "execution",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "ScheduledById",
                schema: "execution",
                table: "ExecutionSteps");
        }
    }
}
