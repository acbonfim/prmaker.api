# Feature 0065 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0065` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Endpoint `Home/cards/by-numbers` | back | — | 1 | ⬜ | Claude (sessão principal) | | | |
| F1 | `TabsService` + persistência + rotas com `data.tab` | front | — | 1 | ⬜ | Claude (sessão principal) | | | |
| F2 | `TabRouteReuseStrategy` + rolagem por aba | front | F1 | 2 | ⬜ | Claude (sessão principal) | | | |
| F3 | `<app-tab-bar>` + layout do PageContainer | front | F1 | 2 | ⬜ | Claude (sessão principal) | | | |
| F4 | Rótulo do card (título, plano, PRs, amarelo) | front | B1, F3 | 3 | ⬜ | Claude (sessão principal) | | | |
| F5 | Integração com register/home/sino + dirty | front | F2, F3 | 3 | ⬜ | Claude (sessão principal) | | | |
| Q1 | Build, teste manual, regressão, PRs | ambos | todas | 4 | ⬜ | Claude + usuário | | | |

## Decisões

Defaults em `spec.md` (D1–D12). Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1–D12 | ⬜ propostas | Escolhidas pela análise do código; aguardam confirmação do usuário (principalmente D3 "sem interação = tempo fora de foco", D5 `localStorage` por usuário e D12 fora do escopo). |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

## Log

- 2026-10-05 — Spec, plano e status escritos; worktrees `prform.api-0065` e `prform-app-0065` criados a partir da `master`.
