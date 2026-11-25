using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <summary>
    /// Perguntas que ficaram abertas em planos já cancelados/concluídos (card 77900): passam a canceladas, como o
    /// cancelamento/conclusão do plano faz a partir de agora.
    /// </summary>
    public partial class CancelOpenQuestionsOfFinishedPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE execution."ExecutionQuestions" q
                   SET "Status" = 'cancelled',
                       "CancelReason" = CASE p."Status" WHEN 'cancelled' THEN 'Plano cancelado' ELSE 'Plano concluído' END
                  FROM execution."ExecutionPlans" p
                 WHERE p."Id" = q."PlanId"
                   AND q."Status" = 'open'
                   AND p."Status" IN ('cancelled', 'completed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
