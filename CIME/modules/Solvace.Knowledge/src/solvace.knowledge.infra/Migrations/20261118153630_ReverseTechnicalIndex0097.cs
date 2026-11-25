using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <summary>0097: resumo do último índice técnico publicado (versão, data da geração, commits, contagens) no módulo.</summary>
    public partial class ReverseTechnicalIndex0097 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TechnicalIndex",
                schema: "knowledge",
                table: "ReverseModules",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TechnicalIndex",
                schema: "knowledge",
                table: "ReverseModules");
        }
    }
}
