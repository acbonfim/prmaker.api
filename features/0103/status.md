# Status 0103

- Branch: `feature/0103` (prform.api-0103 e prform-app-0103), a partir de origin/master.
- Merge: autorizado pelo usuário ("quando terminar pode abrir pr e fazer o merge automaticamente", 2026-11-25).

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — domínio + migração | ✅ | principal | `ScheduleTime` (ISO/sem fuso = Brasília/relativo), `ScheduledAt/ScheduledById` em `WorkItemTasks` e `ExecutionSteps`, `ScheduledAt/ScheduledBy/Parked` em `WorkItems`; migração `WorkSchedule0103` (só colunas + índices parciais) |
| B2 — aplicação | ✅ | principal | control `schedule`, `ScheduleAsync` (set/park/clear/run-now), execute `runNow` (409 `scheduled`), fluxos internos não disparam item agendado, tique `ReleaseDueSchedulesAsync` (trava 20 s) |
| B3 — REST + MCP | ✅ | principal | `POST WorkItems/{n}/schedule`, `execute {runNow}`, tique em `ExecutionWorker/report` e `ExecutionQueue/ready`; MCP `prmake_work_schedule`, `scheduledAt/parked` no create/tool_run/env_validation, `scheduledAt` no add_task e nas tasks, `runNow` no execute, `scheduled` no `prmake_work_task`, `scheduledAt` no `prmake_plan`; instruções do MCP |
| B4 — skills | ✅ | agente | executar-item (regra 5c), especialista do caso/gestão, analisar-bug/us, validar-ambiente (esteira pendente → reagenda), README |
| F1 — front | ✅ | principal | relógio lilás `alarm` `#b392f0`, diálogo de agendamento, menus da sub-tarefa e do item, modal do item/sub-tarefa, editor (Iniciar + agendar por sub-tarefa), play = executar agora |
| Q1 — testes | ✅ | principal | executionplans 449 (14 novos em `WorkSchedule0103Tests`) · prform 657 · specs `work-schedule` + `work-task-controls` 7 · ng build ok |
| Q2 — e2e | ✅ | principal | `.t0103` (banco `cime0103` copiado do 0102, auth falso): `e2e.py` 0 falhas — item agendado → 409 sem runNow → em espera → reagendado → horário vence → report do executor libera → `work-item` na fila; sub-tarefa agendada no meio do item (independentes rodam, item fica pendente esperando, liberada no horário e item volta à fila); criar já agendada + executar agora; MCP `prmake_work_schedule`/execute. Telas no Chrome (`shots/`): lista, menus, diálogos, editor; cliques reais agendam e executam agora |

## Decisões de implementação

- Sub-tarefa agendada reaproveita a pausa (`Held`) do card 78680: `HeldBlocker` já sobe a cadeia de dependências, então
  as dependentes (diretas ou indiretas) esperam e as independentes seguem sem regra nova. `pause` numa agendada tira a
  data; `resume` libera (o "Executar agora").
- O próprio Claude que está na sub-tarefa pode agendá-la sem "interromper" a execução (`actor.IsExecutor`): ele segue com
  as independentes e encerra; a pessoa/especialista agendando a sub-tarefa em andamento para e retoma, como na pausa.
- Item do plano não agenda inteiro (agende a sub-tarefa); item espelho com só a agendada por fazer fica `pending`.
- O tique roda nos sinais de vida dos executores (sem CPU sempre ligada, sem timer no Cloud Run). Liberou e não deu para
  executar (item cancelado, plano fechado) → tira a data, fica pausada, motivo no histórico.

## Log

- 2026-11-25 — feature implementada e testada (unidade + e2e local + telas). PRs abertos e mesclados juntos.
