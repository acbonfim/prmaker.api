using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0097: documentos exigidos da engenharia reversa passam a ser <c>funcional,arquitetura,spec-arquitetura</c> — design e
    /// visão foram descontinuados e a visão prática é sob demanda (sugerida pelas perguntas sem resposta do Pergunte). Troca
    /// a lista gravada no "Skills Configurations" (sem valor gravado vale o padrão do código, que já é o novo). Down volta
    /// à lista da 0054 só onde o valor ainda está exatamente como esta migração pôs. A seção antiga <c>visao-geral</c> passa a ser
    /// substituída por funcional + arquitetura (antes: visão + arquitetura — módulo novo nunca a substituiria).
    /// </summary>
    public partial class ReverseRequiredDocs0097 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";
        private const string Required = "funcional,arquitetura,spec-arquitetura";
        private const string Before = "funcional,arquitetura,visao,spec-arquitetura,design,pratica";
        private const string OverviewBefore = "\"visao-geral\": [\"visao\", \"arquitetura\"]";
        private const string OverviewAfter = "\"visao-geral\": [\"funcional\", \"arquitetura\"]";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringRequiredDocs}', to_jsonb('{{Required}}'::text))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND jsonb_typeof(c."Options"::jsonb) = 'array'
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') <> '{{Required}}';
                """);
            Supersedes(migrationBuilder, OverviewBefore, OverviewAfter);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringRequiredDocs}', to_jsonb('{{Before}}'::text))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}'
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND jsonb_typeof(c."Options"::jsonb) = 'array'
                  AND (c."Options"::jsonb -> 0 ->> 'ReverseEngineeringRequiredDocs') = '{{Required}}';
                """);
            Supersedes(migrationBuilder, OverviewAfter, OverviewBefore);
        }

        /// <summary>Troca o mapa da <c>visao-geral</c> dentro do JSON (texto) gravado em <c>ReverseEngineeringSupersedes</c>.</summary>
        private static void Supersedes(MigrationBuilder migrationBuilder, string from, string to)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringSupersedes}',
                        to_jsonb(replace(c."Options"::jsonb -> 0 ->> 'ReverseEngineeringSupersedes', '{{from}}', '{{to}}')))::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}'
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND jsonb_typeof(c."Options"::jsonb) = 'array'
                  AND strpos(c."Options"::jsonb -> 0 ->> 'ReverseEngineeringSupersedes', '{{from}}') > 0;
                """);
        }
    }
}
