---
name: desfazer-item-de-trabalho
description: Desfaz um item de trabalho cancelado (ou concluido) de um card no PRMake (feature 0085) — percorre as sub-tarefas ja feitas em ordem reversa e usa o "como desfazer" de cada uma (fechar o PR, apagar a branch que o item criou, reverter commit, voltar o estado do card no DevOps), sempre pelos endpoints do PRMake, e marca cada sub-tarefa como desfeita. Nunca desfaz o que ja foi mesclado em branch protegida sem a pessoa decidir. Roda pelo executor do PRMake (pedido "work-item-undo", depois de "Cancelar e desfazer") ou manualmente com /desfazer-item-de-trabalho <card> <numero>.
---

# desfazer-item-de-trabalho

Desfaz **um** item de trabalho (`#<numero>`): as sub-tarefas `completed` (e as `failed` que deixaram efeito, com
`undo`) em **ordem reversa** de `order`, cada uma pelo seu `undo` (`{action, params}`, gravado pelo
`executar-item-de-trabalho` ou pela sincronizacao do plano). Cada uma desfeita → `prmake_work_task(action: revert)`; o
PRMake marca o item `reverted` quando nao sobra nada.

Instalada e atualizada pelo PRMake (fonte: `skills/desfazer-item-de-trabalho`). Depende da `analisar-bug`.

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh   # work-items · close-pr · delete-branch · worktree · open-pr · devops
REPOS=~/.claude/skills/analisar-bug/scripts/revamp-repos.sh # where <repo>
```

## Regras que nao mudam
1. **Tudo pelo PRMake**: fechar PR = `bash $PLAN close-pr` (endpoint do PRMake); estado do card = `prmake_devops` /
   `bash $PLAN devops`; status das sub-tarefas = `prmake_work_task`. Nada de API direta do GitHub/DevOps. `git push
   --delete` so pelo `bash $PLAN delete-branch` (que confere as protecoes).
2. **Mesclado em branch protegida nao se desfaz sozinho**: PR `MERGED`, commit ja na master/release/qa/development
   (ou qualquer base do fluxo) → `prmake_work_task(action: fail, result: "<o que esta mesclado, onde, e o que seria
   preciso: PR de revert para <base>; quem decide>")` e **siga para a proxima** (nao pare o desfazer inteiro). So abra
   PR de revert se a pessoa pedir explicitamente (comentario/nova execucao com essa instrucao).
3. **Nunca mescle** (nem o PR de revert). Nunca apague branch que o item nao criou.
4. Idempotente: o que ja esta desfeito (PR ja fechado, branch ja apagada) conta como desfeito — `revert` com o resultado.
5. Modo executor (`PRMAKE_EXECUTOR=1`): sem perguntas no chat; o que precisa de decisao vira `fail` explicado.

## MCP primeiro
`ToolSearch("select:mcp__prmake__prmake_work_item,mcp__prmake__prmake_work_task,mcp__prmake__prmake_card,mcp__prmake__prmake_devops")`

| O que | MCP | Script (reserva) |
|---|---|---|
| item, sub-tarefas, `output`, `undo` | `prmake_work_item(card, number)` | `bash $PLAN work-items <card> <n>` |
| marcar desfeita / nao deu | `prmake_work_task(card, taskNumber, action: revert\|fail, result)` | (revert so pelo MCP; `work-task ... fail "..."`) |
| fechar PR | — | `bash $PLAN close-pr <card> <repo> <numero>` (exit 5 = mesclado) |
| apagar branch criada pelo item | — | `bash $PLAN delete-branch <card> <repo> <branch>` (exit 5 = recusado: PR aberto/mesclado ou protegida) |
| estado do card | `prmake_card` · `prmake_devops(card, action)` | `bash $PLAN devops <card> <acao>` |

## Fluxo
**0.** `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet desfazer-item-de-trabalho analisar-bug 2>/dev/null || true`

**1. Carregar** — `prmake_work_item(card, number)`. Lista a desfazer = sub-tarefas `completed` + `failed` com `undo`
(nao `none`) ou com `output` que mostre efeito (`created: "true"`, `pr`, `commit`), ordenadas por `order`
**decrescente**. `pending`/`cancelled`/`skipped`/`reverted` ficam como estao. Nada a desfazer → diga e encerre.

**2. Planejar antes de agir** (evita trabalho inutil):
- Branches que **o item criou** (`undo.action == delete-branch`): `revert-commit` nessas branches **nao** precisa de
  revert — a branch inteira sai. Marque essas sub-tarefas `revert` com "coberto pela remocao da branch `<b>`
  (sub-tarefa #n)" **depois** que o `delete-branch` der certo.
- PR aberto numa branch que sera apagada: o `close-pr` vem antes do `delete-branch` (a ordem reversa ja garante,
  porque o PR foi aberto depois da branch).

**3. Para cada sub-tarefa (ordem reversa)**, pelo `undo.action`:

| undo | Como desfazer | Resultado |
|---|---|---|
| `close-pr {repo, number}` | `bash $PLAN close-pr <card> <repo> <number>`. exit 0 → `revert`. exit 5 (mesclado) → regra 2 (`fail`) | "PR #n fechado (sem merge)" |
| `delete-branch {repo, branch}` | `bash $PLAN delete-branch <card> <repo> <branch>`. exit 0 → `revert` (e as `revert-commit` cobertas). exit 5 → `fail` com o motivo impresso (PR aberto: feche antes; mesclado: regra 2) | "branch `b` apagada do origin" |
| `revert-commit {repo, branch, sha, createdByItem}` | Coberta pela remocao da branch → ver passo 2. Branch protegida/base do fluxo, ou o commit ja chegou numa base (`git branch -r --contains <sha>` lista `origin/<base>`) → regra 2. Branch de trabalho com PR aberto (nao mesclado): `WT=$(bash $PLAN worktree <card> <repo> <branch> <branch> --item <n>)`, `git -C "$WT" pull --ff-only origin <branch>`, `git -C "$WT" revert --no-edit <sha...>` (mais novo primeiro), `git -C "$WT" push origin HEAD:<branch>` — o PR se atualiza → `revert` com o sha do revert | "commit `abc` revertido em `b` (`def`)" |
| `devops-state {from, action?}` | `prmake_card`: ja esta em `from` → `revert`. `action` informado e disponivel (`dev-test-in-qa`, `test-in-production`, `ready-for-qa`) → `prmake_devops(card, action)`. Sem acao do PRMake que leve a `from` → `fail`: "o PRMake nao tem a transicao para `<from>` — mova o card a mao no DevOps" (falta endpoint: nao contorne) | "card voltou para `from`" |
| `none` / sem `undo` e sem efeito | nada a fazer | `revert` com "nada a desfazer (<motivo>)" |
| sem `undo`, com `output` (item de plano) | Infira pelo `output`/`links`: PR aberto → `close-pr` (repo e numero do link/output); branch com `created: "true"` → `delete-branch`; resto → `revert` com "nada a desfazer" | idem |

`result` de cada uma: 1–3 linhas, com nomes reais. Falha de rede/HTTP 5xx: tente de novo uma vez; persistiu → `fail`
com o erro e siga.

**4. Fim** — `prmake_work_item` para conferir. Relatorio curto no chat: o que foi desfeito, o que ficou (com o motivo e
quem decide). Nao escreva na Timeline (o PRMake registra os marcos do item).
