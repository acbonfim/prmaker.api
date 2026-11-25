# Plano 0103 — Agendamento de itens de trabalho e sub-tarefas

Branches `feature/0103` em `prform.api-0103` (back, skills) e `prform-app-0103` (front), a partir da origin/master.

## Decisões

- **Sub-tarefa agendada = pausada até um horário.** Reaproveita a pausa do card 78680 (`Held`): o executor não a começa,
  nem as que dependem dela (direta ou indiretamente — `HeldBlocker` sobe a cadeia); as independentes seguem. Campo novo
  `ScheduledAt` (UTC) + `ScheduledById` (quem agendou — dono do pedido na hora de rodar) na sub-tarefa (`WorkItemTasks`) e
  na etapa do plano (`ExecutionSteps`; item espelho acompanha pela sincronização, como a pausa).
  - `control` ganha a ação `schedule` (`at`); `pause` numa agendada tira a data (fica pausada sem data); `resume`
    (= "Executar agora") tira pausa e data e retoma como o "Continuar".
  - Vale também para sub-tarefa **em espera** do Claude (caso do #1080: esperar a próxima execução do job) — volta a
    pendente e aguarda o horário.
- **Item agendado** (só item avulso pendente/com falha, sem pedido na fila): `ScheduledAt`/`ScheduledById`/`ScheduledBy` e
  `Parked` (em espera sem data — opcional, a pessoa decide). Item agendado/em espera não roda por `execute` comum:
  REST/MCP pedem `runNow` (o botão da tela manda); fluxos internos (reabrir/acrescentar/destravar com execute) não o
  disparam. Item do plano agenda pela sub-tarefa.
- **Quem dispara no horário**: o Cloud Run escala a zero (sem timer confiável e sem CPU sempre ligada) — o "tique" roda
  nos sinais de vida dos executores (`ExecutionQueue/report` a cada 60 s e `ready` com o relay fora), com trava de 20 s
  por instância. Só faz sentido rodar com algum executor ligado; o pedido vai para a máquina de **quem agendou**.
  - Item vencido → `ExecuteAsync(runNow)`; sub-tarefa vencida → `resume` (espelho → etapa do plano → retomada do plano;
    avulsa → executa se o item já tinha começado). Falhou (item cancelado, plano fechado...) → tira a data, fica pausada,
    com o motivo no histórico.
- **Horário**: a API aceita ISO-8601 (com fuso; sem fuso = Brasília) ou relativo (`+2h30m`, `+90m`, `+1d`); guarda em UTC;
  textos (histórico, Timeline, MCP) em Brasília `dd/MM HH:mm`. Até 60 dias à frente; no passado recusa.
- **Tela**: relógio de agendamento (`alarm`) lilás (`#b392f0`) no item e na sub-tarefa (status, selo no controle),
  diálogo de agendamento (data/hora local, atalhos +30 min/+1 h/+2h30/amanhã, "sem data" no item), menu da sub-tarefa
  (Agendar…/Alterar agendamento…/Executar agora/Deixar sem data), menu do item (Agendar…/Em espera sem data/Remover
  agendamento), Executar = executar agora; editor do item com "Iniciar: ao executar | agendar | em espera".
- **Skills/especialista**: `prmake_work_schedule` (item ou sub-tarefa: set|park|clear|run-now), `scheduledAt`/`parked` no
  `prmake_work_item_create`, `scheduledAt` no `_add_task` e nas `tasks`, `runNow` no `_execute`. SKILL.md do executar,
  especialista do caso/gestão, analisar-bug/us, validar-ambiente: esperar horário conhecido = agendar (nunca `wait` à toa);
  PR mesclado → item de validação no ambiente agendado (~2h30 depois) ou em espera sem data.

## Contratos

| Método | Rota | O quê |
|---|---|---|
| POST | `WorkItems/{n}/schedule` | `{ action: 'set'\|'park'\|'clear', at? }` → `WorkItemResponse` |
| POST | `WorkItems/{n}/execute` | `{ runNow?: boolean }` (agendado/em espera sem `runNow` → 409 `scheduled`) |
| POST | `WorkItems/tasks/{t}/control` | `+ action: 'schedule'`, `at` |
| POST | `WorkItems/card/{card}` | `+ scheduledAt?`, `parked?`; `tasks[].scheduledAt?` |

```ts
WorkItem  += { scheduledAt?: string|null; scheduledBy?: string|null; parked?: boolean; nextScheduledAt?: string|null; scheduleBlock?: string|null }
WorkTask  += { scheduledAt?: string|null }
WorkTaskControls += { schedule?: string|null }   // null = pode agendar; texto = por que não
```

## Fases

| Fase | O quê | Depende |
|---|---|---|
| B1 | Domínio: campos, `ScheduleTime`, hold com data (sub-tarefa e etapa), item agendado/em espera, controles, migração | — |
| B2 | Aplicação: control `schedule`, `ScheduleAsync`, execute `runNow`, criação, tique de liberação + repositório | B1 |
| B3 | REST + MCP (`prmake_work_schedule`, create/add_task/execute) e saídas do plano | B2 |
| B4 | Skills (SKILL.md) e README | B3 |
| F1 | Front: modelo/looks lilás, diálogo de agendamento, menus, editor, Executar agora | B3 (contrato) |
| Q1 | Testes (unidade back, specs front, build) | todas |
| Q2 | e2e local (API + Postgres isolado) | todas |
