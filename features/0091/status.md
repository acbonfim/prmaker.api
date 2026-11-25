# 0091 — Status

| Fase | Status | Commits | Notas |
|---|---|---|---|
| B1 entregas (domínio, leitura, diff, endpoints) | concluída | back 611344d | `WorkItemDeliverables` + conteúdo (migração `WorkItemDeliverables0091`), `WorkDeliverableListing` (gravadas + output/links + links da etapa + PRs do card), `UnifiedPatch`, endpoints do item e da etapa |
| B2 ganchos automáticos | concluída | back f337c8d | PR (controller + `prmake_pr_open`), DevOps (endpoints + `prmake_devops`), Timeline (`POST Timeline` da skill + `prmake_timeline`), MCP `prmake_work_deliverable`, aviso no `prmake_advance` |
| B3 script/skills/testes | concluída | back 9a2ece1 | `deliver-code`, `open-pr --task`, `PRMAKE_WORK_TASK` na timeline; skills analisar-bug/analisar-us/executar/desfazer; `WorkItemDeliverableTests` (8) |
| F1 modelo + componentes | concluída | front bccbb59 | `WorkDeliverable`, `app-work-deliverables`, `app-deliverable-diff` |
| F2 modais | concluída | front bccbb59 | "O que foi gerado" na sub-tarefa, aba Entregas + resumo no item, popover e sessão de execução |
| Q1 integração | concluída | back (este) | ver abaixo |

## Q1 (2026-11-06)
- `dotnet build Solvace.Master.sln` ok; `dotnet test` ExecutionPlans 180 ok (8 novos), PullRequests 370 ok. Front: `ng build`
  dev ok, specs da área 34 ok.
- e2e local (`.t0091`, banco `cime91` = cópia do `cime` do `cime-pg-0090` + migração; API 5091, auth falso 8191, front
  4391 — 4200/5083/4291 estavam com outras sessões):
  - item #67 (4 sub-tarefas): `prmake-plan.sh deliver-code --task 68` real (worktree temporário) → código com 2 commits, 3
    arquivos e o diff; Timeline da skill com a #69 em andamento → entrega `timeline` da #69 sozinha; PR gravado + output do
    `complete` → uma entrega só; DevOps e chamado pelo output.
  - item #72 (retroativo): sub-tarefa de PR sem nada gravado + PR do card no mesmo repositório aberto durante ela → PR
    `deduced` com estado `merged`.
  - diff: fotografia ok; PR sem integração do GitHub → `error` legível (antes 500 — corrigido); entrega sem diff → 404.
  - Tela (1440 e 390 px): aba Entregas agrupada, resumo na Visão geral, sub-tarefa com "O que foi gerado" e o diff
    completo expandido; sem rolagem horizontal da página.

## Pendências
- Validar num card real: análise/correção pelo executor usando `deliver-code` (skills publicadas pelo /Skills).
- PR mesclado com a branch apagada: "Ver alterações" do PR mostra o erro + link (o código fica na entrega `code`).

## Log
- 2026-11-06 — diagnóstico (77988 #56: 7 sub-tarefas concluídas sem output/links), spec, contrato e plano; B1–B3, F1–F2, Q1.
