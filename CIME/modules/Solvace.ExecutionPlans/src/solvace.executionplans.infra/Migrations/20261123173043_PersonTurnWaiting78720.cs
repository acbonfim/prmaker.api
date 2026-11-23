using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class PersonTurnWaiting78720 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Card 78720: itens do plano "em andamento" em que só sobrou a vez da pessoa (o resto depende dela) passam a
            // aguardar — a mesma regra do WorkItem.OnlyPersonTurn, para os itens parados antes da correção.
            migrationBuilder.Sql("""
                WITH ready AS (
                    SELECT t."WorkItemId", t."Executor"
                      FROM execution."WorkItemTasks" t
                     WHERE t."Status" = 'pending' AND NOT t."Held"
                       AND NOT EXISTS (
                           SELECT 1
                             FROM jsonb_array_elements_text(t."DependsOn") dep(n)
                             JOIN execution."WorkItemTasks" d ON d."WorkItemId" = t."WorkItemId" AND d."Number" = dep.n::bigint
                            WHERE d."Status" IN ('pending', 'running', 'waiting', 'failed'))
                ),
                target AS (
                    SELECT w."Id"
                      FROM execution."WorkItems" w
                     WHERE w."Status" = 'running' AND w."PlanId" IS NOT NULL AND w."StartedAt" IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM execution."WorkItemTasks" x
                                        WHERE x."WorkItemId" = w."Id" AND x."Status" IN ('running', 'waiting', 'failed'))
                       AND EXISTS (SELECT 1 FROM ready r WHERE r."WorkItemId" = w."Id")
                       AND NOT EXISTS (SELECT 1 FROM ready r WHERE r."WorkItemId" = w."Id" AND r."Executor" <> 'user')
                ),
                logs AS (
                    INSERT INTO execution."WorkItemLogs" ("WorkItemId", "TaskNumber", "Actor", "Kind", "Text", "At")
                    SELECT "Id", NULL, 'Plano', 'status', 'Status: running → waiting.', now() FROM target
                )
                UPDATE execution."WorkItems" w
                   SET "Status" = 'waiting', "UpdatedAt" = now()
                  FROM target
                 WHERE w."Id" = target."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
