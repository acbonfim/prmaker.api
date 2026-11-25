using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class PersonTurnRunning78794 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Card 78794 (#1077): itens "em andamento" em que o que roda é só a etapa da pessoa (o advance do plano a pôs
            // em running) e o Claude não tem sub-tarefa pronta passam a aguardar — a mesma regra do WorkItem.Recalculate.
            migrationBuilder.Sql("""
                WITH claude_ready AS (
                    SELECT DISTINCT t."WorkItemId"
                      FROM execution."WorkItemTasks" t
                     WHERE t."Status" = 'pending' AND NOT t."Held" AND t."Executor" <> 'user'
                       AND NOT EXISTS (
                           SELECT 1
                             FROM jsonb_array_elements_text(t."DependsOn") dep(n)
                             JOIN execution."WorkItemTasks" d ON d."WorkItemId" = t."WorkItemId" AND d."Number" = dep.n::bigint
                            WHERE d."Status" IN ('pending', 'running', 'waiting', 'failed'))
                ),
                target AS (
                    SELECT w."Id"
                      FROM execution."WorkItems" w
                     WHERE w."Status" = 'running'
                       AND EXISTS (SELECT 1 FROM execution."WorkItemTasks" x
                                    WHERE x."WorkItemId" = w."Id" AND x."Status" = 'running' AND x."Executor" = 'user')
                       AND NOT EXISTS (SELECT 1 FROM execution."WorkItemTasks" x
                                        WHERE x."WorkItemId" = w."Id" AND x."Status" = 'running' AND x."Executor" <> 'user')
                       AND NOT EXISTS (SELECT 1 FROM claude_ready r WHERE r."WorkItemId" = w."Id")
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
