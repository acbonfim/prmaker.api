# Feature 0074 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0074` em `prform.api` (back + executor) e `solvace.prform.web/prform-app` (front).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Histórico: `versionNumber` + `createdByUserId` | back | — | 1 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | f265b1b |
| F1 | Histórico: painel novo | front | — (B1 p/ integrar) | 1 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | front d5405fd |
| B2 | GitHub: branches, merge-base, commits desde a base | back | — | 1 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | 5f84339 |
| B3 | Perfis de destino em Skills Configurations + resolvedor | back | — | 2 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | 4482828 |
| B4 | Lote: modelo, aplicação e endpoints | back | B2, B3 | 2 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | 63332a8, c3b45df |
| E1 | Executor: pedido `promote` (cherry-pick) | back (tools) | B4 | 2 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | e767a78 |
| B5 | Interpretar mensagem do Release Manager (IA) | back | B3 | 3 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | c3b45df |
| F2 | Tela Promoções (manual) | front | B4 | 3 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | front 26261fb |
| F3 | Colar mensagem → preenche a tela | front | B5, F2 | 3 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | front 26261fb |
| F4 | Progresso, tabela final, copiar texto/tabela | front | F2, B4 | 4 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | front 26261fb |
| B6 | Sinalização e endurecimento | back | B4, E1 | 4 | ✅ | Claude (sessão principal) | 2026-10-30 | 2026-10-30 | c3b45df |
| Q1 | Integração, regressão e publicação | ambos | todas | 5 | 🟡 | Claude (sessão principal) | 2026-10-30 |  |  |

PR-A (histórico) = B1 + F1, pode ir sozinho. PR-B (promoção) = o resto.

## Decisões

| # | Situação | Observação |
|---|---|---|
| D1 | ✅ adotada (confirmar com o time) | Nome real das branches: descoberto pelo remoto; ambíguo = o usuário escolhe na linha |
| D2 | ✅ adotada (confirmar com o time) | Nome da branch do cherry-pick: `{branch}-{target}` (`PromotionBranchPattern`), nunca reaproveita (`-2`, `-3`) |
| D3 | ✅ adotada (confirmar com o time) | Cherry-pick por padrão; PR direto quando marcado ou quando o destino é a base de corte da branch principal |
| D6 | ✅ adotada | Progresso do lote por consulta a cada 3 s (o relay só acorda o executor) |
| D7 | ✅ adotada | Itens de PR direto saem na requisição (até 20); o executor cobre o resto |
| D4 | ✅ adotada | Promoção **não** é `ExecutionPlan` (fase só admite análise/correção); lote com progresso próprio |
| D5 | ✅ adotada | Cartões da mensagem por regex; IA só para o texto livre (custo e confiabilidade) |

## Notas de handoff
- Back: worktree `../prform.api-0074`. Front: `../solvace.prform.web/prform-app-0074` (com `node_modules` linkado de `prform-app-0019`).
- Testes: back `dotnet test` em `solvace.prform.tests` (68), `solvace.executionplans.tests` (11), `tools/Cime.ExecutionAgent.Tests` (14, git real);
  front `plan-history.component.spec.ts` e `promotion-copy.spec.ts` (12) — o `ng test` do front não compila os specs antigos (`async` removido do
  Angular), então rode com um `tsconfig.spec` só com os specs novos e `provideZonelessChangeDetection()`.
- e2e (Q1, em `.t0074/`, fora do git): Postgres isolado, GitHub falso apoiado em git bare real, auth e Gemini falsos, API local, executor real
  (inclusive o binário publicado trimmed) e Chrome headless (puppeteer): lote manual de 3 cards → PRs abertos, conflito reportado, base de corte
  certa, idempotência, "Copiar como texto/tabela", colar mensagem (com e sem IA) e o painel de histórico.
- Agente do executor sobe para **1.1.0** (autoatualiza as máquinas). Migrações: `SeedPromotionProfiles` (config) e `AddPromotionBatches` (tabelas).
- Não coberto por teste automatizado: chamadas reais à API do GitHub (só o fake) e o Azure DevOps (sem o plugin, a prévia segue sem título).

## Log
- 2026-10-30 — spec, plano e status criados.
- 2026-10-30 — B1–B6, E1, F1–F4 implementadas; e2e local verde; achados no e2e corrigidos: base de corte pela release, entidades pendentes do `Open`
  que falhou, idempotência dos itens de cherry-pick, cache de branches.
- 2026-10-30 — ajustes pós-uso: loading/skeleton/animações em todas as telas novas; nomes literais (`release-version`/`hotfix-version`, migração
  `UseLiteralPromotionBranches`); branch do card e base pelo registro do PRMake (sem palpite); seletor de branches do repositório; mensagens
  claras ("sem commits", "não existe no repositório"); servidor exige o registro do PRMake ao criar o lote.
