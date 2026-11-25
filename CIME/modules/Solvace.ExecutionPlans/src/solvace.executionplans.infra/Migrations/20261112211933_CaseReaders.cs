using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class CaseReaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseReaders",
                schema: "execution",
                columns: table => new
                {
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastSeenNumber = table.Column<int>(type: "integer", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseReaders", x => new { x.CardNumber, x.UserId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseReaders",
                schema: "execution");
        }
    }
}
