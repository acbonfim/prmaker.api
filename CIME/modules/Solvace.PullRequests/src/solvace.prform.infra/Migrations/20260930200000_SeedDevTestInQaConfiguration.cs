using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Ação "Dev Test in QA" (dev validando a correção em QA, antes do Ready for QA): estado e coluna do board no
    /// "AI Configurations" — só as chaves que faltam; valores já editados pelo admin são preservados.
    /// </summary>
    public partial class SeedDevTestInQaConfiguration : Migration
    {
        private const string AIPlugin = "AI Configurations";

        private const string AIOptions = """
{"BugDevTestInQaState": "In Development (doing)", "BugDevTestInQaColumn": "Dev Test in QA"}
""";
        private const string AIFieldSettings = """
{"BugDevTestInQaState": {"Label": "Dev Test in QA: estado do card (dev validando em QA)"}, "BugDevTestInQaColumn": {"Label": "Dev Test in QA: coluna do board (vazio = só o estado)"}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        '{{AIOptions}}'::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{AIPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."Plugins"
                SET "FieldSettings" = ('{{AIFieldSettings}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{AIPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['BugDevTestInQaState', 'BugDevTestInQaColumn'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{AIPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['BugDevTestInQaState', 'BugDevTestInQaColumn'])::text
                WHERE "Description" = '{{AIPlugin}}';
                """);
        }
    }
}
