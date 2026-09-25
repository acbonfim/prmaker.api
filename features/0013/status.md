# Status — Feature 0013 (Relay de tempo real no MonsterASP)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0013` no backend (`../prform.api-0013`) e no front (`../solvace.prform.web/prform-app-0013`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Building block (modo Relay, token) + `GET RealTime/connection` | 1 | — | ⬜ pendente | Claude | — |
| B2 | Projeto `Cime.RealTime.Relay` (hub, `/publish`, `/health`) | 1 | — | ⬜ pendente | Claude | — |
| F1 | `WsService` com token e URL vindos da API (fallback legado) | 1 | — | ⬜ pendente | Claude | — |
| F2 | Recarregar dados ao reconectar (`resynced`) | 2 | F1 | ⬜ pendente | Claude | — |
| I1 | Workflow de deploy no MonsterASP, Terraform, DNS, README | 2 | B1, B2 | ⬜ pendente | Claude | — |
| Q1 | Publicação e teste | 3 | todas | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Relay próprio no MonsterASP em vez de Ably/Pusher/Firebase: custo zero (hospedagem já paga), sem fornecedor novo, sem dependência de GCP, mesmo SignalR (usuário, 2026-09-25: "se tiver algo que possa subir lá já zeraria custos").
- Token de 10 min emitido pela API (JWT HS256, chave compartilhada API↔relay) no lugar da chave fixa do front.
- Notificação ao relay aguardada no request, timeout 3 s, sem exceção (Cloud Run estrangula CPU fora do request).
- Modo `InProcess` mantido para dev e rollback.

## Pendências do usuário (para o Q1)
- Site no MonsterASP: criar um novo ou reaproveitar o slot do `prformapi.runasp.net` (código antigo, dá 500 no `POST /PullRequest`).
- Ativar o Web Deploy no painel e cadastrar no GitHub (`acbonfim/prmaker.api`): `MONSTER_WEBSITE_NAME`, `MONSTER_SERVER_COMPUTER_NAME`, `MONSTER_SERVER_USERNAME`, `MONSTER_SERVER_PASSWORD`, `REALTIME_RELAY_KEY`, `REALTIME_TOKEN_SIGNING_KEY`.

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- No worktree do front, `node_modules` é um symlink para `../prform-app/node_modules`. Remover antes de commitar (ou não adicioná-lo).
- Banco de dev = produção: não rodar a API local contra o banco. O teste do relay (B2) não precisa de banco.

## Log
- 2026-09-25 — Planejamento: spec, `plan.md` e `status.md` criados; worktrees `feature/0013` criadas nos dois repos.
