using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NoteId",
                schema: "execution",
                table: "ExecutionArtifacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                schema: "execution",
                table: "ExecutionArtifacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExecutionNotes",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromExecutor = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionNotes_ExecutionPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "execution",
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionArtifacts_NoteId",
                schema: "execution",
                table: "ExecutionArtifacts",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionNotes_CardNumber_Number",
                schema: "execution",
                table: "ExecutionNotes",
                columns: new[] { "CardNumber", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionNotes_PlanId",
                schema: "execution",
                table: "ExecutionNotes",
                column: "PlanId");

            // Arquivos que já existem ganham o número por card (anexo #n) na ordem em que foram criados.
            migrationBuilder.Sql("""
                UPDATE execution."ExecutionArtifacts" a
                SET "Number" = r.n
                FROM (
                    SELECT a2."Id", row_number() OVER (PARTITION BY p."CardNumber" ORDER BY a2."CreatedAt", a2."Name") AS n
                    FROM execution."ExecutionArtifacts" a2
                    JOIN execution."ExecutionPlans" p ON p."Id" = a2."PlanId"
                ) r
                WHERE r."Id" = a."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionNotes",
                schema: "execution");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionArtifacts_NoteId",
                schema: "execution",
                table: "ExecutionArtifacts");

            migrationBuilder.DropColumn(
                name: "NoteId",
                schema: "execution",
                table: "ExecutionArtifacts");

            migrationBuilder.DropColumn(
                name: "Number",
                schema: "execution",
                table: "ExecutionArtifacts");
        }
    }
}
