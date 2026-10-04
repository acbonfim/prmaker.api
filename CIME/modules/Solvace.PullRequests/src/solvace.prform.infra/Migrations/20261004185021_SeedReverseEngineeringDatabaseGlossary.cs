using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0053: banco de referência da engenharia reversa (a DEMO — global e os locais, lidos direto do servidor, somente
    /// leitura) e as palavras genéricas de interface que ficam fora da cobertura de termos do glossário. Chaves no "Skills
    /// Configurations"; só as que faltam — valores já editados pelo admin são preservados.
    /// </summary>
    public partial class SeedReverseEngineeringDatabaseGlossary : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string SkillsOptions = """
{"ReverseEngineeringReferenceDatabase": "{\"environment\": \"DEMO\", \"host\": \"prod\", \"global\": \"DB_DEMO_PRD_GLOBAL\", \"locals\": [\"DB_DEMO_PRD_LOCAL_CTB\", \"DB_DEMO_PRD_LOCAL_GLB\", \"DB_DEMO_PRD_LOCAL_PAR\"]}", "ReverseEngineeringGlossaryExclusions": "[\"Salvar\", \"Cancelar\", \"Filtrar\", \"Filtro\", \"Data\", \"Buscar\", \"Pesquisar\", \"Editar\", \"Excluir\", \"Remover\", \"Adicionar\", \"Novo\", \"Nova\", \"Voltar\", \"Fechar\", \"Sim\", \"Não\", \"OK\", \"Confirmar\", \"Limpar\", \"Exportar\", \"Imprimir\", \"Detalhes\", \"Ações\", \"Opções\", \"Selecione\", \"Todos\", \"Todas\", \"Nenhum\", \"Carregando\", \"Erro\", \"Sucesso\", \"Atenção\", \"Aviso\", \"Enviar\", \"Anexar\", \"Visualizar\", \"Copiar\", \"Save\", \"Cancel\", \"Filter\", \"Date\", \"Search\", \"Edit\", \"Delete\", \"Remove\", \"Add\", \"New\", \"Back\", \"Close\", \"Yes\", \"No\", \"Confirm\", \"Clear\", \"Export\", \"Print\", \"Details\", \"Actions\", \"Options\", \"Select\", \"All\", \"None\", \"Loading\", \"Error\", \"Success\", \"Warning\", \"Send\"]"}
""";
        private const string SkillsFieldSettings = """
{"ReverseEngineeringReferenceDatabase": {"Label": "Engenharia reversa: banco de referência lido direto (JSON: environment, host = alias das credenciais da máquina, global, locals) — a DEMO, que reflete produção"}, "ReverseEngineeringGlossaryExclusions": {"Label": "Engenharia reversa: palavras genéricas de interface fora da cobertura de termos do glossário (JSON: lista)"}}
""";

        private static readonly string[] Keys = ["ReverseEngineeringReferenceDatabase", "ReverseEngineeringGlossaryExclusions"];

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
