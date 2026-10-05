using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0064: traduções do glossário com reserva — sem a credencial de produção do Multilingual, a skill usa as de
    /// "fallbackCredentials" (padrão: o Multilingual de dev da máquina, ~/.claude/postgres-credentials-dev.json). Acrescenta
    /// a chave no JSON de ReverseEngineeringTranslations só se faltar (e só se o valor for um objeto JSON válido).
    /// </summary>
    public partial class SeedTranslationsFallback : Migration
    {
        private const string Expr = """(c."Options"::jsonb -> 0 ->> 'ReverseEngineeringTranslations')""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringTranslations}',
                        to_jsonb(({{Expr}}::jsonb || '{"fallbackCredentials": ["~/.claude/postgres-credentials-dev.json"]}'::jsonb)::text))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = 'Skills Configurations' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL AND {{Expr}} IS JSON OBJECT
                  AND NOT ({{Expr}}::jsonb ? 'fallbackCredentials');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringTranslations}',
                        to_jsonb(({{Expr}}::jsonb - 'fallbackCredentials')::text))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = 'Skills Configurations'
                  AND NULLIF(c."Options", '') IS NOT NULL AND {{Expr}} IS JSON OBJECT;
                """);
        }
    }
}
