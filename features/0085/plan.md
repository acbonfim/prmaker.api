# Plano — Feature 0085: Itens de trabalho + "Bancada do card"

Worktrees: back `prform.api-0085`, front `../solvace.prform.web/prform-app-0085` (branch `feature/0085`, a partir da master).
A tela de pull request (`auth/register`) **não muda**. Todo o contrato abaixo é a fonte da verdade entre os agentes.

## Decisões

### Nome e rota da tela nova
- **Bancada do card** (aba "Bancada", ícone `handyman`), rota `auth/workbench?card=<n>` (+ `&item=<número>` abre o
  modal do item, `&focus=plan|items|timeline|chat`). Entrada no menu lateral logo acima de "Pull Request".
- Motivo: a tela é onde o card é trabalhado (análise, plano, itens, especialista, timeline, PRs) — "pull request" é só
  uma das saídas.

### Layout (desktop ≥ 1024)
```
┌ Cabeçalho do card ───────────────────────────────────────────────────────────────────────────┐
│ [🔎 nº do card]  🐞/📖 #77434  Título do card…   [estado] [área] [responsável]  ⓘ  ↗DevOps  ? │
│ alerta do card (inline) · chip do plano (fase/versão/progresso) · [Analisar bug|Analisar US]  │
├ Coluna principal (≈ 2/3) ─────────────────────────────┬ Coluna lateral (≈ 1/3) ───────────────┤
│ [ Itens de trabalho | Plano ]  (segmentado)            │ Especialista do caso (compacto)       │
│ Itens: barra [+ Novo item] [Pedir ao especialista]     │ ─────────────────────────────────────│
│        [Expandir todos] [Recolher todos] [filtro]      │ Card: descrição / repro (Bug) ou      │
│  ▸ 📖 #12  PR master→release-version  ● em andamento   │   critérios de aceite (US), anexos,   │
│      2/3  [▶ Executar] [✎] [⋯]                         │   PRs do card (resumo)                │
│    ├ #13 Criar branch hotfix/77434-rv   ✓              │ ─────────────────────────────────────│
│    ├ #14 Cherry-pick dos commits        ⟳              │ Timeline (últimas 5 + "ver tudo")     │
│    └ #15 Abrir PR → release-version     ○              │                                       │
│ Plano: <app-execution-plan> + "Cancelar e desfazer"    │                                       │
└────────────────────────────────────────────────────────┴───────────────────────────────────────┘
```
- Mobile (< 1024): abas `Itens | Plano | Especialista | Card`.
- Card sem itens (cards antigos): a aba "Plano" fica em evidência e a de itens mostra o vazio com "Gerar itens a
  partir do plano" (quando há plano de correção) e "+ Novo item". Com itens, "Itens de trabalho" fica em evidência.
- Bug × US: ícone discreto (joaninha `#cc293d` / livrinho `#009ccc`, SVG próprio via `MatIconRegistry` —
  `svgIcon="wi-bug"` / `svgIcon="wi-us"`), texto do card (Repro Steps × Critérios de aceite), botão
  "Analisar bug"/"Analisar US", tipo padrão do item = tipo do card, sem Root Cause para US.
