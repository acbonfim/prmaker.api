using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0085 (rodada 4): ferramentas do Especialista e tipos próprios de item no "Skills Configurations" — idempotente e só
    /// acrescenta:
    /// <list type="bullet">
    /// <item><c>WorkItemTypes</c> (tipos de item: task, pull-request, review, analysis, fix, implementation, adjustment, closing,
    /// data, ticket, text) se a chave não existir;</item>
    /// <item>tipos de sub-tarefa <c>devops-action</c>, <c>pr-text</c>, <c>sql-query</c> e <c>pr-review</c> que faltarem;</item>
    /// <item>modelos (por <c>key</c>) que faltarem: pr-para-branch, prs-fluxo-producao, promover-revamp,
    /// resolver-conflito-promocao, usar-termo-existente, fechamento-code-fix, validar-em-qa, fechamento-sem-codigo,
    /// script-com-chamado, diagnostico-usuario, novo-resumo, corrigir-sonar e revisar-pr (análise em
    /// <c>features/0085/ferramentas-analise.md</c>);</item>
    /// <item><c>ajuste-pos-qa</c> trocado pela versão corrigida (cherry-pick nas branches dos PRs abertos) SÓ se ainda for
    /// exatamente o semeado pela <c>SeedWorkItems0085</c>;</item>
    /// <item>nos modelos existentes, <c>type</c>/<c>group</c> (e <c>when</c> em pr-release-version/pr-hotfix-version) só se
    /// não tiverem.</item>
    /// </list>
    /// Não mexe em <c>PromotionBranchPattern</c> nem em <c>BranchSuffixByTarget</c> (decisão do admin). JSON inválido do admin:
    /// a linha fica como está. Down tira só o que ainda está igual ao que esta migração pôs.
    /// Marcadores dos modelos: <c>WorkItemTemplateRenderer</c> (API) — o teste confere que todos existem.
    /// </summary>
    public partial class SeedSpecialistTools0085c : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        /// <summary>Tipos de item (o mesmo do <c>WorkItemToolsService.DefaultItemTypesJson</c>).</summary>
        public const string ItemTypes = """
[
  { "key": "task", "label": "Tarefa", "icon": "task_alt", "color": "#607d8b", "description": "Tarefa simples do card." },
  { "key": "pull-request", "label": "Pull request", "icon": "merge", "color": "#3f51b5", "description": "Abrir/promover PRs (branch, cherry-pick e PR)." },
  { "key": "review", "label": "Revisão de PR", "icon": "rate_review", "color": "#7e57c2", "description": "Revisar um PR (de outra pessoa ou do turno contrário)." },
  { "key": "analysis", "label": "Análise", "icon": "manage_search", "color": "#0288d1", "description": "Análise inicial do card (plano de análise)." },
  { "key": "fix", "label": "Correção", "icon": "build", "color": "#e53935", "description": "Correção do bug (plano de correção)." },
  { "key": "implementation", "label": "Implementação", "icon": "construction", "color": "#43a047", "description": "Implementação da User Story (plano de implementação)." },
  { "key": "adjustment", "label": "Ajuste", "icon": "tune", "color": "#fb8c00", "description": "Ajuste depois do QA/revisão (Sonar, texto, retorno)." },
  { "key": "closing", "label": "Fechamento", "icon": "flag", "color": "#00897b", "description": "Fechamento do card: root cause, classificação, resumo e estado." },
  { "key": "data", "label": "Dados/Script", "icon": "storage", "color": "#6d4c41", "description": "Consultas e scripts de dados (com chamado)." },
  { "key": "ticket", "label": "Chamado", "icon": "confirmation_number", "color": "#8e24aa", "description": "Abrir/acompanhar chamado." },
  { "key": "text", "label": "Textos/Resumo", "icon": "description", "color": "#546e7a", "description": "Resumos PT/EN, textos e termos." }
]
""";

        public const string NewKinds = """
[
  { "kind": "devops-action", "label": "Ação no DevOps", "icon": "assignment_turned_in", "executor": "claude",
    "description": "Ações do PRMake no card: root cause, resumo PT/EN, classificação, Remaining, estados configurados.",
    "params": [
      { "key": "action", "label": "Ação", "required": true, "input": "select",
        "options": ["rootcause", "summary", "classify", "zero-remaining", "initial-estimate", "dev-test-in-qa", "ready-for-qa", "test-in-production"] },
      { "key": "value", "label": "Classificação / texto", "input": "textarea" }
    ] },
  { "kind": "pr-text", "label": "Gerar PRMake (descrição + RCA)", "icon": "description", "executor": "claude",
    "description": "Mesmo caminho da gerar-prmake: descrição do PR e RCA pelo prompt configurado, sem abrir PR.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
      { "key": "branch", "label": "Branch", "required": true, "input": "branch" }
    ] },
  { "kind": "sql-query", "label": "Consulta (somente leitura)", "icon": "manage_search", "executor": "claude",
    "description": "SELECT nos hosts do cliente (alias do mapa) / Cognito; resultado nos arquivos do plano.",
    "params": [
      { "key": "database", "label": "Banco / ambiente", "input": "text" },
      { "key": "how", "label": "O que conferir", "required": true, "input": "textarea" }
    ] },
  { "kind": "pr-review", "label": "Revisar PR", "icon": "rate_review", "executor": "claude",
    "description": "Revisa um PR (diff contra a análise do card, riscos e testes) e registra a conclusão; nunca aprova nem mescla.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
      { "key": "pullRequest", "label": "Pull request", "required": true, "input": "pull-request" },
      { "key": "focus", "label": "O que olhar", "input": "textarea" }
    ] }
]
""";

        public const string NewTemplates = """
[
  {
    "key": "pr-para-branch", "type": "pull-request", "group": "PRs e promoção",
    "title": "Abrir PR para uma branch",
    "description": "Branch {branch}{suffix:@target} criada a partir do destino escolhido, cherry-pick dos commits do card e PR para o destino. Destinos sugeridos: {promotionTargets}.",
    "when": { "type": ["bug", "user-story"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
      { "key": "target", "label": "Branch de destino", "input": "target-branch", "options": "{promotionTargets}", "required": true }
    ],
    "tasks": [
      { "title": "Criar {branch}{suffix:@target} a partir de {ask:target}", "kind": "branch",
        "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "source": "{ask:target}" } },
      { "title": "Cherry-pick dos commits do card", "kind": "cherry-pick",
        "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "from": "{branch}", "commits": "card" } },
      { "title": "Abrir PR {branch}{suffix:@target} → {ask:target}", "kind": "pr",
        "params": { "repository": "{ask:repository}", "source": "{branch}{suffix:@target}", "target": "{ask:target}", "draft": "false" } }
    ]
  },
  {
    "key": "prs-fluxo-producao", "type": "pull-request", "group": "PRs e promoção",
    "title": "PRs do fluxo (development e qa)",
    "description": "Branches {branch}{suffix:development} e {branch}{suffix:qa} criadas a partir de development e {qaBranch}, cherry-pick dos commits da {branch} e PRs. Use também para refazer PRs com conflito.",
    "when": { "type": ["bug", "user-story"], "flow": ["producao"], "repoKinds": ["legacy", "revamp-frontend"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
    ],
    "tasks": [
      { "title": "Criar {branch}{suffix:development} a partir de development", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "source": "development" } },
      { "title": "Cherry-pick para {branch}{suffix:development}", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "card" } },
      { "title": "PR {branch}{suffix:development} → development", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:development}", "target": "development" } },
      { "title": "Criar {branch}{suffix:qa} a partir de {qaBranch}", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "source": "{qaBranch}" } },
      { "title": "Cherry-pick para {branch}{suffix:qa}", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "from": "{branch}", "commits": "card" } },
      { "title": "PR {branch}{suffix:qa} → {qaBranch}", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:qa}", "target": "{qaBranch}" } }
    ]
  },
  {
    "key": "promover-revamp", "type": "pull-request", "group": "PRs e promoção",
    "title": "Promover para release-version, edge e master",
    "description": "Perfil do revamp: {branch}{suffix:release-version} e {branch}{suffix:edge} nascem do destino com cherry-pick dos commits do card; master recebe PR direto da {branch} em rascunho.",
    "when": { "type": ["bug", "user-story"], "repoKinds": ["revamp-frontend", "revamp-backend"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
    ],
    "tasks": [
      { "title": "Criar {branch}{suffix:release-version} a partir de release-version", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "source": "release-version" } },
      { "title": "Cherry-pick para {branch}{suffix:release-version}", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "from": "{branch}", "commits": "card" } },
      { "title": "PR {branch}{suffix:release-version} → release-version", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:release-version}", "target": "release-version" } },
      { "title": "Criar {branch}{suffix:edge} a partir de edge", "kind": "branch",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:edge}", "source": "edge" } },
      { "title": "Cherry-pick para {branch}{suffix:edge}", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:edge}", "from": "{branch}", "commits": "card" } },
      { "title": "PR {branch}{suffix:edge} → edge", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}{suffix:edge}", "target": "edge" } },
      { "title": "PR {branch} → master (rascunho)", "kind": "pr",
        "params": { "repository": "{repository}", "source": "{branch}", "target": "master", "draft": "true" } }
    ]
  },
  {
    "key": "resolver-conflito-promocao", "type": "pull-request", "group": "PRs e promoção",
    "title": "Resolver conflito do PR para {ask:target}",
    "description": "O PR para {ask:target} parou em conflito: branch {branch}{suffix:@target} recriada a partir do destino, cherry-pick dos commits do card, conflito resolvido mantendo os dois lados, testes do que mudou e PR (o antigo é fechado).",
    "when": { "type": ["bug", "user-story"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
      { "key": "target", "label": "Destino com conflito", "input": "target-branch", "options": "{promotionTargets}", "required": true }
    ],
    "tasks": [
      { "title": "Criar {branch}{suffix:@target} a partir de {ask:target}", "kind": "branch",
        "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "source": "{ask:target}" } },
      { "title": "Cherry-pick e resolver o conflito", "kind": "cherry-pick",
        "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "from": "{branch}", "commits": "card" } },
      { "title": "Validar os testes do que mudou na branch resolvida", "kind": "validation",
        "params": { "how": "testes dos arquivos com conflito e dos que eles usam" } },
      { "title": "PR {branch}{suffix:@target} → {ask:target}", "kind": "pr",
        "params": { "repository": "{ask:repository}", "source": "{branch}{suffix:@target}", "target": "{ask:target}" } },
      { "title": "Registrar na Timeline que a promoção foi destravada", "kind": "comment",
        "params": { "target": "timeline", "text": "" } }
    ]
  },
  {
    "key": "usar-termo-existente", "type": "text", "group": "Textos",
    "title": "Trocar a mensagem nova por um termo existente",
    "description": "Texto novo precisa de aprovação de Produto: substituir pela mensagem/termo do Multilingual que já existe e levar o ajuste aos PRs abertos.",
    "when": { "type": ["bug", "user-story"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
    ],
    "tasks": [
      { "title": "Achar o termo existente equivalente (Multilingual/legado)", "kind": "sql-query",
        "params": { "database": "multilingual", "how": "" } },
      { "title": "Trocar a mensagem pelo termo existente", "kind": "code",
        "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "Usar o termo existente; não criar texto novo." } },
      { "title": "Validar", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
      { "title": "Levar o ajuste aos PRs abertos ({flowTargets})", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } }
    ]
  },
  {
    "key": "fechamento-code-fix", "type": "closing", "group": "Fechamento",
    "title": "Fechamento: PRMake, root cause, code-fix e Dev Test in QA",
    "description": "Gera a descrição/RCA (sem abrir PR), grava o root cause, classifica code-fix, publica o resumo PT/EN e move para Dev Test in QA quando os PRs estão em QA. Ready for QA só depois da validação em QA.",
    "when": { "type": ["bug"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
    ],
    "tasks": [
      { "title": "Gerar PRMake (descrição e RCA, sem abrir PR)", "kind": "pr-text",
        "params": { "repository": "{repository}", "branch": "{branch}" } },
      { "title": "Root cause no card", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } },
      { "title": "Classificar como code-fix", "kind": "devops-action", "params": { "action": "classify", "value": "code-fix" } },
      { "title": "Resumo PT/EN na discussion", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
      { "title": "Mover para Dev Test in QA", "kind": "devops-action", "params": { "action": "dev-test-in-qa", "value": "" } }
    ]
  },
  {
    "key": "validar-em-qa", "type": "task", "group": "QA",
    "title": "Validar em QA e mover para Ready for QA",
    "description": "Uma pessoa confere a correção no ambiente de QA; aprovada, o card vai para Ready for QA (In Development (done)).",
    "when": { "type": ["bug", "user-story"], "flow": ["producao"] },
    "tasks": [
      { "title": "Validar em QA", "kind": "manual", "params": { "instructions": "" } },
      { "title": "Mover para Ready for QA", "kind": "devops-action", "params": { "action": "ready-for-qa", "value": "" } }
    ]
  },
  {
    "key": "fechamento-sem-codigo", "type": "closing", "group": "Fechamento",
    "title": "Fechamento sem código (orientação ao cliente)",
    "description": "Root cause, classificação escolhida, orientação ao cliente PT/EN na discussion, Remaining zerado e Test in Production.",
    "when": { "type": ["bug"] },
    "ask": [
      { "key": "classification", "label": "Classificação", "input": "select", "required": true,
        "options": ["user-education", "user-education-change-request", "configuration", "script-user-action", "script-environment", "script-defect", "cannot-reproduce", "no-user-feedback", "duplicated", "change-request", "not-mapped-requirement"] }
    ],
    "tasks": [
      { "title": "Root cause no card", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } },
      { "title": "Classificar ({ask:classification})", "kind": "devops-action", "params": { "action": "classify", "value": "{ask:classification}" } },
      { "title": "Orientação ao cliente PT/EN na discussion", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
      { "title": "Zerar o Remaining Work", "kind": "devops-action", "params": { "action": "zero-remaining", "value": "" } },
      { "title": "Mover para Test in Production", "kind": "devops-action", "params": { "action": "test-in-production", "value": "" } }
    ]
  },
  {
    "key": "script-com-chamado", "type": "data", "group": "Dados/Chamado",
    "title": "Script de correção de dados com chamado",
    "description": "Consulta antes (somente leitura), script com rollback nos arquivos do plano, chamado no {ticketSystem} e conferência depois.",
    "when": { "type": ["bug"] },
    "tasks": [
      { "title": "Consultar o estado atual (somente leitura)", "kind": "sql-query", "params": { "database": "", "how": "" } },
      { "title": "Preparar o script com rollback", "kind": "sql-script", "params": { "database": "", "file": "" } },
      { "title": "Abrir o chamado com o script", "kind": "ticket",
        "params": { "system": "{ticketSystem}", "title": "AB#{card} ", "description": "", "priority": "Média" } },
      { "title": "Conferir os dados depois do script", "kind": "sql-query", "params": { "database": "", "how": "" } }
    ]
  },
  {
    "key": "diagnostico-usuario", "type": "data", "group": "Dados/Chamado",
    "title": "Conferir o usuário (banco + Cognito)",
    "description": "Cadastro, vínculo SSO, sites/permissões e conta no pool do Cognito do cliente, só leitura, antes de perguntar ao cliente.",
    "when": { "type": ["bug"] },
    "tasks": [
      { "title": "Cadastro, SSO e permissões no banco do cliente", "kind": "sql-query", "params": { "database": "", "how": "" } },
      { "title": "Conta no Cognito (pool do ambiente)", "kind": "sql-query", "params": { "database": "cognito", "how": "" } },
      { "title": "Registrar o diagnóstico na Timeline", "kind": "comment", "params": { "target": "timeline", "text": "" } }
    ]
  },
  {
    "key": "novo-resumo", "type": "text", "group": "Textos",
    "title": "Novo resumo PT/EN no card",
    "description": "Publica um novo resumo não técnico (PT/EN) com o contexto informado, mantendo os anteriores; opcionalmente atualiza o root cause.",
    "when": { "type": ["bug", "user-story"] },
    "tasks": [
      { "title": "Resumo PT/EN com o contexto novo", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
      { "title": "Atualizar o root cause (se mudou)", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } }
    ]
  },
  {
    "key": "corrigir-sonar", "type": "adjustment", "group": "PRs e promoção",
    "title": "Corrigir crítica do Sonar no PR",
    "description": "Ajustar a branch do PR para passar no quality gate (duplicação, cobertura, code smells) e atualizar os PRs abertos.",
    "when": { "type": ["bug", "user-story"], "repoKinds": ["revamp-backend", "revamp-frontend", "integration-api"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
    ],
    "tasks": [
      { "title": "Corrigir o que o Sonar apontou", "kind": "code",
        "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "" } },
      { "title": "Validar (build e testes)", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
      { "title": "Levar a correção às branches dos PRs abertos", "kind": "cherry-pick",
        "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } }
    ]
  },
  {
    "key": "revisar-pr", "type": "review", "group": "PRs e promoção",
    "title": "Revisar um PR",
    "description": "Revisão do PR escolhido (de outra pessoa ou do turno contrário): diff contra a análise do card, riscos e testes; a conclusão vai para a Timeline. Nunca aprova nem mescla.",
    "when": { "type": ["bug", "user-story"] },
    "ask": [
      { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
      { "key": "pullRequest", "label": "Pull request", "input": "pull-request", "required": true },
      { "key": "focus", "label": "O que olhar", "input": "textarea" }
    ],
    "tasks": [
      { "title": "Revisar o PR {ask:pullRequest}", "kind": "pr-review",
        "params": { "repository": "{ask:repository}", "pullRequest": "{ask:pullRequest}", "focus": "{ask:focus}" } },
      { "title": "Registrar a conclusão da revisão na Timeline", "kind": "comment",
        "params": { "target": "timeline", "text": "" } }
    ]
  }
]
""";

        /// <summary><c>ajuste-pos-qa</c> como a <c>SeedWorkItems0085</c> semeou.</summary>
        public const string OldAdjustment = """
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
}
""";

        /// <summary>F7: o ajuste vai por cherry-pick para as branches dos PRs abertos (os PRs se atualizam, sem PR novo).</summary>
        public const string NewAdjustment = """
{
  "key": "ajuste-pos-qa", "type": "adjustment", "group": "QA",
  "title": "Ajuste após retorno de QA",
  "description": "O QA (ou a revisão) devolveu o card: ajuste na {branch}, validação e o mesmo commit levado às branches dos PRs abertos ({flowTargets}) — os PRs se atualizam, sem PR novo.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true }
  ],
  "tasks": [
    { "title": "Ajustar o código conforme o retorno", "kind": "code",
      "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "" } },
    { "title": "Validar o ajuste", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
    { "title": "Levar o ajuste para {branch}{suffix:development}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } },
    { "title": "Levar o ajuste para {branch}{suffix:qa}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "from": "{branch}", "commits": "" } },
    { "title": "Avisar no card que o ajuste subiu", "kind": "notify", "params": { "message": "" } }
  ]
}
""";

        /// <summary>Modelos que já existiam: tipo do item e grupo (só se não tiverem) e <c>when</c> (só se não tiver).</summary>
        public const string ExistingTypes = """
{"pr-release-version": "pull-request", "pr-hotfix-version": "pull-request", "ajuste-pos-qa": "adjustment", "mover-card": "task", "abrir-chamado": "ticket"}
""";

        public const string ExistingGroups = """
{"pr-release-version": "PRs e promoção", "pr-hotfix-version": "PRs e promoção", "ajuste-pos-qa": "QA", "mover-card": "Fechamento", "abrir-chamado": "Dados/Chamado"}
""";

        /// <summary>F4/F5: hotfix-version nunca em revamp-frontend (4 PRs fechados por isso).</summary>
        public const string ExistingWhen = """
{"pr-hotfix-version": {"repoKinds": ["legacy", "revamp-backend"]}, "pr-release-version": {"repoKinds": ["revamp-frontend", "revamp-backend", "integration-api"]}}
""";

        private const string OldTemplatesLabel =
            "Itens de trabalho: modelos prontos (JSON: [{key, title, description, tasks[{title, kind, params}]}]; marcadores {card}, {branch}, {suffix:destino}, {repository}, {flowTargets})";

        private const string NewTemplatesLabel =
            "Itens de trabalho: modelos prontos / ferramentas do especialista (JSON: [{key, title, description, type?, group?, when?{type[], flow[], repoKinds[], states[]}, ask?[{key, label, input, options?, default?, required?}], tasks[{title, kind, executor?, params}]}]; marcadores {card}, {branch}, {suffix:destino}, {suffix:@ask}, {ask:key}, {repository}, {repoKind}, {flow}, {flowTargets}, {base}, {qaBranch}, {promotionTargets}, {ticketSystem})";

        private const string ItemTypesLabel =
            "Itens de trabalho: tipos de item (JSON: [{key, label, icon, color, description}]; bug/user-story são do card no DevOps, não de item)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; orig jsonb; kinds jsonb; kinds0 jsonb; tpls jsonb; tpls0 jsonb; nk jsonb; nt jsonb;
                    old_adj jsonb := $oa${{OldAdjustment}}$oa$::jsonb;
                    new_adj jsonb := $na${{NewAdjustment}}$na$::jsonb;
                    types jsonb := $ty${{ExistingTypes}}$ty$::jsonb;
                    groups jsonb := $gr${{ExistingGroups}}$gr$::jsonb;
                    whens jsonb := $wh${{ExistingWhen}}$wh$::jsonb;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        orig := opts;

                        -- Tipos de item: só se a chave não existir.
                        IF NOT ((opts -> 0) ? 'WorkItemTypes') THEN
                            opts := jsonb_set(opts, '{0,WorkItemTypes}', to_jsonb(jsonb_pretty($it${{ItemTypes}}$it$::jsonb)), true);
                        END IF;

                        -- Tipos de sub-tarefa: acrescenta os que faltam.
                        BEGIN
                            kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            kinds := NULL;
                        END;
                        IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                            kinds0 := kinds;
                            FOR nk IN SELECT e FROM jsonb_array_elements($nk${{NewKinds}}$nk$::jsonb) e LOOP
                                IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                                    kinds := kinds || jsonb_build_array(nk);
                                END IF;
                            END LOOP;
                            IF kinds IS DISTINCT FROM kinds0 THEN
                                opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                            END IF;
                        END IF;

                        -- Modelos: ajuste-pos-qa corrigido (se ainda for o semeado), type/group/when nos existentes, novos que faltam.
                        BEGIN
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            tpls := NULL;
                        END;
                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                            tpls0 := tpls;
                            SELECT COALESCE(jsonb_agg(
                                CASE
                                    WHEN jsonb_typeof(t.e) <> 'object' THEN t.e
                                    WHEN t.e = old_adj THEN new_adj
                                    ELSE t.e
                                        || CASE WHEN NOT (t.e ? 'type') AND types ? (t.e ->> 'key')
                                                THEN jsonb_build_object('type', types -> (t.e ->> 'key')) ELSE '{}'::jsonb END
                                        || CASE WHEN NOT (t.e ? 'group') AND groups ? (t.e ->> 'key')
                                                THEN jsonb_build_object('group', groups -> (t.e ->> 'key')) ELSE '{}'::jsonb END
                                        || CASE WHEN NOT (t.e ? 'when') AND whens ? (t.e ->> 'key')
                                                THEN jsonb_build_object('when', whens -> (t.e ->> 'key')) ELSE '{}'::jsonb END
                                END ORDER BY t.o), '[]'::jsonb)
                            INTO tpls
                            FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o);

                            FOR nt IN SELECT e FROM jsonb_array_elements($nt${{NewTemplates}}$nt$::jsonb) e LOOP
                                IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x ->> 'key' = nt ->> 'key') THEN
                                    tpls := tpls || jsonb_build_array(nt);
                                END IF;
                            END LOOP;
                            IF tpls IS DISTINCT FROM tpls0 THEN
                                opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                            END IF;
                        END IF;

                        IF opts IS DISTINCT FROM orig THEN
                            UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                        END IF;
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set("FieldSettings"::jsonb, '{WorkItemTemplates,Label}', to_jsonb($nl${{NewTemplatesLabel}}$nl$::text), true)::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted" AND NULLIF("FieldSettings", '') IS NOT NULL
                  AND ("FieldSettings"::jsonb -> 'WorkItemTemplates' ->> 'Label') = $ol${{OldTemplatesLabel}}$ol$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set(COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb), '{WorkItemTypes}',
                        jsonb_build_object('Label', $tl${{ItemTypesLabel}}$tl$::text), true)::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted"
                  AND NOT (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) ? 'WorkItemTypes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tira só o que ainda está exatamente como esta migração deixou.
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; kinds jsonb; tpls jsonb;
                    old_adj jsonb := $oa${{OldAdjustment}}$oa$::jsonb;
                    new_adj jsonb := $na${{NewAdjustment}}$na$::jsonb;
                    new_kinds jsonb := $nk${{NewKinds}}$nk$::jsonb;
                    new_tpls jsonb := $nt${{NewTemplates}}$nt$::jsonb;
                    types jsonb := $ty${{ExistingTypes}}$ty$::jsonb;
                    groups jsonb := $gr${{ExistingGroups}}$gr$::jsonb;
                    whens jsonb := $wh${{ExistingWhen}}$wh$::jsonb;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;

                        IF (opts -> 0) ? 'WorkItemTypes' THEN
                            BEGIN
                                IF (opts -> 0 ->> 'WorkItemTypes')::jsonb = $it${{ItemTypes}}$it$::jsonb THEN
                                    opts := jsonb_set(opts, '{0}', (opts -> 0) - 'WorkItemTypes');
                                END IF;
                            EXCEPTION WHEN others THEN
                                NULL;
                            END;
                        END IF;

                        IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o)
                            WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(new_kinds) n WHERE n = t.e);
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                        END IF;

                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                            SELECT COALESCE(jsonb_agg(
                                CASE
                                    WHEN jsonb_typeof(t.e) <> 'object' THEN t.e
                                    WHEN t.e = new_adj THEN old_adj
                                    ELSE t.e
                                        - CASE WHEN types ? (t.e ->> 'key') AND t.e -> 'type' = types -> (t.e ->> 'key') THEN 'type' ELSE '' END
                                        - CASE WHEN groups ? (t.e ->> 'key') AND t.e -> 'group' = groups -> (t.e ->> 'key') THEN 'group' ELSE '' END
                                        - CASE WHEN whens ? (t.e ->> 'key') AND t.e -> 'when' = whens -> (t.e ->> 'key') THEN 'when' ELSE '' END
                                END ORDER BY t.o), '[]'::jsonb)
                            INTO tpls
                            FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o)
                            WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(new_tpls) n WHERE n = t.e);
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                        END IF;

                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set("FieldSettings"::jsonb, '{WorkItemTemplates,Label}', to_jsonb($ol${{OldTemplatesLabel}}$ol$::text), true)::text
                WHERE "Description" = '{{SkillsPlugin}}' AND NULLIF("FieldSettings", '') IS NOT NULL
                  AND ("FieldSettings"::jsonb -> 'WorkItemTemplates' ->> 'Label') = $nl${{NewTemplatesLabel}}$nl$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = ("FieldSettings"::jsonb - 'WorkItemTypes')::text
                WHERE "Description" = '{{SkillsPlugin}}' AND NULLIF("FieldSettings", '') IS NOT NULL
                  AND ("FieldSettings"::jsonb -> 'WorkItemTypes' ->> 'Label') = $tl${{ItemTypesLabel}}$tl$;
                """);
        }
    }
}
