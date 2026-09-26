# Status — Feature 0017 (Upgrade para o .NET 10)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0017` (`../prform.api-0017`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | TFM `net10.0`, `global.json`, pacotes 10.x, IdentityModel 8.x, remoção de pacotes sem uso | 1 | — | ⬜ pendente | Claude | — |
| B2 | `Asp.Versioning` + Swashbuckle 10 (OpenApi 2) | 2 | B1 | ⬜ pendente | Claude | — |
| B3 | EF Core 10: modelo × snapshot nos 4 contextos | 2 | B1 | ⬜ pendente | Claude | — |
| B4 | Docker 10.0, workflow do relay, docs | 3 | B1 | ⬜ pendente | Claude | — |
| T1 | Ensaio local (contrato 8 × 10, tokens cruzados, relay, Swagger) | 4 | B1–B4 | ⬜ pendente | Claude | — |
| Q1 | Deploy | 5 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Log
- 2026-09-26 — Planejamento (spec, plano, status); worktree `feature/0017`.
