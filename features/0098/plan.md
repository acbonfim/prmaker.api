# Plano 0098 — Governança do PRMake

Branches `feature/0098` em `prform.api-0098` (back + skills + executor) e `prform-app-0098` (front), a partir da
origin/master. Três frentes (B, S, F) em paralelo depois do B1; Q no fim. Os contratos abaixo são a fonte da verdade
entre as frentes.

## Decisões

- **Módulo novo `Solvace.Governance`** (domain/application/infra, schema `governance`, `GovernanceContext`). Regras,
  hooks, configurações e a trilha de eventos ficam nele. A composição com os dados que já existem (planos, etapas,
  itens, sub-tarefas, fila do executor, consumo de IA) fica no host (`solvace.prform.api/Governance/`), que já enxerga
  todos os contextos. Nada é duplicado: marcos de card vêm das datas que planos/itens já gravam.
- **Uma trilha só** (`governance."GovernanceEvents"`): toda gravação HTTP (POST/PUT/PATCH/DELETE em `api/v1`), toda
  chamada de ferramenta do MCP e todo evento de hook do Claude Code (terminal ou executor) vira um evento com quem,
  origem, card, plano/fase, item/sub-tarefa, ação, início, fim, duração, resultado e regras disparadas. Leituras (GET)
  não entram (custo; a 0079 já mede navegação). Eventos são gravados **fora da requisição**: fila em memória limitada
  (`Channel`, 5 000, descarta o mais antigo) + `BackgroundService` que grava em lote a cada 2 s — a API (1–2 GiB, banco
  em outra região) não ganha ida ao banco por requisição.
- **Motor de regras único** (`GovernanceRuleEngine`, puro, testável) avalia um `GovernanceRequest` e devolve a decisão.
  É chamado: (1) no middleware HTTP antes do endpoint; (2) no filtro de CallTool do MCP; (3) pelo
  `POST Governance/evaluate` que o hook `PreToolUse` do Claude Code chama (terminal e executor).
- **Efeitos**: `monitor` (só registra), `warn` (deixa passar e devolve aviso), `block` (barra), `approval` (barra até um
  admin aprovar — nesta feature vira `block` com a mensagem "precisa de aprovação"; aprovação de verdade é rodada 2).
  **Modo global** (`off` | `monitor` | `enforce`): `off` não avalia nem registra; `monitor` registra o que barraria mas
  deixa passar; `enforce` aplica. Padrão: `monitor` (nada muda para ninguém no deploy; o admin liga o `enforce`).
- **Regras embutidas** (semeadas por migração, `Builtin = true`, o admin muda efeito/escopo/ativa mas não apaga) =
  o que hoje está fixo em `SkillsRulesService.FixedRules` e no `Guard.cs` do executor. Regra nova do admin usa os
  mesmos campos. O `prmake_rules` / `Skills/rules` passa a listar as regras ativas da governança (no lugar da lista fixa).
- **Hooks** = configuração dos hooks do Claude Code que o instalador registra (um script só, `prmake-governance.py`,
  que lê `GET Governance/hooks/config` com cache local de 60 s) e dos gatilhos do servidor (evento da trilha que casa
  → ação: `timeline`, `webhook`). Falha de rede no hook **deixa passar** (`HookFailOpen`, configurável) e o tempo
  máximo é `HookTimeoutMs` (1 500).
- **Marcos do card** configuráveis (`Milestones` nas configurações): cada marco diz de onde vem a data (plano de
  análise criado/iniciado/concluído, etapa por chave/tipo, item por tipo, sub-tarefa por tipo, evento por ação) —
  "iniciou a tarefa", "análise inicial", "protótipo inicial", "solução escolhida", "correção iniciada", "PR aberto",
  "QA", "fechado". O tempo de cada ação vem de `StartedAt/FinishedAt` (etapas, sub-tarefas, pedidos da fila) e da
  duração dos eventos.
- Tudo de leitura da governança é **admin**. `evaluate`, `hooks/config` e `hooks/events` valem para qualquer usuário
  autenticado (são a ponta dos hooks), sempre com a identidade de quem chama.

## Contratos (API) — `api/v1/Governance`

Tipos (camelCase no JSON; datas sem `Z`, como no resto da API):

