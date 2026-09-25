# Status — Feature 0014 (Autenticação do SQL Server para o PostgreSQL 18)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0014` no backend (`../prform.api-0014`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Cime.Auth no Postgres (Npgsql, schema `auth`, seeder idempotente, `InitialPostgres`, advisory lock) | 1 | — | ⬜ pendente | Claude | — |
| M1 | Migrador `tools/Cime.Auth.DataMigrator` (`check`/`schema`/`copy`/`verify`/`reset`) | 1 | — | ⬜ pendente | Claude | — |
| B2 | `AuthenticationContext` da API de PR no Postgres | 2 | B1 | ⬜ pendente | Claude | — |
| T1 | Ensaio local (Docker: SQL Server → Postgres 18; APIs contra o Postgres) | 2 | B1, M1 | ⬜ pendente | Claude | — |
| I1 | Terraform (`postgres-auth-connection`, `Seed__AdminPassword`) e runbook | 3 | B1, B2 | ⬜ pendente | Claude | — |
| Q1 | Ensaio com os dados reais no database novo, sem virar | 4 | T1, I1 | ⬜ pendente | usuário + Claude | — |
| Q2 | Virada (janela curta) | 4 | Q1 | ⬜ pendente | usuário + Claude | — |
| Q3 | Limpeza (SQL Server; opcional: tabelas legadas do `db31021`) | 5 | Q2 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Postgres 18 no MonsterASP em vez de MySQL (usuário, 2026-09-25): banco único no fim do caminho (o PR vai depois) e caminho livre para o .NET 10 (Npgsql acompanha o EF Core; o Pomelo ainda não tem EF Core 10).
- D1: database próprio, tabelas da auth no schema `auth`; o PR entra depois no mesmo database, com schema próprio. Nomes no padrão do EF.
- D2: chave nova `ConnectionStrings:AuthDatabase`, um secret para as duas APIs.
- D3: `datetime2` → `timestamp without time zone` (datas são `DateTime.Now`); `datetimeoffset` → `timestamptz` em UTC; `setval` das sequências após o `copy`.
- D4: migrações do zero, sem `HasData`; seeder idempotente; senha do admin por secret; `pg_advisory_lock`.
- D5: migrador dedicado com verificação por hash; "nenhum dado perdido" = `verify` com 0 diferenças; quem roda contra produção é o usuário.
- Descartadas: MySQL no `db31021` (B) e database MySQL separado (A), substituídas pelo Postgres.

## Pendências do usuário
- Criar o database Postgres 18 no MonsterASP e informar host, porta e nome do database (a senha vai direto para o Secret Manager/variável de ambiente). Ver se o painel tem backup e acesso externo para esse banco.
- Ligar o Docker Desktop para a T1.
- Combinar a janela da virada (Q2).

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Banco de dev = produção: nunca rodar a Cime.Auth/API local contra os bancos de produção com auto-migrate. B1/T1 usam bancos locais descartáveis (Docker, dados no scratchpad).
- Migrações: factory de design-time temporária com connection string fictícia, removida depois (padrão 0001/0011).

## Log
- 2026-09-25 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0014`.
- 2026-09-25 — Replanejada para PostgreSQL 18 (em vez de MySQL); o PR vai para o Postgres numa feature seguinte.
