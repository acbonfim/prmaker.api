# Status — Feature 0037

Branch `feature/0037` em `prform.api-0037` (backend + skills) e `prform-app-0037` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | `WaitingOn` na etapa + `resolve` + migração `AddStepWaitingOn` | ✅ concluída | a361ca1 |
| B2 | Pendências calculadas, `GET pending`, tempo real, Timeline | ✅ concluída | a361ca1 |
| F1 | Painel: aviso "Aguardando você", estado na linha, botões na linha | ✅ concluída | front f7fabc6 |
| F2 | Sino do topo, chip do card, recentes, título da aba/badge | ✅ concluída (notificação do navegador fica para depois) | front f7fabc6 |
| S1 | Permissões do `sql-query.sh`/`cognito-query.sh` + doctor | ⛔ bloqueada (auto mode) | |
| S2 | `block`/`unblock` e regra de etapa travada | ⛔ bloqueada (auto mode) | |
| S3 | Preflight do banco e análise não fecha sem banco | ⛔ bloqueada (auto mode) | |
| S4 | Resumo PT/EN = orientação ao cliente; plano conclui no fechamento (item 2) | ⛔ bloqueada (auto mode) | |
| Q1 | Teste local | ✅ backend + front (skill pendente) | |

## Handoff
- **S1–S4 não começaram**: o auto mode do Claude Code barrou editar (e depois ler) `skills/` como "Self-Modification" — a S1 mexe em
  `~/.claude/settings.json` (regras de permissão). Precisa de autorização do usuário (regra de permissão para editar `skills/` neste
  repositório, ou rodar S1–S4 fora do auto mode). A versão das skills é o hash do conteúdo (`skills/README.md`) — não há bump manual.
- Contrato que a skill deve usar (já no backend): `PATCH steps/{key}` `{status:"waiting", waitingOn:"user", reason:"<o que fazer>"}`
  (reason obrigatório; 400 sem ele); qualquer outro status limpa o waitingOn; `control` devolve `steps[].waitingOn/reason/changedBy`,
  `userPending`, `userActions[]`. O `watch` atual já acorda com o *Já resolvi* (`etapa X: waiting -> running`).
- Teste local: `.t0037/` (fora do git) — Postgres `cime-pg-0037` na 55438 com os schemas do host + `auth` (migrações da Cime.Auth e um
  usuário com o ExternalId do token, para `GET pending` e o sino funcionarem), `scen.sh` (cenário do card 74669), `s1.mjs`/`s2.mjs`.

## Log
- 2026-10-01 — análise e plano.
- 2026-10-01 — spec ganhou o item 2 (resumo PT/EN = orientação ao cliente; plano conclui no fechamento) → fase S4.
- 2026-10-01 — B1/B2 (a361ca1): WaitingOn, resolve, UserActions/UserPending, GET pending, grupo execplan-pending, Timeline; migração com backfill.
- 2026-10-01 — F1/F2 (front f7fabc6): aviso "Aguardando você", estado/ação na linha, "Parada com um aviso", sino com lista, selo nos recentes,
  (N) no título, chip do card.
- 2026-10-01 — Q1 local: waiting/user sem reason → 400; control com 3 pendências (pergunta, unblock, etapa do usuário); tela com aviso, sino (3),
  chip, Timeline "⚠️ Aguardando você"; "Já resolvi" → etapa running, título (3)→(2), `watch` da skill instalada acordou
  (`confirmar-dados: waiting -> running`); "Concluir" na linha libera a próxima etapa do usuário.
