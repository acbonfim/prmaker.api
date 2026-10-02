using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0049: fila do executor — a correção abre uma sessão nova do Claude (só o resumo da análise, em vez de reler a
    /// conversa inteira da análise a cada resposta) e um comentário espera 120 s antes de retomar o card (comentários em
    /// sequência viram uma retomada só). Chaves no "Skills Configurations"; só as que faltam — valores já editados pelo
    /// admin são preservados.
    /// </summary>
    public partial class SeedExecutorQueueSettings : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ExecutorCorrectionNewSession": "true", "ExecutorNoteDelaySeconds": "120"}
""";
        private const string SkillsFieldSettings = """
{"ExecutorCorrectionNewSession": {"Label": "Executor: correção numa sessão nova do Claude, só com o resumo da análise (true/false)"}, "ExecutorNoteDelaySeconds": {"Label": "Executor: segundos de espera depois de um comentário antes de retomar o card (junta os comentários; 0 = na hora)"}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        '{{SkillsOptions}}'::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."Plugins"
                SET "FieldSettings" = ('{{SkillsFieldSettings}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['ExecutorCorrectionNewSession', 'ExecutorNoteDelaySeconds'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['ExecutorCorrectionNewSession', 'ExecutorNoteDelaySeconds'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
