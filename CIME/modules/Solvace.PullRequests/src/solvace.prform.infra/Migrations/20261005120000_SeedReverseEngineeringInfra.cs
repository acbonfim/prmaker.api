using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0058: contas/perfis/regiões da AWS que a etapa OPCIONAL de infra da engenharia reversa consulta (AWS CLI, somente
    /// leitura). Sem segredo e sem conta fixa — a conta (id) manda e o perfil do CLI é achado na máquina. Só grava a chave se faltar.
    /// </summary>
    public partial class SeedReverseEngineeringInfra : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringInfra": "{\"accounts\": [{\"id\": \"367983645102\", \"label\": \"Solvace (revamp)\"}], \"regions\": [\"us-east-1\"], \"readOnly\": true}"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringInfra": {"Label": "Engenharia reversa: etapa opcional de infra (JSON: accounts = [{id, label}] — o perfil do AWS CLI é achado pela conta, regions, readOnly) — nada de segredo"}}
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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'ReverseEngineeringInfra')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'ReverseEngineeringInfra')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
