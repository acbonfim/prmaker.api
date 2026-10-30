# Feature 0074 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0074` em `prform.api` (back + executor) e `solvace.prform.web/prform-app` (front).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Histórico: `versionNumber` + `createdByUserId` | back | — | 1 | ⬜ | | | | |
| F1 | Histórico: painel novo | front | — (B1 p/ integrar) | 1 | ⬜ | | | | |
| B2 | GitHub: branches, merge-base, commits desde a base | back | — | 1 | ⬜ | | | | |
| B3 | Perfis de destino em Skills Configurations + resolvedor | back | — | 2 | ⬜ | | | | |
| B4 | Lote: modelo, aplicação e endpoints | back | B2, B3 | 2 | ⬜ | | | | |
| E1 | Executor: pedido `promote` (cherry-pick) | back (tools) | B4 | 2 | ⬜ | | | | |
| B5 | Interpretar mensagem do Release Manager (IA) | back | B3 | 3 | ⬜ | | | | |
| F2 | Tela Promoções (manual) | front | B4 | 3 | ⬜ | | | | |
| F3 | Colar mensagem → preenche a tela | front | B5, F2 | 3 | ⬜ | | | | |
| F4 | Progresso, tabela final, copiar texto/tabela | front | F2, B4 | 4 | ⬜ | | | | |
| B6 | Sinalização e endurecimento | back | B4, E1 | 4 | ⬜ | | | | |
| Q1 | Integração, regressão e publicação | ambos | todas | 5 | ⬜ | | | | |

PR-A (histórico) = B1 + F1, pode ir sozinho. PR-B (promoção) = o resto.

## Decisões

| # | Situação | Observação |
|---|---|---|
| D1 | ⬜ a confirmar | Nome real das branches (`release-3.274.10`?): padrão = descobrir pelo remoto e perguntar na linha se ambíguo |
| D2 | ⬜ a confirmar | Nome da branch do cherry-pick: padrão `hotfix/<card>-<destino>` |
| D3 | ⬜ a confirmar | Padrão por perfil: cherry-pick; PR direto só quando o usuário marcar |
| D4 | ✅ adotada | Promoção **não** é `ExecutionPlan` (fase só admite análise/correção); lote com progresso próprio |
| D5 | ✅ adotada | Cartões da mensagem por regex; IA só para o texto livre (custo e confiabilidade) |

## Notas de handoff
- Spec escrita em 2026-10-30. Nada implementado ainda.
- Back: worktree `../prform.api-0074`. Front: criar `../solvace.prform.web/prform-app-0074` e linkar `node_modules` de `prform-app-0019`.

## Log
- 2026-10-30 — spec, plano e status criados.
