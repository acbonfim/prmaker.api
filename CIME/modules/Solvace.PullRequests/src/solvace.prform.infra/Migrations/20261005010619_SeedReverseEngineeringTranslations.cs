using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0056: de onde a engenharia reversa lê as traduções do glossário — o Multilingual do revamp (Aurora PostgreSQL,
    /// schema multilingual; a chave do termo é o texto em português, como o TERM_NAME do legado). Sem segredo: a
    /// credencial fica só na máquina (~/.claude/multilingual-credentials.json, o JSON do secret multilingual/production).
    /// Só grava a chave se faltar.
    /// </summary>
    public partial class SeedReverseEngineeringTranslations : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringTranslations": "{\"source\": \"multilingual\", \"environment\": \"prod\", \"schema\": \"multilingual\", \"credentials\": \"~/.claude/multilingual-credentials.json\", \"secretId\": \"multilingual/production\", \"languages\": {\"pt\": \"pt-BR\", \"en\": \"en-US\", \"es\": \"es-ES\"}}"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringTranslations": {"Label": "Engenharia reversa: fonte das traduções do glossário (JSON: source=multilingual, environment = bloco do arquivo de credenciais da máquina, schema, credentials, secretId, languages) — o Multilingual do revamp"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'ReverseEngineeringTranslations')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'ReverseEngineeringTranslations')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
