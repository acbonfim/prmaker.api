using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class Epics0099 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Epics",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('execution.work_item_number_seq')"),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    DevOpsTeam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DevOpsBoard = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DevOpsCardId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Epics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EpicCards",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    WorkItemType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpicCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpicCards_Epics_EpicId",
                        column: x => x.EpicId,
                        principalSchema: "execution",
                        principalTable: "Epics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpicCards_CardNumber",
                schema: "execution",
                table: "EpicCards",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_EpicCards_EpicId_CardNumber",
                schema: "execution",
                table: "EpicCards",
                columns: new[] { "EpicId", "CardNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Epics_CreatedBy",
                schema: "execution",
                table: "Epics",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Epics_Number",
                schema: "execution",
                table: "Epics",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Epics_Status_UpdatedAt",
                schema: "execution",
                table: "Epics",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpicCards",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "Epics",
                schema: "execution");
        }
    }
}
