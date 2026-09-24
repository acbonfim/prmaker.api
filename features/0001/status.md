# Feature 0001 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0001` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo de dados e migração | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-23 | 2026-09-23 | c48c9d7 |
| B2 | GitHubService multi-repo | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-23 | 2026-09-23 | 701a9d6 |
| F1 | Estado compartilhado + componentes extraídos | front | — | 1 | ✅ | Claude (sessão principal) | 2026-09-23 | 2026-09-23 | front c8936ee |
| B3 | Aplicação e endpoints de PR do card | back | B1, B2 | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 3041985 |
| F2 | Reestruturação da tela principal | front | F1 | 2 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F3 | Modal "Abrir PR" | front | F1 (+B3 p/ integrar) | 2 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
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

### B2 — GitHubService multi-repo ✅
- **Feito** (`Solvace.GitHub`): `IGitHubService` ganhou `UpdatePullRequestAsync(repo, number, title, body)`, `ListRepositoriesAsync()` e `GetPullRequestsStatusAsync(IEnumerable<(Repository, Number)>)`; `CreatePullRequestAsync(..., repository = null)` (parâmetro opcional no fim para não quebrar chamadas). Novas responses `RepositoryResponse` (`id`=nome do repo, `label`, `private`, `defaultBranch`) e `PullRequestStatusResponse`. `PullRequestResponse.Number` virou `int` e ganhou `Repository`, `Status`, `AlreadyExisted` (nenhum consumidor no front usava `number`).
- **Comportamento**: PR já existente para head→base (422 "already exists") → busca o PR aberto e devolve com `AlreadyExisted = true`. Repos: `GetAllForOrg(Owner)` com fallback para `GetAllForCurrent()` filtrado pelo owner (se o Owner for usuário), sem arquivados, cache 10 min (`github:repositories:{owner}`). Status: paralelo com `SemaphoreSlim(5)`, cache 1 min por PR (`github:pr-status:{owner}/{repo}#{n}`); falha vira `Error` no item (não lança). Mensagens de erro dos métodos novos incluem o detalhe da API do GitHub.
- **Endpoints**: `GET /GitHub/repositories`. `POST /GitHub/pull-request` aceita `?repository=` e **passa a responder 400 quando há `error`** (antes respondia 200 com `error` no corpo — não há consumidor no front).
- **Para B3**: status é calculado com `PullRequestGithubStatus.From` (prform.domain) — use `PullRequestStatusResponse.Status` direto em `PullRequestGithub.SetStatus`. Octokit 9 não aceita `CancellationToken`; timeout por chamada fica para a B4 (`Task.WaitAsync`).
- **Pendente**: não foi testado contra o GitHub real (rodar a API local aplicaria as migrações no banco de produção — ver B1). Validar via Swagger em QA na Q1.

### F1 — Estado compartilhado + componentes extraídos ✅ (repo front)
- **Feito**: `services/card-pr-state.service.ts` (signals `cardNumber`, `register`, `description`, `rootCause` — markdown, `null` = nada carregado —, `githubPrs`, `repositories`, `repositoriesWithPr`; `loadRegister`, `setContent`, `upsertGithubPr`, `reset`, `loadRepositories(fallback)` com fallback para `ActiveRepositories`). `services/pull-request.service.ts` com os tipos/métodos do contrato §3. `helpers/markdown.ts` (`mdToHtml`/`htmlToMd`, com a regra de tachado). `interfaces/RepoOption.ts`.
- **Componentes** (standalone, signals `input()/model()`):
  - `app-markdown-editor` — p-editor com valor em markdown; re-renderiza só em mudança externa e ignora `onTextChange` com `source !== 'user'` (evita eco entre dois editores abertos — útil para o popover da F2/F3). Estilos do Quill migrados para cá.
  - `app-pr-description-panel` / `app-root-cause-panel` — `app-card-panel` + editor ligado ao `CardPrStateService`; aceitam `[panel-actions]` e `[loading]`. **Use-os no modal/popovers (F2/F3)**: editar em qualquer um reflete nos outros.
  - `app-branch-input` (`[(prefix)]`, `[(name)]`, `[readonly]`), `app-repo-autocomplete` (`[options]`, `[exclude]`, `[(value)]` objeto ou texto digitado, `(selected)`, `[loading]`, `[readonly]`), `app-target-branch-toggle` (`[options]`, `[(value)]`, `[loading]`, `[disabled]`).
