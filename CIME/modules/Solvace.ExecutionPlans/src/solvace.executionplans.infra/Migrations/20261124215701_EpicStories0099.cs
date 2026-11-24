using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class EpicStories0099 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpicStories",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('execution.work_item_number_seq')"),
                    EpicId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpicNumber = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AcceptanceCriteria = table.Column<string>(type: "text", nullable: true),
                    ReproSteps = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    Fields = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CardId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConvertingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpicStories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpicStories_Epics_EpicId",
                        column: x => x.EpicId,
                        principalSchema: "execution",
                        principalTable: "Epics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpicStories_CardId",
                schema: "execution",
                table: "EpicStories",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_EpicStories_EpicId_Status",
                schema: "execution",
                table: "EpicStories",
                columns: new[] { "EpicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EpicStories_Number",
                schema: "execution",
                table: "EpicStories",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpicStories",
                schema: "execution");
        }
    }
}
