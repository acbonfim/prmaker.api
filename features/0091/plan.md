# 0091 — Plano

Branch `feature/0091` nos dois repos (worktrees `prform.api-0091` e `prform-app-0091`). Contrato em `contract.md`.

| Fase | O quê | Depende |
|---|---|---|
| B1 | `WorkItemDeliverable` + `WorkItemDeliverableContent` (diff), migração `WorkItemDeliverables0091`; `IWorkItemDeliverableApplication`: gravar (upsert por chave), gravar `code` (patch ≤ 2 MB), listar do item (gravadas + deduzidas de `Output`/`Links`, `ExecutionLinks` da etapa, PRs do card no repositório durante a sub-tarefa; estado ao vivo do PR), diff (fotografia ou compare ao vivo no GitHub por repositório); resolvedor da sub-tarefa em andamento; `WorkItemResponse.Deliverables` no GET; endpoints `WorkItems/{n}/deliverables[/{id}/diff]`, `WorkItems/tasks/{n}/deliverables[/code]`, `ExecutionPlan/{plan}/steps/{key}/deliverables[/code]` | — |
| B2 | Ganchos automáticos: abrir PR (controller + `prmake_pr_open`, `workItemTaskNumber`/`taskNumber`), Timeline (`prmake_timeline`, `POST Timeline` com `workItemTaskNumber`), DevOps (`DevOpsActionRunner`: estado/ação, horas, comentário); MCP `prmake_work_deliverable`; aviso no `prmake_advance` de etapa `code` da correção sem diff | B1 |
| B3 | `prmake-plan.sh deliver-code <repo> [--task n] [--step key] [--base b]` (+ bloco Windows/Git Bash), `open-pr --task`, `timeline` com tarefa; skills `analisar-bug` (correcao.md), `analisar-us`, `executar-item-de-trabalho` (`deliver-code` no fim de cada `code`/`branch`/`cherry-pick`), `desfazer-item-de-trabalho` (entregas ficam "desfeito"); testes de domínio/aplicação | B1, B2 |
| F1 | Modelo `WorkDeliverable` + serviço (`deliverables`, `diff`); componente `app-work-deliverables` (cartões por tipo, estado do PR, branch com link, código com arquivos/+/-) e `app-deliverable-diff` (diff completo sob demanda, tema claro/escuro, `parsePatch`) | contrato |
| F2 | Modal da sub-tarefa: "O que foi gerado" (entregas + arquivos da sub-tarefa), saídas cruas em "Dados brutos"; modal do item: aba "Entregas (n)" agrupada + resumo na Visão geral; popover e sessão de execução usam entregas; tempo real `deliverable` | F1 |
| Q1 | Build/test dos dois lados; e2e local (harness) com o item #56 do 77988 copiado (retroativo) e um item novo com PR + código | todas |
