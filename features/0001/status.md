# Feature 0001 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0001` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo de dados e migração | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-23 | 2026-09-23 | c48c9d7 |
| B2 | GitHubService multi-repo | back | — | 1 | 🟡 | Claude (sessão principal) | 2026-09-23 | | |
| F1 | Estado compartilhado + componentes extraídos | front | — | 1 | 🟡 | Claude (sessão principal) | 2026-09-23 | | |
| B3 | Aplicação e endpoints de PR do card | back | B1, B2 | 2 | ⬜ | | | | |
| F2 | Reestruturação da tela principal | front | F1 | 2 | ⬜ | | | | |
| F3 | Modal "Abrir PR" | front | F1 (+B3 p/ integrar) | 2 | ⬜ | | | | |
| B4 | Sync de status e hardening | back | B3 | 3 | ⬜ | | | | |
| F5 | IA: stepper vertical multi-repo | front | F1 (+B3 p/ integrar) | 3 | ⬜ | | | | |
| F4 | Painel de PRs abertos do card | front | F3, B4 | 4 | ⬜ | | | | |
| F6 | IA: passo Resumo + prompt multi-repo | front | F5 | 4 | ⬜ | | | | |
| Q1 | Integração, regressão e publicação | ambos | todas | 5 | ⬜ | | | | |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | a confirmar | |
| D2 | a confirmar | |
| D3 | a confirmar | |
| D4 | a confirmar | |
| D5 | a confirmar | |
| D6 | a confirmar | |
| D7 | a confirmar | spec 4.3 truncada |
| D8 | a confirmar | |
| D9 | a confirmar | |
| D10 | a confirmar | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

### B1 — Modelo de dados e migração ✅
- **Feito**: `Entities/PullRequestGithub.cs` (tabela `PullRequestsGithub`), `Enums/PullRequestGithubStatus.cs` (`OPEN/MERGED/CLOSED`, `IsTerminal`, `From(state, merged)`), `OpenPullRequestGithubRequest`, `UpdatePullRequestGithubRequest`, `PullRequestGithubResponse` (inclui `StatusStale` e `AlreadyExisted`). `PullRequestRegister` ganhou `GithubPullRequests` (backing field `_githubPullRequests`), `MaxCardNumberLength = 50`; `SetDescription`/`SetRootCause` aceitam vazio.
- **Migrações**: `20260924024813_ConsolidatePullRequestPerCard` (TRIM + consolidação D1 via `ROW_NUMBER` → exige MySQL 8+/MariaDB 10.2+; `CardNumber` vira `varchar(50)`; índice único; `BranchPrefix/BranchName` nullable) e `20260924024910_AddPullRequestGithub`. O `Down` da consolidação só reverte schema.
- **Desvios**: não usei `[Obsolete]` nas colunas legadas (geraria warnings em todo lugar) — ficou um comentário. `PullRequestGithub` não implementa `IDescribable` (descrição pode ser vazia e o setter é privado).
- **⚠️ Banco**: o `appsettings.Development.json` aponta para o **mesmo banco de produção** (MonsterASP) e a API faz auto-migrate no startup → **não rodar a API desta branch localmente** sem trocar a connection string. Para gerar migrações sem conectar no banco (`ServerVersion.AutoDetect` conecta), criei temporariamente um `IDesignTimeDbContextFactory` com `MySqlServerVersion(8.0.36)` e removi em seguida — repetir o truque se outra fase precisar de migração.
- **Pendente (Q1)**: migrações **não foram aplicadas** em nenhum banco. Antes de aplicar, conferir:
  ```sql
  SELECT MAX(CHAR_LENGTH(TRIM(CardNumber))) FROM PullRequests;          -- precisa ser <= 50
  SELECT COUNT(*) total, COUNT(DISTINCT TRIM(CardNumber)) cards FROM PullRequests; -- depois: total = cards
  SELECT VERSION();                                                      -- MySQL 8+ (ROW_NUMBER)
  ```
- **Para B3**: `PullRequestRegisterRequest.RepositoryId` ainda é `required`; `ToResponse()` ainda devolve branch com fallback `hotfix/`; `Create` ainda faz upsert por card+repo — tudo isso é escopo da B3.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-23 | — | Plano criado; branches `feature/0001` criadas a partir de `master` nos dois repos. |
| 2026-09-23 | B1, B2, F1 | Iniciadas (onda 1, sessão única, em sequência). |
| 2026-09-23 | B1 | Concluída (c48c9d7). Migrações geradas, não aplicadas. |
