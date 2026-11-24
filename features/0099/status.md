# Status — Feature 0099

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Épicos, chave `E…`, seeds de gestão, PDF, MCP de épico/PDF | em andamento | agente B1 | | |
| B2 | DevOps: metadados, board, consulta, criar/editar/mover, MCP | em andamento | agente B2 | | worktree `prform.api-0099-devops` |
| F1 | Página Mesa de gestão, épicos, cards do épico, chips na Bancada | em andamento | agente F1 | | |
| F2 | Board do DevOps, painel/criação de card, tags/campos | em andamento | agente F2 | | worktree `prform-app-0099-board` |
| S1 | Skills especialista-de-gestao, tarefas-de-gestao | concluído | agente S1 | ef3f5b6 | `especialista-de-gestao` (SKILL.md+skill.json; reaproveita `prmake-case.sh` e seções do especialista-do-caso por `sed`), `tarefas-de-gestao` (guia por kind + `scripts/gestao.sh`→`gestao.py`: fetch/pull/save/pdf pela API do PRMake, report, duplicates, stale, rca-check), `executar-item-de-trabalho` (kinds de gestão, regra 4b chave E…, depende de tarefas-de-gestao), nota 0099 no especialista-do-caso, README. Versão = hash do conteúdo (sobe sozinha). Pendências p/ B1: `EnsureSkillsCommand` incluir `tarefas-de-gestao` (e prompt do especialista de épico rodar `update --quiet especialista-de-gestao`); params dos kinds/ask dos modelos seguem os nomes usuais da skill (source, wiql/filtros, ids, column, file, card/cards); skill usa `POST DevOps/query`, `GET/POST WorkItems/{n}/files`, `POST WorkItems/{n}/files/pdf` e `GET Skills/config` com o token do executor |
| Q1 | Mesclar, build, e2e local | pendente | Claude | | |

## Log
- 2026-11-24 — spec, mapeamento (back/front), plano com contrato; agentes disparados em paralelo.
- 2026-11-24 — S1: skills especialista-de-gestao e tarefas-de-gestao (gestao.sh) + kinds de gestão na executar-item-de-trabalho (ef3f5b6).
