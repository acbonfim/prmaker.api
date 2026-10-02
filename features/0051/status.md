# Status — Feature 0051

Branch `feature/0051` em `prform.api-0051` (API) e `prform-app-0051` (front, `node_modules` → `prform-app-0019`).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | (este commit) |
| B2 | ✅ concluída | Claude | (este commit) |
| F1 | ⏳ pendente | Claude | — |
| F2 | ⏳ pendente | Claude | — |
| T1 | ⏳ pendente | Claude | — |

## Decisões
- "Cards que você participou" não repete os cards que são seus ("Seus últimos cards"): as duas colunas se completam.
- Interações do plano gravadas só com o nome (resposta de pergunta, pausar/continuar/cancelar) entram pelo nome
  completo do usuário (o mesmo que o plano grava).

## Log
- 2026-10-02 — spec, plano e status; worktrees criados a partir de `origin/master`.
- 2026-10-02 — B1: `HomeCardsService` (host, `Home/`) + `GET Home/cards`; testado no Postgres isolado (`.t0051/`,
  porta 55451) com cards de 3 usuários: registro atualizado por mim, PR meu em card alheio, Timeline em card sem
  registro, comentário no plano, pergunta respondida pelo nome, card alheio sem participação (fica de fora).
- 2026-10-02 — B2: `POST Azure/cards/summary` (`workitemsbatch`, `errorPolicy=omit`); sem integração → `[]` + aviso no log.
