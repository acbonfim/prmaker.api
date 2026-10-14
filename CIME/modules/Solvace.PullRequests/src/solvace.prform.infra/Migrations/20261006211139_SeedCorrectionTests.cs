using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0069: testes locais da correção (analisar-bug) — só os specs do que mudou, uma vez, com limite de 420 s; "off"
    /// deixa só o CI do PR. Chaves no "Skills Configurations"; só as que faltam — valores já editados pelo admin são
    /// preservados.
    /// </summary>
    public partial class SeedCorrectionTests : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"CorrectionLocalTests": "changed", "CorrectionTestMaxSeconds": "420"}
""";
        private const string SkillsFieldSettings = """
{"CorrectionLocalTests": {"Label": "Correção: testes locais (changed = só os specs do que mudou, uma vez; off = só o CI do PR)"}, "CorrectionTestMaxSeconds": {"Label": "Correção: limite dos testes locais em segundos (estourou → segue para o PR e o CI valida)"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['CorrectionLocalTests', 'CorrectionTestMaxSeconds'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['CorrectionLocalTests', 'CorrectionTestMaxSeconds'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
