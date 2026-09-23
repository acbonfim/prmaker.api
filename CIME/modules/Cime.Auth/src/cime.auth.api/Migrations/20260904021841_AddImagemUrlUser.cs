using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cime.auth.api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagemUrlUser : Migration
    {
        // Apenas a mudança de schema (coluna nullable). Os UpdateData de seed gerados
        // automaticamente foram removidos de propósito: são fruto de seeds
        // não-determinísticos (Guid/hash/DateTime) e reescreveriam o admin em produção.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagemUrlUser",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagemUrlUser",
                table: "AspNetUsers");
        }
    }
}
