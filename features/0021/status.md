# Status — Feature 0021 (telas menores/celular e tempo real a partir das skills)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0021` no backend (`../prform.api-0021`) e no front (`../solvace.prform.web/prform-app-0021`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Backend: `handover-saved` e grupo global `pullrequest-recent` | 1 | — | ✅ concluída | Claude | backend `0fd28b6` |
| F1 | Rodapé: Detalhes em Ações DevOps, Ações inteligentes, copiar nos painéis | 1 | — | ✅ concluída | Claude | front `f09623c` |
| F3 | Popovers sempre dentro da tela | 1 | — | ✅ concluída | Claude | front `f09623c` |
| F2 | Barra de ações com transbordo "⋯" | 2 | F1 | ✅ concluída | Claude | front `f09623c` |
| F4 | Tempo real no front (tela do card, handover, home) | 2 | B1 | ✅ concluída | Claude | front `f09623c` |
| T1 | Build + teste visual com a API simulada | 3 | B1–F4 | ✅ concluída | Claude | — |
| Q1 | Merge na master e deploy | 4 | T1 | 🟨 em andamento | Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **Item 6 (tempo real)**: a tela do card já escutava `register-saved`/`github-pr-opened` e o relay respondia 202 aos publishes da skill (card 74517, 2026-09-28 21:02). Faltavam: abrir PR em card sem registro (o backend cria o registro, mas a tela só recarregava a lista), handover salvo (não emitia nada) e a home (não escutava). O `WsService` passou a contar inscrições por grupo (as duas listas da home usam o mesmo grupo).
- **Bug antigo corrigido no T1**: em telas ≤ 900 px o painel "Pull Requests" tinha `flex: 1 1 0` numa coluna de altura automática e encolhia a 0 — a lista de PRs não aparecia no celular.
- **T1** (`ng serve` + Chrome headless/puppeteer-core, API simulada por interceptação): rodapé em 1440/1024 com os 5 botões; 768 → "Ações inteligentes" no ⋯; 390 → Abrir PR e Salvar visíveis, o resto no ⋯. Ações DevOps (inclusive aberto pelo ⋯), Ações inteligentes, ⋯, Descrição (folha no celular) e ⚡ → Alterar status: todos inteiros dentro da janela; sem rolagem horizontal. `ng build --configuration production` e `dotnet build` ok.
- Fora do escopo, visto nos logs: o front antigo em `prmakeweb.runasp.net` ainda é acessado e tenta `api.softhouse.app.br/ws/negotiate` (404) — sem tempo real lá.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0021` no backend e no front.
- 2026-09-28 — B1, F1–F4 e T1 concluídas (backend `0fd28b6`, front `f09623c`). Q1: merge direto na master (pedido do usuário) e deploy pelo push.
