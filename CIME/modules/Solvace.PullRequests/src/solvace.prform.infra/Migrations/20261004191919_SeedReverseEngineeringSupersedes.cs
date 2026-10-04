using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0054: a engenharia reversa vira a fonte de cada módulo — mapa "seção antiga da Base Solvace → documentos da
    /// engenharia reversa que a substituem" (todos publicados = a antiga sai do espelho/busca/MCP) e a visão prática
    /// (<c>pratica</c>) passa a ser exigida para o módulo contar como completo. Chave nova só se faltar; na lista de
    /// exigidos, só acrescenta <c>pratica</c> (o que o admin editou fica).
    /// </summary>
    public partial class SeedReverseEngineeringSupersedes : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringSupersedes": "{\"visao-geral\": [\"visao\", \"arquitetura\"], \"modulos\": [\"funcional\", \"uiux\"], \"dados\": [\"arquitetura\"], \"integracoes\": [\"arquitetura\"], \"infra\": [\"arquitetura\"], \"autenticacao\": [\"arquitetura\"], \"jobs\": [\"arquitetura\"], \"regras-de-negocio\": [\"funcional\"], \"operacao\": [\"funcional\"], \"armadilhas\": [\"@armadilhas\"], \"guia-*\": [\"pratica\"]}"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringSupersedes": {"Label": "Engenharia reversa: seção antiga da Base Solvace → documentos que a substituem (JSON; todos publicados = a antiga sai do espelho, da busca e do MCP; guia-* = seções do Guia; @armadilhas = armadilhas migradas)"}}
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

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringRequiredDocs}',
                        to_jsonb((c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') || ',pratica'))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') NOT LIKE '%pratica%';

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
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'ReverseEngineeringSupersedes')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringRequiredDocs}',
                        to_jsonb(replace(c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs', ',pratica', '')))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'ReverseEngineeringSupersedes')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
