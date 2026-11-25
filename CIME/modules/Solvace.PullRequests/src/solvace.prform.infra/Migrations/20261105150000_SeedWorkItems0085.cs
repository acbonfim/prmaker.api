using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0085: itens de trabalho no "Skills Configurations" — sufixo da branch por destino (<c>release-version</c> → <c>-rv</c>),
    /// tipos de sub-tarefa, modelos prontos, vagas por máquina e a média de horas por tipo de card (bug/US, partindo da
    /// média geral AssistantAverageHours) e as etapas padrão da análise de User Story (UsAnalysisDefaultSteps). Só as chaves que faltam (o valor do admin fica). O padrão
    /// do nome da branch da promoção passa a usar o sufixo (<c>{branch}{suffix}</c>) — só se ainda for exatamente o semeado.
    /// </summary>
    public partial class SeedWorkItems0085 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string Suffixes = """
{"development": "-dev", "qa": "-qa", "release-version": "-rv", "hotfix-version": "-hv", "edge": "-edge"}
""";

        private const string TaskKinds = """
[
  {
    "kind": "branch", "label": "Criar branch", "icon": "call_split", "executor": "claude",
    "description": "Cria a branch nova a partir da base (nunca reaproveita uma existente).",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true },
      { "key": "branch", "label": "Branch nova", "required": true },
      { "key": "source", "label": "A partir de", "required": true }
    ],
    "undo": { "action": "delete-branch", "label": "Apagar a branch criada" }
  },
  {
    "kind": "cherry-pick", "label": "Cherry-pick dos commits", "icon": "alt_route", "executor": "claude",
    "description": "Leva os commits do card (ou os informados) para a branch, sem merge e sem force-push.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true },
      { "key": "branch", "label": "Branch de destino do cherry-pick", "required": true },
      { "key": "from", "label": "Branch de onde vêm os commits" },
      { "key": "commits", "label": "Commits (\"card\" = os do card)" }
    ],
    "undo": { "action": "revert-commits", "label": "Reverter os commits aplicados" }
  },
  {
    "kind": "code", "label": "Alterar código", "icon": "code", "executor": "claude",
    "description": "Ajuste de código na branch, com commit no padrão do PRMake.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true },
      { "key": "branch", "label": "Branch", "required": true },
      { "key": "instructions", "label": "O que mudar" }
    ],
    "undo": { "action": "revert-commits", "label": "Reverter os commits da sub-tarefa" }
  },
  {
    "kind": "pr", "label": "Abrir pull request", "icon": "merge", "executor": "claude",
    "description": "Abre o PR pelo PRMake (nunca mescla).",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true },
      { "key": "source", "label": "Branch de origem", "required": true },
      { "key": "target", "label": "Branch de destino", "required": true },
      { "key": "draft", "label": "Rascunho (true/false)" }
    ],
    "undo": { "action": "close-pr", "label": "Fechar o PR aberto" }
  },
  {
    "kind": "devops-state", "label": "Mover o card no DevOps", "icon": "swap_horiz", "executor": "claude",
    "description": "Muda o estado do card no Azure DevOps pelo PRMake.",
    "params": [
      { "key": "state", "label": "Novo estado", "required": true }
    ],
    "undo": { "action": "devops-state", "label": "Voltar ao estado anterior" }
  },
  {
    "kind": "sql-script", "label": "Script SQL", "icon": "storage", "executor": "user",
    "description": "Script de alteração gravado nos arquivos do plano; quem roda é uma pessoa (chamado).",
    "params": [
      { "key": "database", "label": "Banco / ambiente" },
      { "key": "file", "label": "Arquivo do script" }
    ]
  },
  {
    "kind": "validation", "label": "Validar", "icon": "fact_check", "executor": "claude",
    "description": "Build/testes do que mudou ou conferência combinada.",
    "params": [
      { "key": "how", "label": "Como validar" }
    ]
  },
  {
    "kind": "comment", "label": "Comentário no card", "icon": "comment", "executor": "claude",
    "description": "Comentário na Timeline do PRMake ou na discussion do DevOps.",
    "params": [
      { "key": "target", "label": "Onde (timeline/devops)" },
      { "key": "text", "label": "Texto" }
    ]
  },
  {
    "kind": "manual", "label": "Tarefa manual", "icon": "person", "executor": "user",
    "description": "Feita por uma pessoa; o item espera ela concluir.",
    "params": [
      { "key": "instructions", "label": "O que fazer" }
    ]
  }
]
""";

        private const string Templates = """
[
  {
    "key": "pr-release-version",
    "title": "PR da master para a release-version",
    "description": "Leva a correção do card para a release-version: branch {branch}{suffix:release-version} a partir da release-version, cherry-pick dos commits do card e PR para a release-version.",
    "tasks": [
      { "title": "Criar a branch {branch}{suffix:release-version} a partir de release-version", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "source": "release-version" } },
      { "title": "Cherry-pick dos commits do card", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "from": "{branch}", "commits": "card" } },
      { "title": "Abrir PR {branch}{suffix:release-version} → release-version", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:release-version}", "target": "release-version" } }
    ]
  },
  {
    "key": "pr-hotfix-version",
    "title": "PR da master para a hotfix-version",
    "description": "Leva a correção do card para a hotfix-version: branch {branch}{suffix:hotfix-version} a partir da hotfix-version, cherry-pick dos commits do card e PR para a hotfix-version.",
    "tasks": [
      { "title": "Criar a branch {branch}{suffix:hotfix-version} a partir de hotfix-version", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:hotfix-version}", "source": "hotfix-version" } },
      { "title": "Cherry-pick dos commits do card", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:hotfix-version}", "from": "{branch}", "commits": "card" } },
      { "title": "Abrir PR {branch}{suffix:hotfix-version} → hotfix-version", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:hotfix-version}", "target": "hotfix-version" } }
    ]
  },
  {
    "key": "ajuste-pos-qa",
    "title": "Ajuste após retorno de QA",
    "description": "O QA devolveu o card: ajuste na branch {branch}, validação e PRs do fluxo do card ({flowTargets}).",
    "tasks": [
      { "title": "Ajustar o código conforme o retorno do QA", "kind": "code",
        "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "" } },
      { "title": "Validar o ajuste", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
      { "title": "Abrir/atualizar os PRs do fluxo ({flowTargets})", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}", "target": "{flowTargets}" } }
    ]
  },
  {
    "key": "mover-card",
    "title": "Mover o card no DevOps",
    "description": "Muda o estado do card {card} no Azure DevOps.",
    "tasks": [
      { "title": "Mover o card para o novo estado", "kind": "devops-state", "params": { "state": "" } }
    ]
  }
]
""";

        /// <summary>Etapas padrão da análise de User Story (a mesma reserva do prmake-plan.sh).</summary>
        private const string UsAnalysisSteps = """
[
 {"key":"identificar-card","title":"Identificar a US","description":"Descobrir o número do card (branch feature/us ou informado) e criar a pasta de artefatos."},
 {"key":"coletar-dados","title":"Ler a US","description":"Título, estado, descrição, critérios de aceite, discussion e anexos da User Story no PRMake/Azure DevOps."},
 {"key":"entender-us","title":"Entender a US","description":"Objetivo, quem usa, regras e critérios de aceite numerados (CA-1, CA-2...); o que está claro e o que é dúvida."},
 {"key":"consultar-base","title":"Consultar a Base Solvace","description":"Engenharia reversa do módulo (prmake_base_search/get com o card, termos em português e inglês), armadilhas e Knowledge Center. Conclui citando os itens usados (módulo#ID) ou “lacuna: o que a base não cobre”."},
 {"key":"mapear-codigo","title":"Mapear no código onde a US entra","description":"Confirmar no código os “Onde:” dos itens usados e levantar os pontos de alteração (telas, APIs, tabelas) por repositório."},
 {"key":"desenhar-solucao","title":"Desenhar a solução","description":"Opções de solução com o impacto na base (prmake_base_impact), os critérios de aceite que cada uma cobre, riscos e esforço."},
 {"key":"montar-analise","title":"Montar a análise da US","description":"Escrever a análise em markdown: entendimento, critérios de aceite, solução proposta, impacto e entregas."},
 {"key":"publicar","title":"Publicar na timeline","description":"Postar a análise completa na Timeline do card."},
 {"key":"propor-solucoes","title":"Propor a solução e decidir com você","kind":"question","description":"Apresentar as opções (prós, contras, riscos, entregas) e perguntar o que for preciso — responda no PRMake ou no Claude. Com as respostas, nasce o plano de implementação e os itens de trabalho."}
]
""";

        private const string FieldSettings = """
{"BranchSuffixByTarget": {"Label": "Itens de trabalho: sufixo da branch por destino (JSON: destino → sufixo; ex.: release-version → -rv = hotfix/77434-rv)"}, "WorkItemTaskKinds": {"Label": "Itens de trabalho: tipos de sub-tarefa (JSON: [{kind, label, icon, executor, params[{key, label}], undo}])"}, "WorkItemTemplates": {"Label": "Itens de trabalho: modelos prontos (JSON: [{key, title, description, tasks[{title, kind, params}]}]; marcadores {card}, {branch}, {suffix:destino}, {repository}, {flowTargets})"}, "WorkItemSlots": {"Label": "Itens de trabalho: quantos rodam ao mesmo tempo por máquina (fora do limite de execuções)"}, "AssistantAverageHoursBug": {"Label": "Média de horas por bug"}, "AssistantAverageHoursUserStory": {"Label": "Média de horas por US"}, "UsAnalysisDefaultSteps": {"Label": "Etapas padrão do plano de análise de User Story (analisar-us; JSON: [{key, title, description, kind?}])"}}
""";

        private const string PromotionLabel =
            "Promoção em lote: nome da branch do cherry-pick ({branch} = branch principal do card, {suffix} = sufixo do destino no BranchSuffixByTarget, {target} = destino)";

        private const string OldPromotionLabel =
            "Promoção em lote: nome da branch do cherry-pick ({branch} = branch principal do card, {target} = destino)";

        private const string OldPattern = "{branch}-{target}";
        private const string NewPattern = "{branch}{suffix}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        jsonb_build_object(
                            'BranchSuffixByTarget', $suf${{Suffixes}}$suf$::text,
                            'WorkItemTaskKinds', $kinds${{TaskKinds}}$kinds$::text,
                            'WorkItemTemplates', $tpl${{Templates}}$tpl$::text,
                            'WorkItemSlots', '2',
                            'UsAnalysisDefaultSteps', $us${{UsAnalysisSteps}}$us$::text,
                            'AssistantAverageHoursBug', COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0 ->> 'AssistantAverageHours', ''),
                            'AssistantAverageHoursUserStory', COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0 ->> 'AssistantAverageHours', ''))
                            || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,PromotionBranchPattern}', to_jsonb($np${{NewPattern}}$np$::text), false)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'PromotionBranchPattern') = $op${{OldPattern}}$op$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set(
                        $fs${{FieldSettings}}$fs$::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb),
                        '{PromotionBranchPattern}', jsonb_build_object('Label', $pl${{PromotionLabel}}$pl$::text), true)::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,PromotionBranchPattern}', to_jsonb($op${{OldPattern}}$op$::text), false)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}'
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'PromotionBranchPattern') = $np${{NewPattern}}$np$;

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}',
                        (c."Options"::jsonb -> 0) - ARRAY['BranchSuffixByTarget', 'WorkItemTaskKinds', 'WorkItemTemplates', 'WorkItemSlots', 'AssistantAverageHoursBug', 'AssistantAverageHoursUserStory', 'UsAnalysisDefaultSteps'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set(
                        COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb)
                            - ARRAY['BranchSuffixByTarget', 'WorkItemTaskKinds', 'WorkItemTemplates', 'WorkItemSlots', 'AssistantAverageHoursBug', 'AssistantAverageHoursUserStory', 'UsAnalysisDefaultSteps'],
                        '{PromotionBranchPattern}', jsonb_build_object('Label', $pl${{OldPromotionLabel}}$pl$::text), true)::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