```ts
type Source = 'screen' | 'api' | 'mcp' | 'skill' | 'executor' | 'specialist' | 'hook' | 'system';
type Effect = 'monitor' | 'warn' | 'block' | 'approval';
type Mode = 'off' | 'monitor' | 'enforce';
type Outcome = 'ok' | 'error' | 'blocked' | 'warned';

interface GovernanceEvent {
  id: number; startedAt: string; finishedAt: string | null; durationMs: number | null;
  userId: string | null; userName: string; role: string | null;
  source: Source; action: string;            // 'POST ExecutionPlan/{id}/steps/{key}/complete' | 'mcp:prmake_advance' | 'hook:PreToolUse:Bash'
  category: string;                          // plan | step | question | work-item | task | queue | devops | pr | timeline | case | config | governance | knowledge | session | tool | other
  cardNumber: number | null; planId: string | null; phase: string | null; stepKey: string | null;
  workItemNumber: number | null; taskNumber: number | null;
  outcome: Outcome; statusCode: number | null; rules: string[]; detail: string | null;
  sessionId: string | null; machine: string | null;
}

interface GovernanceRule {
  id: string; key: string; name: string; description: string; category: string;   // fluxo | git | dados | credenciais | integracoes | custo | qualidade
  enabled: boolean; effect: Effect; builtin: boolean; order: number;
  message: string;                                   // mostrado a quem foi barrado/avisado
  scope: { roles: string[]; users: string[]; sources: Source[]; cardTypes: string[]; exceptRoles: string[] };
  match: {
    actions: string[];      // glob sobre GovernanceEvent.action (ex.: 'POST Azure/card/*/actions/*', 'mcp:prmake_pr_*')
    tools: string[];        // glob sobre a ferramenta do Claude Code (ex.: 'Bash', 'mcp__prmake__*', 'Write')
    patterns: string[];     // regex (case-insensitive) sobre o comando/entrada da ferramenta ou o corpo resumido
    paths: string[];        // glob sobre caminho de arquivo (Read/Write/Edit)
  };
  updatedAt: string | null; updatedBy: string | null;
}

interface GovernanceHook {
  id: string; key: string; name: string; description: string;
  event: string;            // 'claude:SessionStart' | 'claude:UserPromptSubmit' | 'claude:PreToolUse' | 'claude:PostToolUse' | 'claude:Stop' | 'claude:SubagentStop' | 'server:<glob da action>'
  matcher: string;          // glob da ferramenta (claude:*ToolUse) ou da categoria (server:*); '' = tudo
  action: 'record' | 'evaluate' | 'timeline' | 'webhook';
  config: Record<string, string>;   // webhook: url, secret; timeline: template ('{user} {action} no card {card}')
  enabled: boolean; builtin: boolean; order: number; updatedAt: string | null; updatedBy: string | null;
}

interface GovernanceSettings {
  mode: Mode; retentionDays: number; hookFailOpen: boolean; hookTimeoutMs: number; hookCacheSeconds: number;
  exemptRoles: string[];                 // nunca barrados (padrão: [])
  recordSources: Source[];               // origens gravadas na trilha (padrão: todas)
  milestones: Milestone[];
  updatedAt: string | null; updatedBy: string | null;
}
interface Milestone {
  key: string; label: string; order: number;
  from: 'plan' | 'step' | 'work-item' | 'task' | 'event' | 'request';
  phase?: 'analysis' | 'correction';     // plan/step
  match?: string;                        // glob: chave/tipo da etapa, tipo do item, tipo da sub-tarefa ou action do evento
  at: 'created' | 'started' | 'finished';
}

interface EvaluateRequest {
  source: Source; event?: string; tool?: string; input?: string; path?: string; action?: string;
  cardNumber?: number; sessionId?: string; machine?: string; cwd?: string;
}
interface EvaluateResponse {
  decision: 'allow' | 'warn' | 'block'; mode: Mode; reason: string | null;
  rules: { key: string; name: string; effect: Effect; message: string }[];   // todas as que casaram (inclusive monitor)
}
```

Endpoints:

