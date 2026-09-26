# Status — Feature 0015 (API principal do MySQL para o PostgreSQL)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0015` no backend (`../prform.api-0015`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B0 | Auditoria de datas e textos (mapeamento final, contrato do JSON) | 1 | — | ✅ concluída | Claude | (este commit) |
| M1 | `tools/Cime.DataMigrator` (perfis `auth` e `prform`, tabelas do modelo EF, `reset` por schema) | 1 | — | ⬜ pendente | Claude | — |
| B1 | Default/Vacation/Timeline no Postgres (schemas, `InitialPostgres` ×3, lock, seeder, dev local) | 2 | B0 | ⬜ pendente | Claude | — |
| T1 | Ensaio local (Docker: MySQL 8 → Postgres 18) + comparação de contrato da API | 3 | B1, M1 | ⬜ pendente | Claude | — |
| I1 | Terraform (`postgres-prform-connection`) e runbook | 4 | B1 | ⬜ pendente | Claude | — |
| Q1 | Ensaio com os dados reais, sem virar | 5 | T1, I1 | ⬜ pendente | usuário + Claude | — |
| Q2 | Virada (janela curta) | 5 | Q1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- D1: mesmo database do auth (`db70140`), um schema por módulo: `prform`, `vacations`, `timeline`.
- D2: chave nova `ConnectionStrings:PrformDatabase` (secret `postgres-prform-connection`); a `DefaultConnection` (MySQL) fica para o rollback.
- D3: `InitialPostgres` por contexto; migrações de dados não são reescritas (os dados vêm da cópia); seeder só para o mínimo de um banco novo.
- D4: `pg_try_advisory_lock` para os 3 contextos.
- D5: migrador generalizado a partir do da 0014, lendo tabelas e ordem das FKs do modelo EF.
- D6: dev local em Postgres Docker (fim do "banco de dev = produção").
- B0: `DateTimeOffset` → `timestamptz`; `DateTime` → `timestamp without time zone` + conversor `Kind=Unspecified` (idêntico ao MySQL, JSON sem mudança). O front manda as datas de férias com `toISOString()` (hora 03:00 guardada). Comparações sem diferenciar maiúsculas explícitas em `RepositoryId`/`BranchPrefix`/`BranchName` (GitHub) e `EnvironmentName` (forms).

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Ao rodar a API localmente: integrações externas desligadas (SMTP, DevOps, GitHub, Teams, IA). Nunca contra os bancos de produção com auto-migrate.
- Migrações: factory de design-time temporária com connection string fictícia, removida depois.

## Log
- 2026-09-26 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0015`.
- 2026-09-26 — B0 concluída (auditoria de datas e textos, seção 1.1 do plano).
