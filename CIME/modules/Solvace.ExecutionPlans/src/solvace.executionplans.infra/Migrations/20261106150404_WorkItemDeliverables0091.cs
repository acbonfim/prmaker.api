using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemDeliverables0091 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkItemDeliverables",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskNumber = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Repository = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Target = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HasPatch = table.Column<bool>(type: "boolean", nullable: false),
                    PatchTruncated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemDeliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemDeliverables_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalSchema: "execution",
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemDeliverableContents",
                schema: "execution",
                columns: table => new
                {
                    DeliverableId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemDeliverableContents", x => x.DeliverableId);
                    table.ForeignKey(
                        name: "FK_WorkItemDeliverableContents_WorkItemDeliverables_Deliverabl~",
                        column: x => x.DeliverableId,
                        principalSchema: "execution",
                        principalTable: "WorkItemDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemDeliverables_CardNumber_TaskNumber_Key",
                schema: "execution",
                table: "WorkItemDeliverables",
                columns: new[] { "CardNumber", "TaskNumber", "Key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemDeliverables_WorkItemId_TaskNumber",
                schema: "execution",
                table: "WorkItemDeliverables",
                columns: new[] { "WorkItemId", "TaskNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemDeliverableContents",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "WorkItemDeliverables",
                schema: "execution");
        }
    }
}