- **Tela**: `register.component` usa os componentes; o espelhamento estado → `this.pullRequest` é feito por `effect` (`onSharedContentChange`). CSS migrado saiu do `register.component.css`. Novo skeleton global `.cime-skeleton` em `styles.scss`.
- **Validação**: `ng build --configuration development` ok (0 erros; warnings só pré-existentes). **Regressão visual/manual não foi feita** (não subi o front) — conferir na F2 ou na Q1: prefixo com duplo clique, autocomplete de repo, toggle, editores, IA preenchendo descrição/RC, Limpar.
- **Obs.**: `src/environments/environment.ts` está modificado no working tree do front (apontando p/ localhost/`production:false`) — **não é desta fase e não foi commitado**.

### B3 — Aplicação e endpoints de PR do card ✅
- **Desvio de arquitetura**: a orquestração ficou em `Solvace.GitHub/.../Services/PullRequestGithubApplication.cs` (`IPullRequestGithubApplication`, registrada em `AddGitHubModule`), não no `prform.application` — o módulo GitHub já referencia `prform.application` (cache de plugins), então o inverso criaria ciclo. Ela usa o `DefaultContext` direto (mesmo padrão das outras applications).
- **Endpoints** (conforme contrato §3): `POST /PullRequest/{card}/github`, `PUT /PullRequest/{card}/github/{id}`, `GET /PullRequest/{card}/github?refreshStatus=true`. Erros de validação/GitHub viram `DomainException` → controller responde `400 { error }`.
- **Abrir PR**: cria o `PullRequestRegister` do card se ainda não existir (sem descrição/RC, `FormId = 1`); idempotente por `repo + número` (retorno `alreadyExisted`). O PR é criado no GitHub antes de gravar no banco — se o banco falhar, repetir a chamada só registra o existente.
- **Listar**: só PRs não terminais são consultados; status só é gravado quando muda (`StatusSyncedAt` = última mudança de status, não "última consulta"); falha do GitHub → `statusStale: true` com o status persistido.
- **Registro do card** (`POST /PullRequest`): upsert só por `CardNumber`; `description`/`rootCause` opcionais — **`null` mantém o valor atual, `""` limpa**; `branchPrefix/branchName/repositoryId` aceitos e ignorados (skill `gerar-prmake` continua funcionando). Corrigido bug antigo: quando nada mudava, o `Create` caía no `AddAsync` e duplicava o card.
- **`GetByCardNumber`**: ignora `repositoryId`; inclui `githubPullRequests` (status persistido, sem refresh). `branchPrefix/branchName/repositoryId` do response vêm do PR do GitHub mais recente (fallback: legado da linha) — o front atual continua funcionando até a F2.
- **`GetRecentByUser`**: repo/branch do PR do GitHub mais recente, fallback no legado.
- **Validação**: build ok; `has-pending-model-changes` = sem mudanças de modelo; teste descartável (SQLite em memória + `IGitHubService` fake, no scratchpad da sessão) com 14 cenários — abrir com card novo, idempotência, 2º repo, erro do GitHub, update, refresh MERGED, `statusStale`, terminal não reconsultado, upsert null/vazio, payload legado, `GetRecentByUser` — **todos passaram**. Não testado contra MySQL/GitHub reais.
- **Não feito**: entrada automática na Timeline ao abrir PR (o módulo GitHub não referencia o Timeline; avaliar na Q1).
- **Incidente (corrigido)**: os commits B1 (c48c9d7) e B3 (3041985) levaram por engano versões vazias de `features/0002/spec.md` e `features/README.MD` que estavam em stage; removidos no commit seguinte, stage do usuário restaurado.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-23 | — | Plano criado; branches `feature/0001` criadas a partir de `master` nos dois repos. |
| 2026-09-23 | B1, B2, F1 | Iniciadas (onda 1, sessão única, em sequência). |
| 2026-09-23 | B1 | Concluída (c48c9d7). Migrações geradas, não aplicadas. |
| 2026-09-23 | B2 | Concluída (701a9d6). Sem teste contra o GitHub real. |
| 2026-09-23 | F1 | Concluída (front c8936ee). Onda 1 fechada; liberadas B3, F2, F3. |
| 2026-09-24 | B3, F2, F3 | Iniciadas (onda 2, sessão única, em sequência). |
| 2026-09-24 | B3 | Concluída (3041985). Teste de lógica com SQLite + GitHub fake: 14/14. |
