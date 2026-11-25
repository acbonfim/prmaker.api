using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class Assistant0077 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SituationAutoPost",
                schema: "execution",
                table: "ExecutionPlans",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExecutionSuggestions",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChosenAction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Actions = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionSuggestions_ExecutionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "execution",
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSuggestions_CardNumber_DedupKey",
                schema: "execution",
                table: "ExecutionSuggestions",
                columns: new[] { "CardNumber", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSuggestions_PlanId",
                schema: "execution",
                table: "ExecutionSuggestions",
                column: "PlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionSuggestions",
                schema: "execution");

            migrationBuilder.DropColumn(
                name: "SituationAutoPost",
                schema: "execution",
                table: "ExecutionPlans");
        }
    }
}
