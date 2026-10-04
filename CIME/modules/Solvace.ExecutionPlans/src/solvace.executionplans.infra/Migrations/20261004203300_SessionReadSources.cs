using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <summary>
    /// 0055: de onde a sessão leu (<c>Sources</c>/<c>BaseSources</c>) e os arquivos de código explorados
    /// (<c>ExploredFiles</c>/<c>BaseExploredFiles</c>) dentro do JSON das sessões — sem coluna nova; a migração só
    /// atualiza o modelo do EF. Sessões antigas ficam sem as listas (a tela mostra o comparativo de antes).
    /// </summary>
    public partial class SessionReadSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
