using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class ReverseInfraSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReverseInfraSnapshots",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Account = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CollectedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReverseInfraSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReverseInfraSnapshots_ModuleKey",
                schema: "knowledge",
                table: "ReverseInfraSnapshots",
                column: "ModuleKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReverseInfraSnapshots",
                schema: "knowledge");
        }
    }
}
