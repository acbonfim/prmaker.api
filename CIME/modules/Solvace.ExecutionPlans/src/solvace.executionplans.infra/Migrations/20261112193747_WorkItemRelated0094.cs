using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemRelated0094 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkItemRelated",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RelatedItemNumber = table.Column<long>(type: "bigint", nullable: false),
                    RelatedTaskNumber = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Context = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemRelated", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemRelated_CardNumber",
                schema: "execution",
                table: "WorkItemRelated",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemRelated_CardNumber_RelatedItemNumber",
                schema: "execution",
                table: "WorkItemRelated",
                columns: new[] { "CardNumber", "RelatedItemNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemRelated",
                schema: "execution");
        }
    }
}