| Método | Rota | Quem | O quê |
|---|---|---|---|
| POST | `evaluate` | autenticado | avalia (não grava a ação; grava evento `governance` só quando barra/avisa) |
| GET | `hooks/config` | autenticado | `{ mode, failOpen, timeoutMs, cacheSeconds, hooks: GovernanceHook[] (só claude:* ativos), toolMatchers: string[] (globs de tools das regras ativas — o script só chama o evaluate quando casa) , version }` |
| POST | `hooks/events` | autenticado | lote (≤ 200) de `{ event, tool?, input?, startedAt, finishedAt?, durationMs?, outcome?, cardNumber?, sessionId?, machine?, detail? }` → trilha (source `hook`, ou `executor` com credencial do executor) |
| GET | `events` | admin | `?from&to&user&card&source&category&outcome&action&search&page&size` → `{ total, items: GovernanceEvent[] }` (ordem: mais recente primeiro) |
| GET | `overview` | admin | `?from&to&tzOffsetMinutes` → `{ kpis: {events, users, cards, plansStarted, analysesDone, correctionsDone, workItemsDone, tasksDone, blocked, warned, errors, medianAnalysisMs, medianCorrectionMs, medianFirstAnalysisMs, aiCostUsd}, perDay: {day, events, blocked, warned, errors}[], bySource: {key,count}[], topActions: {key,count,medianMs}[], topRules: {key,name,count,blocked}[] }` |
| GET | `users` | admin | `?from&to` → `{ userId, userName, events, cards, plansStarted, analysesDone, medianAnalysisMs, workItems, tasksDone, medianTaskMs, blocked, warned, errors, aiCostUsd, lastActivityAt }[]` |
| GET | `users/{userId}` | admin | resumo acima + `cards: CardRow[]` + `recent: GovernanceEvent[]` (50) |
| GET | `cards` | admin | `?from&to&search&user&page&size` → `{ total, items: CardRow[] }` com `CardRow = { cardNumber, title, kind, users: string[], firstActivityAt, lastActivityAt, milestones: {key,label,at}[], analysisMs, correctionMs, leadTimeMs, workItems, tasksDone, blocked, status }` |
| GET | `cards/{card}` | admin | `{ card: CardRow, timeline: TimelineRow[] }`, `TimelineRow = { at, endAt, durationMs, kind: 'milestone'|'plan'|'step'|'work-item'|'task'|'request'|'event', title, user, outcome, ref }` (ordem cronológica) |
| GET | `tasks` | admin | `?from&to` → `{ kind, source: 'step'|'task'|'request', count, done, failed, medianMs, p90Ms, users }[]` (etapas agrupadas por `Kind`/`Key` sem sufixo numérico, sub-tarefas por `Kind`, pedidos do executor por `Kind`) |
| GET/POST | `rules` | admin | lista / cria (`key` único, slug) |
| PUT/DELETE | `rules/{id}` | admin | altera (embutida: não muda `key`/`builtin`) / apaga (embutida → 409) |
| POST | `rules/test` | admin | `EvaluateRequest` + `rule?: GovernanceRule` (rascunho) → `EvaluateResponse` sem gravar |
| GET/POST | `hooks` | admin | idem regras |
| PUT/DELETE | `hooks/{id}` | admin | idem |
| GET/PUT | `settings` | admin | `GovernanceSettings` |
| GET | `changes` | admin | `?take` → histórico de alterações de regras/hooks/configurações (são eventos `category=governance`) |

Barrado no servidor: HTTP 403 `{ code: 'governance_blocked', message, rules: [...] }` (o front mostra a mensagem);
MCP: `CallToolResult { isError: true, content: "Barrado pela governança: <mensagem> (regra <key>)" }`. Aviso: cabeçalho
`X-Governance-Warning` (HTTP) e o texto anexado ao resultado (MCP).

## Regras embutidas (seed)

| key | categoria | efeito | casa |
|---|---|---|---|
| `no-merge` | git | block | Bash `gh pr merge`, `gh pr review .*--approve`, `git merge` em branch protegida; `mcp:*merge*` |
| `no-force-push` | git | block | Bash `git push .*(--force\b\|-f\b\|--force-with-lease)` |
| `no-push-protected` | git | block | Bash `git push .* (master\|main\|develop\|release[^ ]*)\b` |
| `writes-through-prmake` | integracoes | block | Bash `curl`/`gh api`/`az` com método de escrita para `dev.azure.com`, `api.github.com`, `graph.microsoft.com` |
| `no-db-writes` | dados | block | Bash/MCP de SQL com `insert\|update\|delete\|merge\|drop\|alter\|truncate\|create` fora de `select` |
| `no-cognito-writes` | dados | block | Bash `aws cognito-idp (admin-)?(create\|delete\|update\|set\|disable\|enable)` |
| `protect-credentials` | credenciais | block | Read/Bash em `**/solvace-env-credentials.json`, `**/.prmake/db-credentials*`, `**/.aws/credentials` |
| `no-destructive-rm` | git | block | Bash `rm -rf (/\|~\|\$HOME)( \|$)` |
| `no-invented-branch` | fluxo | warn | `mcp:prmake_pr_open`/`POST *pull*` sem branch das regras do card (avaliado pelo `SkillsRulesService`) — rodada 2: só monitor |
| `ai-cost-daily` | custo | warn | pedido do executor quando o custo do dia do usuário passa de `ExecutionUserSettings.DailyBudgetUsd` |

## Hooks embutidos (seed)

| key | evento | ação |
|---|---|---|
| `claude-session` | `claude:SessionStart` | record |
| `claude-prompt` | `claude:UserPromptSubmit` | record (só tamanho e card; nunca o texto) |
| `claude-pretool` | `claude:PreToolUse` | evaluate |
| `claude-posttool` | `claude:PostToolUse` | record (matcher `Bash\|Write\|Edit\|mcp__prmake__*`) |
| `claude-stop` | `claude:Stop` | record |
| `server-blocked-timeline` | `server:*` (outcome blocked) | timeline — desligado por padrão |

