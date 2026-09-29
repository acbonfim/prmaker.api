using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependsOn",
                schema: "execution",
                table: "ExecutionSteps",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Executor",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "claude");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "task");

            migrationBuilder.AddColumn<string>(
                name: "Repository",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "analysis");

            migrationBuilder.CreateTable(
                name: "ExecutionLinks",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BlocksStep = table.Column<bool>(type: "boolean", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: true),
                    Repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatusChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionLinks_ExecutionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "execution",
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionQuestions",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AllowFreeText = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: true),
                    AnsweredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AnsweredVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionQuestions_ExecutionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "execution",
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans",
                column: "ParentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLinks_PlanId_StepKey",
                schema: "execution",
                table: "ExecutionLinks",
                columns: new[] { "PlanId", "StepKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionQuestions_PlanId_Status",
                schema: "execution",
                table: "ExecutionQuestions",
                columns: new[] { "PlanId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExecutionPlans_ExecutionPlans_ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans",
                column: "ParentPlanId",
                principalSchema: "execution",
                principalTable: "ExecutionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExecutionPlans_ExecutionPlans_ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropTable(
                name: "ExecutionLinks",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "ExecutionQuestions",
                schema: "execution");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionPlans_ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DependsOn",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "Executor",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "Repository",
                schema: "execution",
                table: "ExecutionSteps");

            migrationBuilder.DropColumn(
                name: "ParentPlanId",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "Phase",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
