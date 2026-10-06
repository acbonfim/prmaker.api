using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0066: como a engenharia reversa gera — modelo dos subagentes (padrão e por documento), subagentes simultâneos,
    /// orçamento da área (KB do pacote de leitura), checkpoint, idade máxima do retrato do banco/AWS e corte dos trechos.
    /// Só grava a chave se faltar (preserva o valor do admin).
    /// </summary>
    public partial class SeedReverseEngineeringGeneration : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringGeneration": "{\"subagentModel\": \"sonnet\", \"modelByDoc\": {}, \"maxParallel\": 5, \"areaBudgetKb\": 90, \"checkpointEvery\": 10, \"snapshotMaxAgeDays\": 7, \"smallFileLines\": 400, \"blockMaxLines\": 220}"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringGeneration": {"Label": "Engenharia reversa: como a skill gera (JSON: subagentModel sonnet|haiku|opus, modelByDoc {\"design\": \"haiku\"}, maxParallel, areaBudgetKb, checkpointEvery, snapshotMaxAgeDays, smallFileLines, blockMaxLines)"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'ReverseEngineeringGeneration')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'ReverseEngineeringGeneration')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
