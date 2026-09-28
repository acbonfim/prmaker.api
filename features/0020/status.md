# Status — Feature 0020 (Timeline em tela cheia)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0020` no backend (`../prform.api-0020`) e no front (`../solvace.prform.web/prform-app-0020`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| F1 | "Importar do Teams" escondido (flag) + botão de tela cheia | 1 | — | ✅ concluída | Claude | front `c5f7b9a` |
| F2 | Tela cheia com animação de crescer/encolher | 1 | — | ✅ concluída | Claude | front `c5f7b9a` |
| T1 | Teste no navegador | 2 | F1, F2 | ✅ concluída | Claude | — |
| Q1 | PR e deploy | 3 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **T1** — build de produção servido localmente + Chrome headless (`puppeteer-core`), sessão falsa no `localStorage` e **toda chamada à API/auth interceptada** com dados falsos (25 registros no card 4242; nada sai para a produção); service worker desligado no teste (`setBypassServiceWorker`), senão ele atrapalha a interceptação.
  - Cabeçalho: botão "Importar do Teams" não aparece; botão de tela cheia sim.
  - **Abrir** (1440×900): 531×565 → 1200×828 em ~340 ms, amostras intermediárias crescendo (779×663, 1044×767, 1145×806…). Aberta: elemento no `<body>`, fundo presente, rolagem da página travada, título com `#4242`, ícone `close_fullscreen`; host mantém os 565 px.
  - **Fechar (Esc)**: encolhe 1200×828 → 531×565 e volta **exatamente** ao retângulo original, dentro do host, sem estilo inline sobrando, fundo removido, rolagem da página liberada.
  - **Estado**: rascunho digitado antes de abrir continua aberto e depois de fechar; lista continua no fim.
  - Clique no fundo fecha; botão abre/fecha; clique duplo rápido abre e fecha em fila (termina fechada, sem fundo sobrando); Esc no meio da abertura fecha ao fim dela e volta ao lugar.
  - **Camadas**: com o modal aberto, o ponto do sino do topo é o fundo; o tooltip do botão fica por cima do modal. (Achado e corrigido no T1: com z-index 998/999 o topo ficava por cima.)
  - Troca de rota com a timeline aberta: nada sobra no `<body>`, rolagem liberada. `prefers-reduced-motion`: abre sem animação.
  - Console: nenhum erro vindo da timeline. Os erros que aparecem são do mock incompleto (`UserIntegration/status` e configurações da tela devolvendo `[]`).

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0020` no backend e no front.
- 2026-09-28 — F1, F2 e T1 concluídas (front `c5f7b9a`). Falta o Q1 (PRs e deploy).
