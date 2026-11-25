# Plano 0094 — fases

## Fase 1 — Back: modelo + relação manual + lista  ⬜
- Entidade `WorkItemRelated` (domain) com factory/validação e `Source` no `WorkItemRelatedSource`.
- `ExecutionPlanContext`: DbSet + config + migração `WorkItemRelated0094` (schema `execution`).
- `IWorkItemRelatedRepository` + impl (upsert idempotente, list, remove).
- Response `WorkItemRelatedResponse` (+ item resolvido).
- App em `IWorkItemApplication`/`WorkItemApplication`: `ListRelatedAsync`, `AddRelatedAsync`, `RemoveRelatedAsync`.
- Controller: `GET/POST/DELETE card/{card}/related`.
- Build back.

## Fase 2 — Back: captura automática das menções  ⬜
- `CaptureMentionsAsync` (extrai `#+(\d+)`, resolve, upsert cross-card).
- Ganchos: CaseMessage (especialista), nota do plano / resultado de sub-tarefa, Timeline, descrição/sub-tarefa de item.
- Idempotente; manual não é rebaixado.

## Fase 3 — Back: contexto de IA  ⬜
- `RelatedSummaryMarkdownAsync` (resumo compacto).
- `prmake_case_context` + carregamento do executor incluem "Relatados".
- Skills `especialista-do-caso` e `executar-item-de-trabalho` leem a seção.

## Fase 4 — Front: aba Relatado read-only  ⬜
- `readOnly` + modo `related` no `work-item-list`; propagar aos modais/execução.
- Nova aba `reported` no workbench; busca manual; selo de origem.
- Build front (lint + build).

## Fase 5 — Validação  ⬜
- Build back + testes do módulo; build front.
- Testes de unidade: entidade `WorkItemRelated`, captura cross-card/idempotência, read-only.
- PRs nos dois repos; mesclar os dois juntos (merge autorizado pelo usuário).
