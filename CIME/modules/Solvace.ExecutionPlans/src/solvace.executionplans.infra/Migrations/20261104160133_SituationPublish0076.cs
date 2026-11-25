using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class SituationPublish0076 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SituationAutoPost",
                schema: "execution",
                table: "ExecutionUserSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SituationPostedAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationPostedBy",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituationPostedText",
                schema: "execution",
                table: "ExecutionPlans",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SituationAutoPost",
                schema: "execution",
                table: "ExecutionUserSettings");

            migrationBuilder.DropColumn(
                name: "SituationPostedAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "SituationPostedBy",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "SituationPostedText",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
