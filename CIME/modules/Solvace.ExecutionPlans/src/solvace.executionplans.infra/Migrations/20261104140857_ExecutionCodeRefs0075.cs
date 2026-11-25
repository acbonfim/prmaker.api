using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionCodeRefs0075 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionCodeRefs",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ref = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Language = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    StartLine = table.Column<int>(type: "integer", nullable: false),
                    EndLine = table.Column<int>(type: "integer", nullable: false),
                    Change = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Why = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Snapshot = table.Column<string>(type: "text", nullable: true),
                    FirstLine = table.Column<int>(type: "integer", nullable: false),
                    Proposal = table.Column<string>(type: "text", nullable: true),
                    Implemented = table.Column<string>(type: "text", nullable: true),
                    ImplementedSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Annotations = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionCodeRefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionCodeRefs_ExecutionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "execution",
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionCodeRefs_CardNumber_Ref",
                schema: "execution",
                table: "ExecutionCodeRefs",
                columns: new[] { "CardNumber", "Ref" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionCodeRefs_PlanId_Ref",
                schema: "execution",
                table: "ExecutionCodeRefs",
                columns: new[] { "PlanId", "Ref" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionCodeRefs",
                schema: "execution");
        }
    }
}
