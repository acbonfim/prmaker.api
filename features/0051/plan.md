# Plano — Feature 0051

Branch `feature/0051` em `prform.api-0051` (API) e `prform-app-0051` (front, `node_modules` → `prform-app-0019`).

| Fase | O quê | Depende |
|---|---|---|
| B1 | `GET api/v1/Home/cards?scope=mine\|participated&take=` no host (`Home/HomeCardsService`, lê `DefaultContext`, `TimelineContext`, `ExecutionPlanContext`, `AuthenticationContext`). `mine` = cards registrados pelo usuário (mesma regra do `GetRecentByUser`). `participated` = cards em que o usuário: atualizou o registro (`CreatedBy/UpdatedBy`), abriu PR (`PullRequestGithub.UserId`), escreveu na Timeline (`UserId`), comentou no plano (`ExecutionNote.AuthorUserId`), criou o plano, respondeu pergunta / pausou-continuou o plano (pelo nome), salvou handover — sem os cards que já são dele (`mine`), ordenados pela última interação dele. Cada card: participantes (id + nome, dono primeiro), por que participou, PRs (status/draft/url), plano mais recente (fase, status, etapas feitas/total, etapa atual, aguardando você, perguntas abertas, comentários), Timeline (total + última entrada), handover, RCA/descrição/resumo, última atividade (quem/quando/o quê) | — |
| B2 | `POST api/v1/Azure/cards/summary` (ids) → título, estado, coluna do board, tipo, responsável, alteração, link no DevOps — uma chamada `workitemsbatch` (`errorPolicy=omit`); sem integração configurada a tela segue sem o status | — |
| F1 | Home: remove "Acesso rápido" e "Suas férias"; duas colunas "Seus últimos cards" e "Cards que você participou" (componente único `app-home-cards`, `scope`), cartão rico: AB#, título/estado do DevOps, avatares sobrepostos (`app-avatar-stack`, +N), selos do plano/pendência, chips dos PRs (abre o GitHub), última entrada da Timeline, última atividade, atalhos Plano / Timeline / PRs / DevOps; tempo real (`pullRequestRecentUpdated`, timeline e plano) com recarga silenciosa | B1, B2 |
| F2 | Tela do card: `?focus=plan\|timeline\|prs` rola até o painel (no celular troca a aba) | — |
| T1 | Build back + front; teste local (Postgres isolado) do endpoint com dados de várias fontes | todas |

Ondas: **1** — B1, B2, F2. **2** — F1. **3** — T1.
