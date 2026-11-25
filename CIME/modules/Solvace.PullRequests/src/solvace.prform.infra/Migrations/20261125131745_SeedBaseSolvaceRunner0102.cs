using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0102: quem responde as perguntas da Base Solvace — <c>BaseSolvaceRunner</c> no "AI Configurations" (provider | executor;
    /// campo pessoal opcional que cai no valor global "provider") e o modelo/esforço do executor nessas perguntas no "Skills
    /// Configurations" (sonnet, low; a fundo medium). Só acrescenta as chaves que faltam; valores já editados ficam.
    /// </summary>
    public partial class SeedBaseSolvaceRunner0102 : Migration
    {
        private const string AIPlugin = "AI Configurations";
        private const string SkillsPlugin = "Skills Configurations";

        private const string AIOptions = """
{"BaseSolvaceRunner": "provider"}
""";
        private const string AIFieldSettings = """
{"BaseSolvaceRunner": {"Label": "Perguntas da Base Solvace", "Optional": true, "UseGlobalDefault": true, "Suggestions": ["provider", "executor"], "Help": "Quem responde o Pergunte da Base Solvace: provider = o provedor de IA do servidor (cobra a sua chave de API); executor = o Claude Code da sua máquina (assinatura), mais preciso. Precisa do executor online."}}
""";
        private const string SkillsOptions = """
{"BaseAskExecutorModel": "sonnet", "BaseAskExecutorEffort": "low", "BaseAskExecutorDeepEffort": "medium"}
""";
        private const string SkillsFieldSettings = """
{"BaseAskExecutorModel": {"Label": "Executor: modelo do Claude nas perguntas da Base Solvace"}, "BaseAskExecutorEffort": {"Label": "Executor: esforço (--effort) nas perguntas da Base Solvace"}, "BaseAskExecutorDeepEffort": {"Label": "Executor: esforço ao analisar a fundo na Base Solvace"}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MergeKeys(AIPlugin, AIOptions, AIFieldSettings));
            migrationBuilder.Sql(MergeKeys(SkillsPlugin, SkillsOptions, SkillsFieldSettings));
            // Campo de cada pessoa (Minhas integrações); PersonalFields nulo = todas as chaves já são da pessoa.
            migrationBuilder.Sql($$"""
                UPDATE prform."Plugins"
                SET "PersonalFields" = ("PersonalFields"::jsonb || '["BaseSolvaceRunner"]'::jsonb)::text
                WHERE "Description" = '{{AIPlugin}}' AND NOT "IsDeleted" AND NULLIF("PersonalFields", '') IS NOT NULL
                  AND NOT "PersonalFields"::jsonb ? 'BaseSolvaceRunner';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveKeys(AIPlugin, AIOptions));
            migrationBuilder.Sql(RemoveKeys(SkillsPlugin, SkillsOptions));
            migrationBuilder.Sql($$"""
                UPDATE prform."Plugins"
                SET "PersonalFields" = ("PersonalFields"::jsonb - 'BaseSolvaceRunner')::text
                WHERE "Description" = '{{AIPlugin}}' AND NULLIF("PersonalFields", '') IS NOT NULL;
                """);
        }

        private static string MergeKeys(string plugin, string options, string fieldSettings) => $$"""
            UPDATE prform."PluginConfigurations" c
            SET "Options" = jsonb_set(
                    COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                    '{{options}}'::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                    true)::text
            FROM prform."Plugins" p
            WHERE p."Id" = c."PluginId" AND p."Description" = '{{plugin}}' AND NOT p."IsDeleted";

            UPDATE prform."Plugins"
            SET "FieldSettings" = ('{{fieldSettings}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                "UpdatedAt" = now()
            WHERE "Description" = '{{plugin}}' AND NOT "IsDeleted";
            """;

        private static string RemoveKeys(string plugin, string options) => $$"""
            UPDATE prform."PluginConfigurations" c
            SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY(SELECT jsonb_object_keys('{{options}}'::jsonb)))::text
            FROM prform."Plugins" p
            WHERE p."Id" = c."PluginId" AND p."Description" = '{{plugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

            UPDATE prform."Plugins"
            SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY(SELECT jsonb_object_keys('{{options}}'::jsonb)))::text
            WHERE "Description" = '{{plugin}}';
            """;
    }
}
