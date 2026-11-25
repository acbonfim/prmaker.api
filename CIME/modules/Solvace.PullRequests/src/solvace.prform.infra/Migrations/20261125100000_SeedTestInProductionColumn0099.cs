using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0099: "Test in production" muda estado E coluna do board (estado Test QA na coluna QA Done) — chave
    /// <c>BugTestInProductionColumn</c> no "AI Configurations". Só acrescenta a chave que falta; valores já editados ficam.
    /// </summary>
    public partial class SeedTestInProductionColumn0099 : Migration
    {
        private const string AIPlugin = "AI Configurations";

        private const string AIOptions = """
{"BugTestInProductionColumn": "QA Done"}
""";
        private const string AIFieldSettings = """
{"BugTestInProductionColumn": {"Label": "Coluna do board ao mover para Test in production (Bug)", "Optional": true, "UseGlobalDefault": true, "Help": "Estado e coluna são coisas diferentes no DevOps (ex.: estado Test QA na coluna QA Done). Vazio = só muda o estado."}}
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

                -- Quem preenche o estado (campo pessoal) preenche também a coluna.
                UPDATE prform."Plugins"
                SET "PersonalFields" = ("PersonalFields"::jsonb || '["BugTestInProductionColumn"]'::jsonb)::text
                WHERE "Description" = '{{AIPlugin}}' AND NOT "IsDeleted" AND NULLIF("PersonalFields", '') IS NOT NULL
                  AND "PersonalFields"::jsonb ? 'BugTestInProductionState' AND NOT "PersonalFields"::jsonb ? 'BugTestInProductionColumn';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'BugTestInProductionColumn')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{AIPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'BugTestInProductionColumn')::text
                WHERE "Description" = '{{AIPlugin}}';

                UPDATE prform."Plugins"
                SET "PersonalFields" = ("PersonalFields"::jsonb - 'BugTestInProductionColumn')::text
                WHERE "Description" = '{{AIPlugin}}' AND NULLIF("PersonalFields", '') IS NOT NULL;
                """);
        }
    }
}
