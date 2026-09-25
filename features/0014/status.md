# Status — Feature 0014 (Autenticação do SQL Server para o MySQL)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0014` no backend (`../prform.api-0014`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Cime.Auth no MySQL (Pomelo, collation, seeder idempotente, `InitialMySql`, `GET_LOCK`) | 1 | — | ⬜ pendente | Claude | — |
| M1 | Migrador `tools/Cime.Auth.DataMigrator` (`check`/`copy`/`verify`) | 1 | — | ⬜ pendente | Claude | — |
| B2 | `AuthenticationContext` da API de PR no MySQL | 2 | B1 | ⬜ pendente | Claude | — |
| T1 | Ensaio local (SQL Server Docker → MySQL local, APIs contra o MySQL) | 2 | B1, M1 | ⬜ pendente | Claude | — |
| I1 | Terraform (secret + `ConnectionStrings__AuthDatabase`) e runbook | 3 | B1, B2 | ⬜ pendente | Claude | — |
| Q1 | Ensaio com os dados de produção, sem virar | 4 | T1, I1 | ⬜ pendente | usuário + Claude | — |
| Q2 | Virada (janela curta) | 4 | Q1 | ⬜ pendente | usuário + Claude | — |
| Q3 | Limpeza do SQL Server (~30 dias depois) | 5 | Q2 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- D1: database MySQL próprio para o auth no mesmo servidor (no MySQL, schema = database). Fallback: mesmo `db31021` com `__EFMigrationsHistory_Auth`, se o plano do MonsterASP não permitir outro database.
- D2: chave nova `ConnectionStrings:AuthDatabase` (a env entra antes do deploy; rollback pela revisão anterior).
- D3: migrações MySQL do zero, sem `HasData` (seeder idempotente; senha do admin por config). Collation `utf8mb4_0900_as_ci`.
- D4: migrador dedicado com verificação por hash. "Nenhum dado perdido" = `verify` com 0 diferenças. Quem roda contra produção é o usuário.

## Pendências do usuário
- Confirmar no painel do MonsterASP se o plano permite criar mais um database MySQL (e um usuário read-only).
- Ligar o Docker Desktop para a T1 (SQL Server de teste).
- Combinar a janela da virada (Q2).

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Banco de dev = produção: nunca rodar a Cime.Auth/API local contra os bancos de produção com auto-migrate; B1/T1 usam bancos locais descartáveis (dados no scratchpad).
- Migrações: factory de design-time temporária com connection string fictícia e `MySqlServerVersion(8.0.36)`, removida depois (padrão 0001/0011).

## Log
- 2026-09-25 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0014`.
