# Status — Feature 0019 (PRMake como PWA instalável)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0019` no backend (`../prform.api-0019`) e no front (`../solvace.prform.web/prform-app-0019`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| F1 | Manifest, ícones e metas do `index.html` | 1 | — | ⬜ pendente | — | — |
| F2 | `@angular/service-worker` + `ngsw-config.json` (só o shell, sem dados da API) | 1 | — | ⬜ pendente | — | — |
| F4 | nginx: SW/`ngsw.json` sem cache, manifest, ícones | 1 | — | ⬜ pendente | — | — |
| F3 | `PwaService`: aviso "Nova versão — Atualizar" | 2 | F2 | ⬜ pendente | — | — |
| T1 | Teste local em container (instalação, cabeçalhos, atualização A→B, API/tempo real) | 3 | F1–F4 | ⬜ pendente | — | — |
| Q1 | Deploy e teste em produção | 4 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **Pendente de decisão (F1)**: arte dos ícones — o `logo.svg` é horizontal; sem símbolo quadrado da marca, usar monograma "PR" sobre a cor primária.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0019` no backend e no front.
