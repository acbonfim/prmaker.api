using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0047: modelo do Claude Code por fase quando o executor do PRMake roda o card — Opus na análise, Sonnet na
    /// correção (apelidos: sempre a versão mais nova). Chaves no "Skills Configurations"; só as que faltam — valores já
    /// editados pelo admin são preservados.
    /// </summary>
    public partial class SeedExecutorModels : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ExecutorAnalysisModel": "opus", "ExecutorCorrectionModel": "sonnet"}
""";
        private const string SkillsFieldSettings = """
{"ExecutorAnalysisModel": {"Label": "Executor: modelo do Claude na análise (opus = sempre o Opus mais novo; vazio = padrão da máquina)"}, "ExecutorCorrectionModel": {"Label": "Executor: modelo do Claude na correção (sonnet = sempre o Sonnet mais novo; vazio = padrão da máquina)"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['ExecutorAnalysisModel', 'ExecutorCorrectionModel'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['ExecutorAnalysisModel', 'ExecutorCorrectionModel'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
