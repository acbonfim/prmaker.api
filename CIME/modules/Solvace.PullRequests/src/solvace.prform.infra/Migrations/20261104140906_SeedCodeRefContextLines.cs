using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0075: linhas de contexto da fotografia do código citado (prmake-plan.sh coderef) e etapas que exigem toda citação
    /// caminho:linha fotografada. Chaves no "Skills Configurations"; só as que faltam — valor já editado pelo admin é preservado.
    /// </summary>
    public partial class SeedCodeRefContextLines : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"CodeRefContextLines": "30", "CodeRefGateSteps": "montar-analise,propor-solucoes"}
""";
        private const string SkillsFieldSettings = """
{"CodeRefContextLines": {"Label": "Análise: linhas de contexto antes e depois do trecho citado na fotografia do código (máx. 600 linhas por fotografia)"}, "CodeRefGateSteps": {"Label": "Análise: etapas que só concluem com toda citação caminho:linha fotografada (coderef); vazio desliga"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['CodeRefContextLines', 'CodeRefGateSteps'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['CodeRefContextLines', 'CodeRefGateSteps'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
