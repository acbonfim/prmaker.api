using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0063: a análise ganha a etapa própria de consulta à Base Solvace (consultar-base), antes do código, e a trava da
    /// engenharia reversa passa a valer nela (lista: consultar-base,investigar-codigo — planos antigos sem a etapa nova
    /// continuam travando na investigação). As etapas padrão do plano de análise vão para a configuração
    /// (AnalysisDefaultSteps), não mais fixas na skill. Só grava a chave nova se faltar; a trava só muda se ainda estiver
    /// no valor semeado pela 0052 (investigar-codigo) — valor que o admin mudou fica.
    /// </summary>
    public partial class SeedAnalysisBaseSteps : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";
        private const string OldGate = "investigar-codigo";
        private const string NewGate = "consultar-base,investigar-codigo";

        private static readonly object[] Steps =
        [
            new { key = "identificar-card", title = "Identificar o card", description = "Descobrir o número do card (branch hotfix/bugfix ou informado) e criar a pasta de artefatos." },
            new { key = "coletar-dados", title = "Ler o card", description = "Buscar título, estado, tipo e repro steps do card no PRMake/Azure DevOps." },
            new { key = "consultar-base", title = "Consultar a Base Solvace", description = "Engenharia reversa do módulo (os itens que o contexto trouxe; prmake_base_search e prmake_base_get com o card, termos em português e em inglês), armadilhas ligadas aos itens e Knowledge Center. Conclui citando os itens usados (módulo#ID) ou “lacuna: o que a base não cobre”." },
            new { key = "investigar-codigo", title = "Confirmar no código", description = "Confirmar no código os “Onde:” dos itens usados e reconstruir o fluxo até o erro. Arquivo, função ou tabela que nenhum item cita: buscar na base antes de abrir; o que só o código mostrou vira lacuna registrada." },
            new { key = "consultar-ambiente", title = "Consultar dados e ambiente", description = "Quando necessário: Cognito (usuário/ambiente) e SQL Server somente leitura." },
            new { key = "causa-raiz", title = "Levantar a causa raiz", description = "Hipóteses priorizadas e pontos suspeitos (caminho:linha); o que é confirmado e o que é hipótese. Hipótese nova ou outro módulo envolvido: volta à base antes do código." },
            new { key = "montar-analise", title = "Montar a análise e os scripts", description = "Escrever a análise em markdown (com a engenharia reversa usada e as lacunas registradas) e, se houver, os scripts (ex.: SQL de correção e rollback)." },
            new { key = "publicar", title = "Publicar na timeline", description = "Postar a análise completa na Timeline do card." },
            new { key = "propor-solucoes", title = "Propor soluções e decidir com você", kind = "question", description = "Apresentar as opções de solução (prós, contras, riscos e o impacto na base de cada ponto de alteração — prmake_base_impact) e perguntar o que for preciso — responda no PRMake ou no Claude. Com as respostas, nasce o plano de correção." }
        ];

        private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var options = JsonSerializer.Serialize(new Dictionary<string, string> { ["AnalysisDefaultSteps"] = JsonSerializer.Serialize(Steps, Json) }, Json);
            var fields = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["AnalysisDefaultSteps"] = new { Label = "Análise (analisar-bug): etapas padrão do plano de análise (JSON: [{key, title, description, kind?}]) — a skill usa quando o plano nasce" }
            }, Json);
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        {{Literal(options)}}::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringGateStep}', '"{{NewGate}}"'::jsonb)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL AND c."Options"::jsonb -> 0 ->> 'ReverseEngineeringGateStep' = '{{OldGate}}';

                UPDATE prform."Plugins"
                SET "FieldSettings" = ({{Literal(fields)}}::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'AnalysisDefaultSteps')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,ReverseEngineeringGateStep}', '"{{OldGate}}"'::jsonb)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL
                  AND c."Options"::jsonb -> 0 ->> 'ReverseEngineeringGateStep' = '{{NewGate}}';

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'AnalysisDefaultSteps')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
