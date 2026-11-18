using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <summary>0097: itens desatualizados no índice — o código citado mudou na master desde a geração (reancoragem).</summary>
    public partial class ReverseStaleItems0097 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StaleCommit",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaleReason",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StaleSince",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaleCommit",
                schema: "knowledge",
                table: "ReverseIndexEntries");

            migrationBuilder.DropColumn(
                name: "StaleReason",
                schema: "knowledge",
                table: "ReverseIndexEntries");

            migrationBuilder.DropColumn(
                name: "StaleSince",
                schema: "knowledge",
                table: "ReverseIndexEntries");
        }
    }
}
