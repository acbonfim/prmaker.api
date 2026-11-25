using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <summary>
    /// 0082: correções que nasceram de uma análise vazia (nenhuma etapa concluída, nenhum arquivo — o início da skill abriu
    /// uma versão nova porque a anterior estava cancelada; card 77900) passam a partir da última análise feita do card, e
    /// a vazia é cancelada — como a criação da correção faz a partir de agora.
    /// </summary>
    public partial class ReparentCorrectionsFromEmptyAnalysis0082 : Migration
    {
        private const string Reason = "Versão sem análise: a correção partiu da análise anterior";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE reparent_0082 AS
                SELECT sh."Id" AS "ShellId",
                       (SELECT a."Id" FROM execution."ExecutionPlans" a
                         WHERE a."CardNumber" = sh."CardNumber" AND a."Phase" = 'analysis' AND a."CreatedAt" < sh."CreatedAt"
                           AND EXISTS (SELECT 1 FROM execution."ExecutionSteps" s WHERE s."PlanId" = a."Id" AND s."Status" = 'completed')
                         ORDER BY a."CreatedAt" DESC LIMIT 1) AS "AnalysisId"
                  FROM execution."ExecutionPlans" sh
                 WHERE sh."Phase" = 'analysis' AND sh."Status" NOT IN ('cancelled', 'completed')
                   AND NOT EXISTS (SELECT 1 FROM execution."ExecutionSteps" s WHERE s."PlanId" = sh."Id" AND s."Status" = 'completed')
                   AND NOT EXISTS (SELECT 1 FROM execution."ExecutionArtifacts" f WHERE f."PlanId" = sh."Id")
                   AND EXISTS (SELECT 1 FROM execution."ExecutionPlans" c WHERE c."ParentPlanId" = sh."Id" AND c."Phase" = 'correction');

                DELETE FROM reparent_0082 WHERE "AnalysisId" IS NULL;

                UPDATE execution."ExecutionPlans" c
                   SET "ParentPlanId" = r."AnalysisId"
                  FROM reparent_0082 r
                 WHERE c."ParentPlanId" = r."ShellId" AND c."Phase" = 'correction';

                UPDATE execution."ExecutionSteps" s
                   SET "Status" = 'cancelled', "StatusReason" = '{Reason}', "StatusChangedBy" = 'PRMake', "UpdatedAt" = now()
                  FROM reparent_0082 r
                 WHERE s."PlanId" = r."ShellId" AND s."Status" NOT IN ('completed', 'cancelled', 'failed');

                UPDATE execution."ExecutionPlans" p
                   SET "Status" = 'cancelled', "StatusReason" = '{Reason}', "StatusChangedBy" = 'PRMake',
                       "FinishedAt" = now(), "UpdatedAt" = now()
                  FROM reparent_0082 r
                 WHERE p."Id" = r."ShellId";

                DROP TABLE reparent_0082;
                """.Replace("{Reason}", Reason));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
