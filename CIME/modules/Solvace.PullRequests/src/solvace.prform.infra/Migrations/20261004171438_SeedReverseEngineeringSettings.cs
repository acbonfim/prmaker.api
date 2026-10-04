using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0052: engenharia reversa por módulo — quem aprova/publica, documentos exigidos para o módulo contar como completo,
    /// etapa da analisar-bug que só conclui consultando/citando a base, cobertura mínima do inventário e modelos que
    /// substituem os do código. Chaves no "Skills Configurations"; só as que faltam — valores já editados pelo admin
    /// são preservados.
    /// </summary>
    public partial class SeedReverseEngineeringSettings : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringApproverRoles": "admin,gestor", "ReverseEngineeringRequiredDocs": "funcional,arquitetura,visao,spec-arquitetura,design", "ReverseEngineeringGateStep": "investigar-codigo", "ReverseEngineeringMinCoverage": "0.9", "ReverseEngineeringTemplates": "{}"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringApproverRoles": {"Label": "Engenharia reversa: papéis que aprovam e publicam (separados por vírgula)"}, "ReverseEngineeringRequiredDocs": {"Label": "Engenharia reversa: documentos exigidos para o módulo contar como completo (funcional, arquitetura, uiux, visao, spec-arquitetura, design)"}, "ReverseEngineeringGateStep": {"Label": "Engenharia reversa: etapa da análise que só conclui consultando/citando a base quando o módulo está completo (vazio desliga)"}, "ReverseEngineeringMinCoverage": {"Label": "Engenharia reversa: cobertura mínima do inventário do código (0 a 1) — abaixo, o revisor vê o aviso"}, "ReverseEngineeringTemplates": {"Label": "Engenharia reversa: modelos que substituem os do código, em JSON ({\"funcional\": \"markdown\"}; {} = os do código)"}}
""";

        private static readonly string[] Keys =
        [
            "ReverseEngineeringApproverRoles", "ReverseEngineeringRequiredDocs", "ReverseEngineeringGateStep", "ReverseEngineeringMinCoverage",
            "ReverseEngineeringTemplates"
        ];

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
            var keys = string.Join(", ", Keys.Select(k => $"'{k}'"));
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY[{{keys}}])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY[{{keys}}])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
