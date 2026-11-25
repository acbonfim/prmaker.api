using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class StepTaskKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaskKind",
                schema: "execution",
                table: "ExecutionSteps",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Sub-tarefas pendentes de itens abertos que espelham o plano: o tipo que a versão anterior dava (etapa "task"
            // do Claude → "code"; chamado/pergunta → "manual") passa ao tipo deduzido pela chave da etapa (WorkTaskKind.FromStep).
            migrationBuilder.Sql(
                """
                UPDATE execution."WorkItemTasks" t
                SET "Kind" = CASE
                        WHEN s."Kind" = 'ticket' THEN 'ticket'
                        WHEN s."Kind" = 'question' THEN 'decision'
                        WHEN s."Key" IN ('identificar-card', 'coletar-dados', 'entender-us', 'consultar-base', 'mapear-codigo',
                                         'investigar-codigo', 'causa-raiz', 'desenhar-solucao', 'montar-analise', 'impacto-na-base') THEN 'research'
                        WHEN s."Key" = 'consultar-ambiente' THEN 'sql-query'
                        WHEN s."Key" = 'publicar' THEN 'notify'
                        WHEN s."Key" = 'propor-solucoes' THEN 'decision'
                        WHEN s."Key" IN ('preparar-fechamento', 'fechar-card') THEN 'devops-action'
                        WHEN s."Key" LIKE 'preparar-script%' THEN 'sql-script'
                        WHEN s."Key" LIKE 'pr-%' THEN 'pr'
                        WHEN s."Key" LIKE 'implementar-%' OR s."Key" LIKE 'corrigir-%' THEN 'code'
                        WHEN s."Key" LIKE 'validar%' THEN 'validation'
                        ELSE 'task'
                    END,
                    "UpdatedAt" = now()
                FROM execution."WorkItems" i, execution."ExecutionSteps" s
                WHERE t."WorkItemId" = i."Id"
                  AND s."PlanId" = i."PlanId"
                  AND s."Key" = t."StepKey"
                  AND t."Status" = 'pending'
                  AND i."Status" NOT IN ('completed', 'cancelled', 'reverted', 'reverting')
                  AND ((s."Kind" = 'task' AND s."Executor" = 'claude' AND t."Kind" = 'code')
                       OR (s."Kind" IN ('ticket', 'question') AND t."Kind" = 'manual'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskKind",
                schema: "execution",
                table: "ExecutionSteps");
        }
    }
}
