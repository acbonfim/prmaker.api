using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <summary>
    /// 0047: consumo por modelo dentro do JSON das sessões (<c>Models</c>/<c>BaseModels</c>) — sem coluna nova; a
    /// migração só atualiza o modelo do EF. Sessões antigas ficam sem a lista (o plano usa o modelo único delas).
    /// </summary>
    public partial class SessionModelUsage : Migration
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
