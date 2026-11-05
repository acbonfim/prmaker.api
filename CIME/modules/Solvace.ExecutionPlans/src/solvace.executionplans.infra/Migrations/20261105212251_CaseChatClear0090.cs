using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class CaseChatClear0090 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClearedAt",
                schema: "execution",
                table: "CaseChats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClearedBy",
                schema: "execution",
                table: "CaseChats",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClearedAt",
                schema: "execution",
                table: "CaseChats");

            migrationBuilder.DropColumn(
                name: "ClearedBy",
                schema: "execution",
                table: "CaseChats");
        }
    }
}
