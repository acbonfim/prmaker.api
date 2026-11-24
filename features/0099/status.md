# Status — Feature 0099

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Épicos, chave `E…`, seeds de gestão, PDF, MCP de épico/PDF | em andamento | agente B1 | | |
| B2 | DevOps: metadados, board, consulta, criar/editar/mover, MCP | concluído (aguarda Q1) | agente B2 | 1ad2f2c, b27344e | worktree `prform.api-0099-devops`. Serviço `IDevOpsDeskService`/`DevOpsDeskService` + mapeamento puro `DevOpsDeskMapping` (Azure application); `DevOpsController` (todas as rotas do contrato); `DevOpsDeskRunner` (Timeline "… pela Mesa de gestão do CIME" + `devopsCardsUpdated {ids, action}` no grupo `devops`); `DevOpsMcpTools` (6 ferramentas; escrita em `ActingTools`). Testes: `DevOpsDesk0099Tests` (prform.tests, 30 casos). **Q1 liga**: `IEpicCardSource` (cards do épico p/ `scope=epic`) e `IDevOpsCardCreatedHook` (grava `EpicCard created`) — interfaces e impl. nulas em `solvace.prform.api/Execution/DevOpsDeskRunner.cs`, registradas no Program.cs (trocar as 2 linhas); trocar o regex `^E\d+$` de `DevOpsMcpTools` por `EpicKey` (comentário `// 0099: trocar por EpicKey no Q1`). **Desvios**: `board/items` aceita `ids` (a tela pode mandar os cards do épico); move/create sem team/board = time padrão + board que tem o tipo; erro = `{code, message, error}` (codes: invalid, no_state_mapping, not_found, not_configured, devops_rejected/forbidden/not_found/conflict/error); comment → `{id, commentId}`; create com coluna/lane = 2 revisões (cria no estado inicial e move; falha no move não desfaz, vai no resumo); move só de ordem → action `reordered`, sem Timeline; coluna final do board só os 50 mais recentes (como o DevOps); tags/tipos/estados do filtro em OU; MCP query top padrão 200; MCP create com token de épico usa o próprio épico. **Pendente**: validar no DevOps real (só leitura primeiro); teams/members em 7.1 com fallback `-preview`. |
| F1 | Página Mesa de gestão, épicos, cards do épico, chips na Bancada | em andamento | agente F1 | | |
| F2 | Board do DevOps, painel/criação de card, tags/campos | em andamento | agente F2 | | worktree `prform-app-0099-board` |
| S1 | Skills especialista-de-gestao, tarefas-de-gestao | em andamento | agente S1 | | |
| Q1 | Mesclar, build, e2e local | pendente | Claude | | |

## Log
- 2026-11-24 — spec, mapeamento (back/front), plano com contrato; agentes disparados em paralelo.
- 2026-11-24 — B2 concluído no worktree `prform.api-0099-devops` (serviço, controller, MCP, testes); pontos de extensão do épico para o Q1.