## Fases

| Fase | O quê | Onde | Depende |
|---|---|---|---|
| B1 | Módulo `Solvace.Governance` (domain: entidades `GovernanceEvent`, `GovernanceRule`, `GovernanceHook`, `GovernanceSetting`; `GovernanceRuleEngine` puro; application; infra: contexto + migração + seed das regras/hooks/configurações); sln, Dockerfile (nada — `COPY . .`), `StartupMigrator`, `AddGovernanceModule` | `CIME/modules/Solvace.Governance` | — |
| B2 | Trilha: `GovernanceActorResolver` (quem/origem: skill pelo `X-Execution-Client`, executor pela claim `executorId`, especialista pelo `CaseClaims`, MCP, tela), `GovernanceEventSink` (Channel + gravação em lote + retenção diária), middleware HTTP de auditoria (template da rota, card/plano/item/sub-tarefa pelos valores da rota) | host `Governance/` | B1 |
| B3 | Aplicação das regras: middleware HTTP (403/aviso), filtro de CallTool do MCP (audita + aplica; encadeado com `SpecialistPlanEdits.Filter`), `POST evaluate`, `GET hooks/config`, `POST hooks/events`; cache das regras (30 s + invalidação local ao gravar) | host | B2 |
| B4 | Admin: CRUD regras/hooks/configurações + `rules/test` + `changes`; `GET events` | host + application | B1 |
| B5 | Métricas: `overview`, `users`, `users/{id}`, `cards`, `cards/{card}` (linha do tempo com marcos), `tasks` — SQL com GROUP BY nos contextos de execução/governança/IA, nunca carregar planos inteiros | host `Governance/GovernanceMetrics*.cs` | B2 |
| B6 | Gatilhos do servidor: hooks `server:*` executados pelo sink depois de gravar (timeline/webhook, com tempo máximo e sem bloquear); `prmake_rules`/`Skills/rules` listam as regras ativas da governança | host | B3 |
| S1 | `skills/_tooling/prmake-governance.py` (stdin do hook → evaluate/record; cache de config em `~/.prmake/governance.json`; fail-open; Windows/Git Bash); `prmake-skills.sh` registra os 5 hooks no `~/.claude/settings.json` (idempotente, `doctor`/`status` mostram) e serve o script com o pacote da ferramenta | `skills/_tooling` | B3 (contrato) |
| S2 | Executor 1.5.0: `ClaudeSettings.Ensure` acrescenta o hook de governança; `Guard.Check` continua (rede de segurança offline) e **reporta** o bloqueio ao PRMake (`hooks/events` com outcome blocked) | `tools/Cime.ExecutionAgent` | S1 |
| S3 | SKILL.md das skills: seção curta "Governança" (respeitar o que o hook barra; aviso = seguir e citar; nunca contornar) | `skills/*/SKILL.md` | S1 |
| F1 | Rota `auth/governance` (Admin), menu "Governança", `GovernanceApiService` + modelos + `?exemplo=1` com dados de demonstração; casca com abas (Visão geral · Usuários · Cards · Tarefas · Auditoria · Regras · Hooks · Configurações) e filtro de período compartilhado | front `pages/authenticated/governance/` | contrato |
| F2 | Visão geral (KPIs, eventos/dia por resultado, por origem, top ações e regras) · Usuários (tabela + detalhe) · Cards (tabela + linha do tempo com marcos e durações) · Tarefas (mediana/p90 por tipo) — gráficos `usage-charts` | front | F1 |
| F3 | Auditoria: tabela paginada com filtros (quem, card, origem, categoria, resultado, ação, texto), detalhe do evento | front | F1 |
| F4 | Regras e guardrails (lista por categoria, liga/desliga, efeito, escopo, casamentos, mensagem; simulador com `rules/test`), Hooks (lista, editor, liga/desliga), Configurações (modo global com explicação, retenção, hooks, isentos, marcos) | front | F1 |
| F5 | Ação barrada nas telas: interceptor mostra `governance_blocked` com a mensagem e a regra; `X-Governance-Warning` vira aviso | front | F1 |
| Q1 | Testes: motor de regras (cada regra embutida casa/não casa nos comandos do `Guard.cs`), modo off/monitor/enforce, escopo, sink, middleware (rota → card), métricas sobre dados semeados; `tools/SkillTests` para o script; `ng test` dos `.logic.ts` | | B*, S*, F* |
| Q2 | e2e local (harness `.t0098`: Postgres isolado, API, front, hook de verdade num Claude Code sem rede) | | Q1 |
| Q3 | Deploy (modo `monitor`) e uma semana de leitura antes do `enforce` — precisa de ok do usuário | | Q2 |
