# Status — Feature 0051

Branch `feature/0051` em `prform.api-0051` (API) e `prform-app-0051` (front, `node_modules` → `prform-app-0019`).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | 543ebe4 |
| B2 | ✅ concluída | Claude | 543ebe4 |
| F1 | ✅ concluída | Claude | front 7bc98e1 |
| F2 | ✅ concluída | Claude | front 7bc98e1 |
| T1 | ✅ concluída | Claude | — (harness fora do git em `.t0051/`) |

## Decisões
- "Cards que você participou" não repete os cards que são seus ("Seus últimos cards"): as duas colunas se completam.
- Interações do plano gravadas só com o nome (resposta de pergunta, pausar/continuar/cancelar) entram pelo nome
  completo do usuário (o mesmo que o plano grava).

- Plano e Timeline não avisam o grupo `pullrequest-recent`: a home recarrega em silêncio pelo grupo de recentes
  (card salvo, PR aberto, handover), ao voltar para a aba e a cada 2 min com a aba visível — sem entrar no grupo de
  cada card (custo do relay). O selo "Aguardando você" segue ao vivo pelo `UserPendingService`.
- `Handover/GetRecent` e `PullRequest/GetRecentByUser` ficam na API (não são mais usados pela home).

## Log
- 2026-10-02 — spec, plano e status; worktrees criados a partir de `origin/master`.
- 2026-10-02 — B1: `HomeCardsService` (host, `Home/`) + `GET Home/cards`; testado no Postgres isolado (`.t0051/`,
  porta 55451) com cards de 3 usuários: registro atualizado por mim, PR meu em card alheio, Timeline em card sem
  registro, comentário no plano, pergunta respondida pelo nome, card alheio sem participação (fica de fora).
- 2026-10-02 — B2: `POST Azure/cards/summary` (`workitemsbatch`, `errorPolicy=omit`); sem integração → `[]` + aviso no log.
- 2026-10-02 — F1: home sem "Acesso rápido" e "Suas férias"; `app-home-cards` (mine/participated) e `app-avatar-stack`
  (dono por cima, "+N" acima de 4); título/estado/coluna do DevOps em lote; fotos pelo `PhotosByExternalIds` com cache
  entre as listas. Removidos `recent-cards` e `recent-handovers`.
- 2026-10-02 — F2: `?focus=plan|timeline|prs` na tela do card (aba no celular, rolagem + destaque no desktop).
- 2026-10-02 — T1: `dotnet build` e `ng build` ok; Chrome headless (1600 px e 400 px) com API local + auth de fotos
  falso: avatares sobrepostos com foto/iniciais, selos do plano/PRs/Timeline, atalho da Timeline abre o card.
