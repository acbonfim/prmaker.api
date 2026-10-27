# Status — Feature 0073 (busca da ER fora da memória)

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B0 | ⏳ pendente | — | — |
| B1 | ⏳ pendente | — | — |
| B2 | ⏳ pendente | — | — |
| B3 | ⏳ pendente | — | — |
| B4 | ⏳ pendente | — | — |
| T1 | ⏳ pendente | — | — |
| T2 | ⏳ pendente | — | — |

## Contexto / handoff

- 2026-10-27: OOM em `ReverseEngineering` (1 GiB, revisão 00136). Paliativo: 2 GiB (PR #133, revisão 00137, aplicado com
  terraform; state local com backup `terraform.tfstate.pre-2gi`). Spec escrita a partir dos logs do Cloud Run e da leitura de
  `ReverseSearch.cs`; a causa exata do pico (EF/JSON × concorrência) ainda não foi medida — é o B0.
- Harness local da 0070 em `.t0070/` (Postgres isolado porta 55470, Base real semeada) serve de ponto de partida.

## Log

- 2026-10-27 — feature aberta (spec/plan/status).