- Modais: **Item** (cabeçalho com tipo/#/status/origem; abas Visão geral · Sub-tarefas · Histórico; ações), **Sub-tarefa**
  (aberto de dentro do item: tipo, parâmetros, resultado, saídas, desfazer), **Editor de item** (criar/editar; modelo
  pronto ou do zero; sub-tarefas reordenáveis), **Cancelar** (item ou plano, com "desfazer o que já foi feito").

### Regras de negócio
- Vários itens por card; cada item tem sub-tarefas ordenadas, cada uma com status e informações próprias.
- **Numeração**: uma sequência global do Postgres `execution.work_item_number_seq` (START 1, MAXVALUE 9999999 — cabe
  em `#0000000`), compartilhada por itens e sub-tarefas (o número identifica sem ambiguidade). Exibe `#123` sem zeros.
- **Edição**: só item `pending` com todas as sub-tarefas `pending`. Iniciado/concluído → 409 `notEditable`; a tela
  oferece "Criar novo a partir deste" (`POST {number}/duplicate`).
- **Execução**: cada item roda em paralelo aos outros (pista própria por item na fila); dentro do item, as sub-tarefas
  rodam na ordem. Item ligado a um plano (`planId`) executa retomando o plano (pista main de hoje).
- **Cancelar com desfazer**: item ou plano. Cancela pedidos na fila e sub-tarefas abertas; com `undo=true` cria pedido
  `work-item-undo` que desfaz em ordem reversa as sub-tarefas concluídas usando `undo` de cada uma (fechar PR, apagar
  branch criada, reverter commit, voltar estado do DevOps) — sempre por endpoints do PRMake. Sub-tarefa desfeita =
  `reverted`; item = `reverted` ao fim.
- **Duplicidade**: `fingerprint` = sha256 de tipo + kinds das sub-tarefas + parâmetros-chave normalizados
  (repository/source/target/branch). Criar com fingerprint igual a item não cancelado → 409 `duplicate`
  `{number,title,status}` (MCP devolve mensagem com o item existente), a menos de `allowDuplicate=true` + motivo.
- **Plano → itens**: quando um plano de correção é criado (`prmake_correction`/`CreateAsync` fase correction), o servidor
  cria um item `origin=correction`, `planId`, com uma sub-tarefa por etapa (`stepKey`), a não ser que a skill já tenha
  criado itens para esse plano. Mudança de etapa sincroniza a sub-tarefa (start/complete/cancel/waiting); plano concluído
  → item `completed`. Cards antigos: `POST WorkItems/card/{c}/from-plan/{planId}` materializa sob demanda.
- **US**: plano com `Kind=analisar-us` quando `System.WorkItemType = User Story` (prompt da fila escolhe a skill);
  itens do card nascem `type=user-story`.
- **Regras para o especialista**: `GET Skills/rules?card=` (e MCP `prmake_rules`) devolve um markdown "Regras do PRMake"
  calculado para o card (fluxo pela área, base, **nome de branch por destino** com `BranchSuffixByTarget`, padrões de
  commit/título, nunca mesclar, toda gravação pelo PRMake, regras dos itens). O `prmake_case_context` inclui as regras
  e a lista de itens (com fingerprint) — o especialista não inventa nome de branch nem repete item.
- **`-rv`**: chave nova `BranchSuffixByTarget` = `{"development":"-dev","qa":"-qa","release-version":"-rv","hotfix-version":"-hv","edge":"-edge"}`.
  `PromotionBranchPattern` ganha o marcador `{suffix}` (sufixo do destino, ou `-<destino>` se não houver); a migração
  troca o valor só se ainda for o padrão `{branch}-{target}` → `{branch}{suffix}` (admin pode voltar).

## Contrato

### Domínio (schema `execution`, `ExecutionPlanContext`)
`WorkItem`: Id (Guid), Number (long, seq), CardNumber (string), Type (`bug|user-story`), Title (≤ 300), Description
(markdown), Status, Origin (`user|specialist|analysis|correction|template`), TemplateKey?, PlanId? (Guid), Fingerprint,
CreatedBy/CreatedByName, CreatedAt/UpdatedAt/StartedAt?/CompletedAt?, CancelReason?, Links (jsonb
`[{kind:pr|branch|commit|doc|attachment|timeline,title,url}]`), ActiveRequestId?, Version (xmin).
`WorkItemTask`: Id, Number (long, seq), WorkItemId, Order, Title, Description, Kind, Executor (`claude|user`),
Params (jsonb `Record<string,string>`), Status, StepKey?, Result (markdown), Output (jsonb), Undo (jsonb
`{action, params}`), StartedAt?, CompletedAt?.
`WorkItemLog`: Id (long identity), WorkItemId, TaskNumber?, At, Actor, Kind, Text.
Status do item: `pending, queued, running, waiting, completed, failed, cancelled, reverting, reverted`.
Status da sub-tarefa: `pending, running, waiting, completed, failed, skipped, cancelled, reverted`.
Kinds de sub-tarefa (catálogo configurável `WorkItemTaskKinds`): `branch, cherry-pick, code, pr, devops-state,
sql-script, validation, comment, manual`.
`ExecutionRequest`: coluna nova `WorkItemId` (Guid?); kinds novos `work-item`, `work-item-undo`; pista `work-item`;
índice único filtrado por `WorkItemId` ativo; o índice da pista main passa a excluir esses kinds.

### REST `api/v1/WorkItems` (auth x-api-key; o executor e o token do caso também)
| Método | Rota | Corpo/retorno |
|---|---|---|
| GET | `card/{card}` | `WorkItemResponse[]` (com tasks), ordem Number |
| GET | `{number:long}` | `WorkItemResponse` + `logs` |
| GET | `catalog?card=` | `{taskKinds:[{kind,label,icon,params[]}], templates:[{key,title,description,type?,tasks:[...] já preenchidos para o card]}}` |
| POST | `card/{card}` | `CreateWorkItemRequest {title, description?, type?, templateKey?, tasks:[{title,description?,kind,executor?,params?}], allowDuplicate?, duplicateReason?}` → 201 `WorkItemResponse` / 409 `{code:"duplicate", existing:{number,title,status}}` |
| PUT | `{number}` | `UpdateWorkItemRequest {title, description, tasks[]}` → 409 `{code:"notEditable"}` |
| POST | `{number}/duplicate` | novo item pending copiado |
| POST | `{number}/execute` | `{requestId,status}` |
| POST | `{number}/cancel` | `{reason?, undo:bool}` |
| POST | `{number}/undo` | desfaz item já cancelado/concluído |
| POST | `tasks/{taskNumber}/{start\|complete\|fail\|skip}` | `{result?, output?}` (sub-tarefa do usuário ou executor) |
| POST | `card/{card}/from-plan/{planId}` | materializa item do plano (idempotente) |
| POST | `ExecutionPlan/{id}/cancel` (no controller de plano) | `{reason?, undo:bool}` — cancela plano + itens do plano (+ desfazer) |

`WorkItemResponse`: `{id, number, cardNumber, type, title, description, status, origin, templateKey?, planId?,
planPhase?, createdBy, createdByName, createdAt, updatedAt, startedAt?, completedAt?, cancelReason?, editable,
progress:{done,total}, links[], requestId?, activity?, fingerprint, tasks: WorkTaskResponse[], logs?}`.
`WorkTaskResponse`: `{id, number, order, title, description, kind, executor, params, status, stepKey?, result?,
output, undo?, startedAt?, completedAt?}`. JSON camelCase.

### Tempo real
Grupo `execplan:{card}`, evento **`workItemsUpdated`**, payload `{cardNumber, number, taskNumber?, action, status}`
(action: created, updated, task, status, request, activity, deleted). É só sinal: a tela refaz o GET.

### MCP (Execution/WorkItemMcpTools.cs, registrado no Program.cs)
| Ferramenta | Uso | Especialista? |
|---|---|---|
| `prmake_rules(card)` | markdown das regras do PRMake para o card | sim |
| `prmake_work_items(card)` | lista resumida (número, tipo, título, status, fingerprint, sub-tarefas) | sim |
| `prmake_work_item(card, number)` | detalhe | sim |
| `prmake_work_item_create(card, title, description, tasks, type?, templateKey?, planPhase?, allowDuplicate?, duplicateReason?)` | cria (origin = specialist se token do caso, senão analysis/correction pela fase) | sim |
| `prmake_work_item_update(card, number, title?, description?, tasks?)` | só pending | sim |
| `prmake_work_task(card, taskNumber, action, result?, output?, undo?)` | start/complete/fail/wait/skip/revert | não |

### Fila / executor
- `work-item`: prompt manda ler `skills/executar-item-de-trabalho/SKILL.md` com `card` e `item`; `work-item-undo`:
  `skills/desfazer-item-de-trabalho/SKILL.md`. Sessão nova sempre. Vagas próprias `WorkItemSlots` (Skills Configurations,
  padrão 2), fora do `MaxConcurrency`. Só executor ≥ 1.3.0 recebe esses pedidos.
- Executor 1.3.0 (`tools/Cime.ExecutionAgent`): trata os kinds novos como job comum (sem guard do especialista), com
  pool próprio de vagas.

### Configuração (Skills Configurations, semeada por migração idempotente no `DefaultContext`)
`BranchSuffixByTarget`, `WorkItemTaskKinds`, `WorkItemTemplates` (ex.: `pr-release-version` = branch
`{branch}{suffix:release-version}` a partir de `release-version` → cherry-pick → PR; `ajuste-pos-qa` = code → validation
→ pr), `WorkItemSlots`, `PromotionBranchPattern` (ajuste acima).

## Fases (ondas)

| Fase | O quê | Dono | Depende |
|---|---|---|---|
| B1 | Domínio + migração `WorkItems0085` (seq, tabelas, `ExecutionRequest.WorkItemId`, índices) + repositório + `WorkItemApplication` + `WorkItemsController` + tempo real + sincronização plano→item + cancelar plano com desfazer | agente B1 | — |
| B2 | Fila (kinds/pista/vagas/prompts/US), executor 1.3.0, MCP `WorkItemMcpTools`, `CaseSpecialistMiddleware`, contexto do caso, `GET Skills/rules`, seed `SeedWorkItems0085` (prform), `{suffix}` na promoção, fechar PR (desfazer) | agente B2 | B1 (interfaces) |
| F1 | Página `workbench`: rota, menu, aba, cabeçalho do card + busca, ícones Bug/US, coluna lateral (especialista, card, timeline), aba Plano com `<app-execution-plan>` + "Cancelar e desfazer", mobile, tour `workbench.tour.ts` | agente F1 | contrato |
| F2 | Itens: modelo/serviço/tempo real, `app-work-item-list`, modais item/sub-tarefa/editor/cancelar, pedir ao especialista | agente F2 | contrato |
| S1 | `especialista-do-caso`: regras pré-carregadas (cache no card), itens sem duplicar, criar itens, nunca inventar branch | agente S1 | contrato |
| S2 | Skills novas `executar-item-de-trabalho`, `desfazer-item-de-trabalho`, `analisar-us`; `analisar-bug` gera/executa itens; `prmake-plan.sh` (`rules`, `work-items`) | agente S2 | contrato |
| Q1 | Build back+front, e2e local (harness `.t0085`), revisão cruzada do contrato | Claude | todas |
