# Status — Feature 0015 (API principal do MySQL para o PostgreSQL)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0015` no backend (`../prform.api-0015`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B0 | Auditoria de datas e textos (mapeamento final, contrato do JSON) | 1 | — | ✅ concluída | Claude | (este commit) |
| M1 | `tools/Cime.DataMigrator` (perfis `auth` e `prform`, tabelas do modelo EF, `reset` por schema) | 1 | — | ✅ concluída | Claude | `a662ed8`, (fix T1) |
| B1 | Default/Vacation/Timeline no Postgres (schemas, `InitialPostgres` ×3, lock, dev local) | 2 | B0 | ✅ concluída | Claude | `825e59f` |
| T1 | Ensaio local (Docker: MySQL 8 → Postgres 18) + comparação de contrato da API | 3 | B1, M1 | ✅ concluída | Claude | — (testes no scratchpad) |
| I1 | Terraform (`postgres-prform-connection`) e runbook | 4 | B1 | ✅ concluída | Claude | (commit I1) |
| Q1 | Ensaio com os dados reais, sem virar | 5 | T1, I1 | ✅ concluída | usuário + Claude | — |
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

## Notas da implementação
- **B1**: `Cime.BuildingBlocks.Persistence` (convenção `UseUnspecifiedDateTimes` + `PostgresMigrationLock`). Migrações validadas num Postgres local com o schema `auth`: duas instâncias subindo juntas migram em série; cada contexto sobe, desce e sobe; nada fora dos 3 schemas é tocado. Tipos gerados conforme a B0.
- **M1**: perfil `auth` revalidado no cenário da 0014 (SQL Server com o schema da auth antiga + casos de borda): `check` acusa `\0`/espaço no fim; `copy` + `verify` = 0; `reset` só apaga `auth`.
- **T1 — ambiente**: auth local (usuários do ensaio da 0014, api-keys da maria/gestor e do admin); MySQL 8 (`utf8mb4_general_ci` no servidor) com o schema criado pela **API antiga** (código da 0014) nas próprias migrações; dados criados **pela API antiga** (saldo e pedidos de férias com aprovação/autorização, timeline, handover privado, registro de PR e resumo) + por SQL (PRs do GitHub normal e legado com caixa diferente, plugin de IA com JSON/FieldSettings/PersonalFields e acentos/emoji, plugin apagado, integração pessoal, forms, card com espaço no fim, timeline importada do Teams e sem origem, linha na `PullRequestsLegacyBackup`).
- **T1 — migrador**: `check` ok com avisos certos (collation, tabela fora do modelo, espaço no fim); `copy` + `verify` = **0 diferenças**; conferência independente: férias com a hora 03:00 preservada, períodos à meia-noite, `timestamptz` em UTC, `DeletedAt`, JSON intacto, `GithubPrId` grande, sequências no próximo id.
- **T1 — contrato**: 27 leituras (card, PRs do GitHub, handovers, timeline, férias, calendário, saldos, departamentos, plugins, forms, integrações) contra a API antiga (MySQL) e a nova (Postgres): **status HTTP e JSON idênticos** (inclusive `DateTime` sem `Z` e `DateTimeOffset` com `+00:00`; form por `qa` acha `QA`).
- **T1 — escritas na API nova**: saldo (a checagem de duplicado com parâmetro `DateTime` UTC funciona), pedido de férias, aprovação/autorização (`UtcNow`), calendário, exclusão; timeline criar/editar/apagar; visibilidade do handover e rota pública; registro de PR (atualizar e novo card) e resumo. Nenhum erro do Npgsql; os únicos 500 foram regras de negócio esperadas.
- **T1 — maiúsculas**: os predicados do upsert/legado do GitHub e do form, executados no Postgres, acham os registros gravados com outra caixa (`lower(...)`); o `==` puro não acharia (controle).
- **T1 — negativos**: `copy` com destino cheio recusa; `reset` com nome errado recusa; `reset prform` apaga só `prform/vacations/timeline` (o `auth` fica intacto); ciclo refeito = 0.

## Q1 — andamento (2026-09-26)
- **A origem de produção é MariaDB 10.11** (`5.5.5-10.11.15-MariaDB`), não MySQL. Collation `utf8mb4_general_ci` (**PAD SPACE**: ignora espaço no fim). O `check` não achou nenhum valor com espaço no início/fim nas colunas de busca, então nenhum dado é afetado. O migrador foi ajustado (o MariaDB não tem `PAD_ATTRIBUTE`).
- Nomes de tabela em minúsculas no MariaDB (`lower_case_table_names`); as tabelas do modelo casam sem diferenciar maiúsculas.
- `check` ok: 621 linhas (Forms 5, Handovers 21, Plugins 11, PluginConfigurations 11, PullRequests 221, UserPluginConfigurations 6, PullRequestsGithub 234, UserVacationBalances 9, VacationRequests 9, TimelineEntries 94).
- **Fora do modelo (não copiadas; ficam para o backup final do MariaDB, 0016 B2)**: `aspnet*` legadas (4 papéis, 1 usuário), `services`/`userservices` (1/1), `userforgetcodes` (0), `pullrequestslegacybackup` (217).
- `copy` + `verify` = **0 diferenças** (01:12 UTC).
- ✅ **Contrato com dados reais**: API de produção (MariaDB) × API nova local (cópia no Postgres), 26 leituras com a api-key do usuário (`q1-contract.py`, só status e caminhos divergentes):
  - 1ª rodada: 22/26. Duas diferenças eram de **ordem** (mesmo conjunto de registros): plugins sem `ORDER BY` (o MariaDB devolve pela PK) e saldos de férias com empate na data. Corrigido com `OrderBy(Id)`/`ThenBy(Id)` (commit do fix B1).
  - 2ª rodada: **24/26**. As 2 restantes (`UserIntegration` e `/status`) são **de ambiente**: a API local não tem `UserIntegrations:EncryptionKey` (secret de produção), então os tokens pessoais não são decifrados. O `verify` prova que as 6 linhas de `UserPluginConfigurations` são idênticas byte a byte, e a decifração depende só da chave e dos bytes.
- ✅ `reset --confirm db70140` do perfil `prform`: sobrou só o schema `auth` (usuários intactos).

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Ao rodar a API localmente: integrações externas desligadas (SMTP, DevOps, GitHub, Teams, IA). Nunca contra os bancos de produção com auto-migrate.
- Migrações: factory de design-time temporária com connection string fictícia, removida depois.

## Log
- 2026-09-26 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0015`.
- 2026-09-26 — B0 concluída (auditoria de datas e textos, seção 1.1 do plano).
- 2026-09-26 — B1, M1 e T1 concluídas (ensaio local com contrato da API idêntico).
- 2026-09-26 — Q1 concluída: cópia real (MariaDB → Postgres) com 0 diferenças; contrato 24/26 (2 de ambiente); fix de ordenação.
