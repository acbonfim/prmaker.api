# 0091 — Contrato (API ↔ tela)

## Entrega (`WorkDeliverableResponse`, JSON camelCase)

```jsonc
{
  "id": "6f1c…" ,            // guid da entrega gravada, ou sintético ("out:57:pr", "link:<guid>", "pr:edv-solvace#123")
  "taskNumber": 63,          // sub-tarefa (#n); null = do item
  "kind": "pr",              // pr | branch | commit | code | ticket | pr-text | devops-state | comment | timeline | hours | notify | doc
  "title": "PR #4521 edv-solvace: feature/77988-dev → development",
  "url": "https://github.com/…/pull/4521",
  "status": "open",          // pr: open|merged|closed|draft; ticket: open|resolved|closed; demais: null
  "repository": "edv-solvace",
  "branch": "feature/77988-dev",   // pr = origem; branch/code/commit = a branch
  "target": "development",         // pr = destino; branch/code = base
  "number": 4521,                  // pr = número do PR
  "sha": "a1b2c3d",                // commit/code = último commit
  "text": "…",                     // timeline/comment/pr-text/notify: texto curto (≤ 600); horas: "1.5"
  "data": { },                     // extras por tipo (abaixo) — objeto, nunca null
  "source": "auto",                // auto (gravado pelo servidor) | task (output/links da sub-tarefa) | plan (etapa do plano) | deduced
  "reverted": false,               // sub-tarefa desfeita / PR fechado pelo desfazer
  "hasDiff": true,                 // tem diff para "Ver alterações" (code gravado, ou pr/branch com repositório+base)
  "at": "2026-11-06T14:36:52Z"
}
```

`data` por tipo:
- `pr`: `{ alreadyExisted?, checks?: "success|failure|pending", checksFailed?, approvals?, changesRequested?, isDraft? }`
- `code` / `branch` / `cherry-pick` (kind `code` ou `branch`): `{ base?, mergeBase?, commits?: [{sha, title}], files?: number, additions?: number, deletions?: number, truncated?: bool, fileList?: [{path, status, additions, deletions}] }` (`fileList` até 300)
- `commit`: `{ title? }`
- `ticket`: `{ system? }`
- `devops-state`: `{ from?, to?, action? }`
- `hours`: `{ hours }`
- `timeline`: `{ entryId? }`

## Onde aparece

- `GET WorkItems/{n}` → `WorkItemResponse.deliverables: WorkDeliverableResponse[]` (todas do item, ordem: sub-tarefa, depois `at`).
  `GET WorkItems/card/{card}` **não** traz (lista leve). `WorkTaskResponse` não muda: a tela filtra por `taskNumber`.
- `GET WorkItems/{n}/deliverables` → `WorkDeliverableResponse[]` (o mesmo, sozinho).
- `GET WorkItems/{n}/deliverables/{id}/diff` → `WorkDeliverableDiffResponse`:
  ```jsonc
  { "source": "snapshot|github", "repository": "edv-solvace", "base": "development", "head": "feature/77988",
    "truncated": false, "compareUrl": "https://github.com/…/compare/development...feature/77988",
    "files": [{ "path": "a/b.cs", "previousPath": null, "status": "modified|added|removed|renamed",
                "additions": 3, "deletions": 1, "patch": "@@ -1,3 +1,4 @@\n…" }] }
  ```
  404 sem diff; `{ "error": "…" }` quando o GitHub não responde (branch apagada etc.).
- Tempo real: `workItemsUpdated` com `action: "deliverable"` e `number` do item (a tela recarrega o item aberto).

## Gravação

- `POST WorkItems/tasks/{taskNumber}/deliverables` (skill/script) — body `{ kind, title?, url?, status?, repository?, branch?, target?, number?, sha?, text?, data? }` → `WorkDeliverableResponse`. Mesma chave (tipo + repositório + branch/número/url) atualiza.
- `POST WorkItems/tasks/{taskNumber}/deliverables/code` — `multipart/form-data`: `repository`, `branch`, `base`, `mergeBase?`, `head` (sha), `commits` (JSON `[{sha,title}]`), `patch` (arquivo `git diff --no-color -M mergeBase..head`, até 2 MB; maior → cortado com `truncated`). Substitui o `code` anterior do mesmo repositório+branch na sub-tarefa.
- `POST ExecutionPlan/{planId}/steps/{key}/deliverables[/code]` — igual, pela etapa (vai para a sub-tarefa espelho).
- Automático no servidor (sub-tarefa = a informada ou a em andamento do card): abrir PR (`POST PullRequest/{card}/github` com
  `workItemTaskNumber?` opcional, `prmake_pr_open` com `taskNumber?`), `prmake_timeline` / `POST Timeline` com
  `workItemTaskNumber?`, ações do DevOps (`prmake_devops`, `DevOpsActionRunner`).
- MCP `prmake_work_deliverable(card, taskNumber, kind, …)` = o POST acima.
