using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddStepWaitingOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaitingOn",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Etapas já aguardando: perguntas pelo texto gravado pelo servidor; o resto (chamado, merge) é externo.
            migrationBuilder.Sql("""
                UPDATE execution."ExecutionSteps"
                   SET "WaitingOn" = CASE WHEN "StatusReason" LIKE 'Aguardando%resposta%' THEN 'answer' ELSE 'external' END
                 WHERE "Status" = 'waiting';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaitingOn",
                schema: "execution",
                table: "ExecutionSteps");
        }
    }
}
