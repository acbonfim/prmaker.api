using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0077: assistente do card — sugestões que não travam (estimativa inicial, lançar horas, publicar resumos de espera):
    /// ligado, etapas que sugerem horas, horas por etapa e a média do time por card. Só as chaves que faltam.
    /// </summary>
    public partial class SeedAssistant0077 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"AssistantEnabled": "true", "AssistantHoursSteps": "montar-analise,propor-solucoes,corrigir-*,validar-qa", "AssistantHoursPerStep": "1", "AssistantAverageHours": "3"}
""";
        private const string SkillsFieldSettings = """
{"AssistantEnabled": {"Label": "Assistente do card: sugere estimativa inicial, lançar horas e publicar resumos (true/false)"}, "AssistantHoursSteps": {"Label": "Assistente: etapas que, ao concluir, sugerem lançar horas (lista; corrigir-* = prefixo)"}, "AssistantHoursPerStep": {"Label": "Assistente: horas sugeridas por etapa concluída"}, "AssistantAverageHours": {"Label": "Assistente: média de horas do time por card (avisa quando passa)"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['AssistantEnabled', 'AssistantHoursSteps', 'AssistantHoursPerStep', 'AssistantAverageHours'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['AssistantEnabled', 'AssistantHoursSteps', 'AssistantHoursPerStep', 'AssistantAverageHours'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
