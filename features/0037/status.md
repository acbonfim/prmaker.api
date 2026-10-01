# Status — Feature 0037

Branch `feature/0037` em `prform.api-0037` (backend + skills) e `prform-app-0037` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | `WaitingOn` na etapa + `resolve` + migração `AddStepWaitingOn` | ✅ concluída | a361ca1 |
| B2 | Pendências calculadas, `GET pending`, tempo real, Timeline | ✅ concluída | a361ca1 |
| F1 | Painel: aviso "Aguardando você", estado na linha, botões na linha | ✅ concluída | front f7fabc6 |
| F2 | Sino do topo, chip do card, recentes, título da aba/badge | ✅ concluída (notificação do navegador fica para depois) | front f7fabc6 |
| S1 | Permissões do `sql-query.sh`/`cognito-query.sh` + doctor + `permissions` | ✅ concluída | (ver log) |
| S2 | `block`/`unblock`, `control`/`watch` com pendências e regra de etapa travada | ✅ concluída | (ver log) |
| S3 | `sql-query.sh --ping`, preflight e análise não fecha sem banco | ✅ concluída | (ver log) |
| S4 | Resumo PT/EN = orientação ao cliente; plano conclui no fechamento (item 2) | ✅ concluída | (ver log) |
| Q1 | Teste local | ✅ backend + front + skill | |

## Handoff
- S1–S4 feitas depois que o usuário liberou a edição de `skills/` (branch `feature/0037-skill`). A versão das skills é o hash do
  conteúdo (`skills/README.md`) — publicada no próximo deploy da API.
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
- 2026-10-01 — back (#54) e front (#32) mesclados e publicados.
- 2026-10-01 — S1–S4 (branch `feature/0037-skill`): `PERMISSION_GROUPS` (devops-v1 + readonly-v2 com `sql-query.sh`/`cognito-query.sh`),
  `doctor` confere, `prmake-skills.sh permissions` reaplica; `block`/`unblock`; `control` separa "aguardando o usuario" e mostra as pendências;
  `watch` imprime `PENDENCIA RESOLVIDA` no *Já resolvi*; `sql-query.sh --ping` (exit 2 VPN, 3 login, 4 credenciais); regras: block na hora,
  card com dados não fecha sem o banco, seção "Banco de dados" obrigatória, orientação ao cliente = resumo PT/EN, sem `orientar-cliente`/
  `validar-cliente`, plano conclui no `fechar-card`.
- 2026-10-01 — Q1 da skill (API local): block → 200 e "aguardando o usuario" no control; block sem texto → erro; *Já resolvi* pela API → watch
  `PENDENCIA RESOLVIDA pelo usuario (Admin Teste)`; unblock → running; `permissions` num HOME falso: 6 regras novas, idempotente; doctor acusa
  a regra ausente; `--ping` sem credenciais → 4, host inalcançável → 2.
