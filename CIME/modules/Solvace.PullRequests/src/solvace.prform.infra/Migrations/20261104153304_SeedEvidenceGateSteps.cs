using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0076: etapas da analisar-bug que só concluem com as evidências do card (anexos, imagens da descrição e da
    /// discussion) importadas e abertas. Chave no "Skills Configurations"; só se faltar — valor editado é preservado.
    /// </summary>
    public partial class SeedEvidenceGateSteps : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"EvidenceGateSteps": "montar-analise"}
""";
        private const string SkillsFieldSettings = """
{"EvidenceGateSteps": {"Label": "Análise: etapas que só concluem com as evidências do card no DevOps importadas e abertas (anexos, prints da descrição e da discussion); vazio desliga"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'EvidenceGateSteps')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'EvidenceGateSteps')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
