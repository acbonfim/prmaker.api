using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchitectureQuestions",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Normalized = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Coverage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuggestedProject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SuggestedSection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Times = table.Column<int>(type: "integer", nullable: false),
                    FirstAskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstAskedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastAskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAskedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AnsweredProject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AnsweredSection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureQuestions_Normalized",
                schema: "knowledge",
                table: "ArchitectureQuestions",
                column: "Normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureQuestions_Status_LastAskedAt",
                schema: "knowledge",
                table: "ArchitectureQuestions",
                columns: new[] { "Status", "LastAskedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchitectureQuestions",
                schema: "knowledge");
        }
    }
}
