# Feature 0001 — Abrir PR pelo backend + PRs multi-repositório

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch (nos dois repos): `feature/0001` criada a partir de `master`.

| Repo | Caminho | Sigla das fases |
|---|---|---|
| Backend (este) | `prform.api` | `B*` |
| Frontend (Angular 20 + PrimeNG 20 + Material) | `../solvace.prform.web/prform-app` | `F*` |

---

## 1. Análise

### 1.1 Como está hoje

**Backend**
- `PullRequestRegister` (`CIME/modules/Solvace.PullRequests/src/solvace.prform.domain/Entities/PullRequestRegister.cs`) guarda **um registro por card × repositório** (`CardNumber` + `RepositoryId`), com `Description`, `RootCause`, `BranchPrefix`, `BranchName`. `Description` e `RootCause` são obrigatórios (lançam `DomainException`).
- `PullRequestApplication.Create` faz upsert por `CardNumber + RepositoryId`. `GetByCardNumber` filtra por repo.
- `GitHubService.CreatePullRequestAsync` já existe (Octokit), mas usa **repo fixo** do plugin "Github Configurations" (`Owner`/`Repo`). Não há listagem de repositórios nem consulta de status de PR.
- `GetCommitDiffAsync` e `GetBranchCommitsAsync` já aceitam `repository`.
- A geração com IA **não tem lógica no backend**: `POST /AI/generate` recebe o prompt pronto.

**Frontend** (`src/app/pages/authenticated/register/register.component.*`, 1039 linhas de TS)
- Toolbar com card, branch (prefixo editável por duplo clique), repositório (autocomplete de `PullRequest.ActiveRepositories` da config) e toggle "Branch para PR" (`ActiveBranchs`).
- `pr-info-card` + painéis `app-card-panel` de **Descrição** e **Root Cause** na tela principal.
- "Abrir PR" hoje só abre a página de compare do GitHub (`openGithubPullRequestPage`).
- "Salvar" envia description/rootCause/branch/repo para `POST /PullRequest`.
- "Gerar com IA" abre `components/dialog-prompt` (`mat-stepper` Commit → Resultado) para **um** repo/branch; o prompt vem da config AI (`PromptBug`/`PromptUS`) com placeholder `{githubCommitDiff}`.

**Outros consumidores afetados**
- `recent-cards`, `handover-dialog`, `recent-handovers`, `pull-request.service.ts` (usam `repositoryId`/`branch*`).
- Skill `~/.claude/skills/gerar-prmake` (`prmake-publish.sh`) faz `POST /PullRequest` com `branchPrefix/branchName/repositoryId` → precisa continuar funcionando.

### 1.2 Mudança de modelo

```
PullRequests (1 por card)                 PullRequestsGithub (N por card)
─────────────────────────                 ──────────────────────────────────
Id                                  1───N PullRequestRegisterId (FK)
CardNumber  (UNIQUE)                      CardNumber
Description (opcional)                    RepositoryId, BranchPrefix, BranchName, TargetBranch
RootCause   (opcional, único p/ todos)    GithubPrNumber, GithubPrId, Url
UserId, FormId, auditoria                 Title, Description (snapshot enviado ao GitHub)
[BranchPrefix/BranchName/RepositoryId      Status (OPEN|MERGED|CLOSED), StatusSyncedAt
 → obsoletos, removidos em fase futura]    UserId (quem abriu no CIME), auditoria
```

### 1.3 Pontos de atenção identificados

1. **Autor do PR**: o PR é criado com o token do plugin, então no GitHub o autor é sempre a conta do token. "Avatar + nome de quem abriu" (item 3) **deve vir do usuário CIME** (`UserId` → `authService.getPhotosByExternalIds`), não do GitHub.
2. **Migração de dados**: há cards com várias linhas (uma por repo). É preciso consolidar numa linha antes de criar o índice único em `CardNumber`.
3. **Compatibilidade**: `POST /PullRequest` precisa aceitar (e ignorar) `branchPrefix/branchName/repositoryId` até a skill `gerar-prmake` e o front serem atualizados.
4. **PR duplicado**: se já houver PR aberto para `head→base` no repo, o GitHub responde 422. Tratar buscando o PR existente e registrando-o (idempotência).
5. **Tamanho do prompt**: com N repositórios o diff pode estourar o limite do provedor. Enviar só `filename/status/patch` (não `blobUrl`, `rawUrl` etc.) e truncar patches muito grandes.
6. **Spec 4.3 está truncada** (termina em "do registro de checklist. Check"). Ver decisão D7.

### 1.4 Performance do status (item 3) — recomendação

**Uma única chamada do front** (`GET /PullRequest/{card}/github?refreshStatus=true`); o backend resolve:
1. Lê as linhas do card no banco.
2. PRs com status terminal (`MERGED`/`CLOSED`) **não são reconsultados** — o valor persistido é devolvido.
3. PRs `OPEN` (ou sem sync há > 60 s) são consultados **em paralelo** (`Task.WhenAll` limitado por `SemaphoreSlim(5)`), com cache de 60 s por `repo#number` (`ICacheService` do BuildingBlocks).
4. Mudanças de status são persistidas (`Status`, `StatusSyncedAt`).

Assim o front faz 1 request e o custo com a API do GitHub cai com o tempo (a maioria dos PRs antigos fica terminal). Se o volume crescer, trocar por uma consulta GraphQL única com aliases (`pr1: repository(...){pullRequest(number:..){state merged}}`) — fica como otimização futura.

---

## 2. Decisões (default adotado — confirmar)

| # | Tema | Default adotado no plano |
|---|---|---|
| D1 | Consolidação de linhas antigas por card | Manter a linha mais recente (`UpdatedAt ?? CreatedAt`); `Description`/`RootCause` = o mais recente não vazio. |
| D2 | Dados de branch/repo das linhas antigas | **Não** migrar para `PullRequestsGithub` (não há número de PR). Colunas antigas ficam nulas/obsoletas até a remoção. |
| D3 | Onde fica a "descrição" | Descrição é **do card** (`PullRequests.Description`, compartilhada — item 2.2 "reflete em todos os lugares"). Cada PR do GitHub guarda um **snapshot** de título/descrição enviados. |
| D4 | Clique num PR existente (3.1) | Modal abre preenchido; repo/branch **somente leitura**; ação principal = "Atualizar PR" (PATCH título/descrição no GitHub + banco) e "Copiar link". |
| D5 | "esses dois botões" (2.2.1) | ✅ Popovers **Descrição** e **Root Cause** (no pr-info-card da tela e no modal). |
| D6 | Lista de repositórios | Repos da org `Owner` acessíveis pelo token (`GET /orgs/{owner}/repos`, cache 10 min). Fallback: `ActiveRepositories` da config. |
| D7 | Spec 4.3 truncada | Implementar o que está escrito (resumo repo + commit: autor, data, SHA, mensagem). Completar quando a spec for corrigida. |
| D8 | PR em draft | Tratado como `OPEN` (flag `IsDraft` exposta para exibir badge). |
| D9 | Título padrão do PR | ✅ `AB#{card} {LABEL DO DESTINO}` (convenção que já existia no código), editável no modal. |
| D10 | Mudança do prompt da IA | O placeholder `{githubCommitDiff}` passa a receber um bloco multi-repo. Ajustar os textos `PromptBug`/`PromptUS` (plugin AI, id 3) para mencionar múltiplos repositórios — tarefa de dados na fase Q1. |

---

## 3. Contrato de API (fonte da verdade para front e back trabalharem em paralelo)

Base: `/api/v1`. Autenticação atual (`X-API-Key`/Bearer) inalterada.

```http
# Repositórios disponíveis (D6)
GET  /GitHub/repositories
→ 200 [{ "id": "edv-solvace", "label": "edv-solvace", "private": true, "defaultBranch": "master" }]

# Abrir PR no GitHub e registrar (1.2 / 1.3)
POST /PullRequest/{cardNumber}/github
{ "repositoryId": "edv-solvace-apps", "branchPrefix": "hotfix/", "branchName": "73001",
  "targetBranch": "qa", "title": "AB#73001 - ...", "description": "markdown...",
  "draft": false, "userId": "<externalId>" }
→ 200 PullRequestGithubResponse   (se já existia PR p/ head→base: devolve o existente, "alreadyExisted": true)
→ 400 { "error": "Branch não encontrada" | ... }

# Atualizar título/descrição de um PR já aberto (D4)
PUT  /PullRequest/{cardNumber}/github/{id}
{ "title": "...", "description": "..." }
→ 200 PullRequestGithubResponse

# Listar PRs do card (1.4 / 3)
GET  /PullRequest/{cardNumber}/github?refreshStatus=true
→ 200 PullRequestGithubResponse[]

PullRequestGithubResponse = {
  "id": 12, "pullRequestRegisterId": 5, "cardNumber": "73001",
  "repositoryId": "edv-solvace-apps", "branchPrefix": "hotfix/", "branchName": "73001",
  "targetBranch": "qa", "number": 1234, "url": "https://github.com/...",
  "title": "...", "description": "...",
  "status": "OPEN" | "MERGED" | "CLOSED", "isDraft": false, "statusSyncedAt": "...",
  "userId": "<externalId>", "createdAt": "...", "updatedAt": "...", "alreadyExisted": false
}

# Registro do card (1.5) — branch/repo passam a ser ignorados (compat)
POST /PullRequest            { cardNumber, userId, formId, description?, rootCause? }
GET  /PullRequest/GetByCardNumber?cardNumber=73001   (repositoryId aceito e ignorado)
```

---

## 4. Fases

Legenda de dependência: uma fase só começa quando todas as dependências estão `✅ concluída` em `status.md`.
Fases na mesma **onda** podem rodar em paralelo (agentes distintos).

```
Onda 1:  B1 ──┐   B2 ──┐   F1
Onda 2:       └── B3 ◄─┘   F2 (F1)   F3 (F1, contrato)
Onda 3:  B4 (B3)            F5 (F1, contrato)
Onda 4:  F4 (F3, B4)        F6 (F5)
Onda 5:  Q1 (todas)
```

---

### B1 — Modelo de dados e migração (backend)
**Depende de:** — · **Spec:** 1.3, 1.5, 4

Tarefas
- [x] Criar entidade `PullRequestGithub` em `solvace.prform.domain/Entities/` (padrão DDD: setters privados, validação com `DomainException`, `IEntity<int>`, `IAuditableEntity`, `ToResponse()`), campos da seção 1.2 + enum/const `PullRequestGithubStatus` (`OPEN`, `MERGED`, `CLOSED`).
- [x] `PullRequestRegister`: `Description` e `RootCause` opcionais (validar só se não vazio); navegação `ICollection<PullRequestGithub> GithubPullRequests`; marcar `BranchPrefix/BranchName/RepositoryId` como `[Obsolete]` e nullable.
- [x] `DefaultContext`: `DbSet<PullRequestGithub> PullRequestsGithub`; FK com cascade; índices `(CardNumber)`, `(RepositoryId, GithubPrNumber)` único; `Description` como `longtext`.
- [x] Migração `ConsolidatePullRequestPerCard`: SQL (MySQL) que consolida duplicatas por `CardNumber` conforme **D1**, depois índice **único** em `PullRequests.CardNumber`.
- [x] Migração `AddPullRequestGithub`.
- [x] Requests/Responses: `OpenPullRequestGithubRequest`, `UpdatePullRequestGithubRequest`, `PullRequestGithubResponse` (contrato seção 3).

Aceite
- `dotnet build Solvace.Master.sln` ok; `dotnet ef migrations list` mostra as duas migrações; aplicadas num banco local/QA sem erro e sem cards duplicados.
- Script SQL de conferência (contagem antes/depois) anotado nas notas de handoff.

Arquivos principais: `Entities/PullRequestRegister.cs`, `Entities/PullRequestGithub.cs` (novo), `Contexts/DefaultContext.cs`, `solvace.prform.infra/Migrations/*`.

---

### B2 — GitHubService multi-repositório (backend)
**Depende de:** — · **Spec:** 1.1, 1.2, 3

Tarefas
- [x] `CreatePullRequestAsync`: novo parâmetro `repository` (fallback no `Repo` do plugin). Tratar 422 "A pull request already exists" → buscar o PR aberto `head→base` (`PullRequest.GetAllForRepository` com `PullRequestRequest{Head="owner:branch", Base=...}`) e retornar com flag `AlreadyExisted`.
- [x] `UpdatePullRequestAsync(repository, number, title, body)`.
- [x] `ListRepositoriesAsync()`: repos da org `Owner` (Octokit `Repository.GetAllForOrg`), mapeado para `{id,label,private,defaultBranch}`, ordenado por nome, cache 10 min via `ICacheService`.
- [x] `GetPullRequestsStatusAsync(IEnumerable<(repo, number)>)`: paralelo com `SemaphoreSlim(5)`, cache 60 s por `repo#number`; status = `MERGED` se `merged`, senão `state` (`open`/`closed`) em maiúsculas; inclui `IsDraft`, `MergedAt`, `ClosedAt`.
- [x] `PullRequestResponse`: `Number` como `int`; incluir `Repository`, `AlreadyExisted`.
- [x] `GitHubController`: `GET /GitHub/repositories`; manter `POST /GitHub/pull-request` com query opcional `repository` (compat).

Aceite
- Build ok. Teste manual (Swagger) contra um repo de teste: criar PR, criar de novo (retorna o existente), listar repos, consultar status de PR aberto/mergeado/fechado.

Arquivos: `Solvace.GitHub/src/solvace.github.application/{Contract/IGitHubService.cs,Services/GitHubService.cs}`, `solvace.github.domain/Responses/*`, `Controllers/GitHubController.cs`.

---

### B3 — Aplicação e endpoints de PR do card (backend)
**Depende de:** B1, B2 · **Spec:** 1.2, 1.3, 1.4, 1.5, 4

Tarefas
- [x] `IPullRequestGithubApplication` + implementação (mesmo padrão de `PullRequestApplication`, `ICommitable`):
  - `Open(cardNumber, request)`: garante (upsert) o `PullRequestRegister` do card; chama `CreatePullRequestAsync`; persiste `PullRequestGithub` (idempotente por `repo+number`); retorna response.
  - `Update(cardNumber, id, request)`: PATCH no GitHub + atualiza snapshot.
  - `ListByCard(cardNumber, refreshStatus)`: lógica da seção 1.4 (status persistido p/ terminais; refresh paralelo p/ `OPEN`; persiste mudanças).
- [x] Rotas no `PullRequestController` conforme contrato (seção 3). Registrar DI no mesmo lugar onde `IPullRequestApplication` é registrado.
- [x] `PullRequestApplication.Create`: upsert **só por `CardNumber`**; `Description`/`RootCause` opcionais; ignora `branch*`/`repositoryId` (compat — `RepositoryId` deixa de ser `required` no request).
- [x] `GetByCardNumber`: ignora `repositoryId`; incluir no response a lista resumida de PRs do GitHub (sem refresh) para a tela carregar numa ida só.
- [x] `GetRecentByUser`: `RepositoryId/BranchPrefix/BranchName` passam a vir do PR GitHub mais recente do card (se houver).
- [ ] Registrar entrada na Timeline do card ao abrir PR (opcional, se `ITimelineApplication` estiver acessível — anotar no handoff).

Aceite
- Fluxo via Swagger: `POST /PullRequest` sem description/rootCause → 200; `POST /PullRequest/{card}/github` → PR criado + linha gravada; `GET .../github?refreshStatus=true` lista com status; payload antigo da skill `gerar-prmake` continua retornando 200.

---

### B4 — Sincronização de status e hardening (backend)
**Depende de:** B3 · **Spec:** 3

Tarefas
- [ ] Garantir que terminais não são reconsultados e que `StatusSyncedAt` limita refresh (≥ 60 s).
- [ ] Timeout por chamada ao GitHub (ex.: 5 s) — em falha, devolver o status persistido e `statusStale: true` em vez de erro 500.
- [ ] Log (ILogger) de rate-limit restante do GitHub (`ApiInfo.RateLimit`) em nível Debug/Warning.
- [ ] Revisar tratamento de erros em `GitHubService` (hoje engole exceções com mensagens genéricas) para os métodos novos: incluir a mensagem do GitHub no `Error`.

Aceite
- Card com 5+ PRs responde em < 1,5 s com cache frio e < 200 ms com cache quente (medir e anotar).

---

### F1 — Base do frontend: estado compartilhado e componentes extraídos
**Depende de:** — · **Spec:** 1.1, 2.2, 2.3

Tarefas
- [x] `services/pull-request.service.ts`: tipos e métodos do contrato (seção 3): `getRepositories`, `openGithubPr`, `updateGithubPr`, `listGithubPrs`, `saveCard`, `getByCardNumber`.
- [x] Novo `services/card-pr-state.service.ts` (signals): `cardNumber`, `pullRequest` (description, rootCause), `githubPrs`, `repositories`; setters usados por tela principal, modal e popovers → edição em um lugar reflete em todos (2.2 / 2.3).
- [x] Extrair componente **`app-branch-input`** (prefixo editável por duplo clique + nome) do `register.component` (`onPrefixDoubleClick`, `onPrefixBlur`, `onPrefixChange`) com `[(prefix)]`/`[(name)]`.
- [x] Extrair **`app-repo-autocomplete`** (input + `mat-autocomplete`, `[(value)]`, `[exclude]` para esconder repos já usados) — reutilizado no modal (F3) e no stepper da IA (F5).
- [x] Extrair **`app-target-branch-toggle`** (toggle `ActiveBranchs`).
- [x] Extrair editores **`app-pr-description-panel`** e **`app-root-cause-panel`** (conteúdo atual dos `app-card-panel` de Descrição/Root Cause, ligados ao `card-pr-state`).
- [x] Nenhuma mudança visual ainda: `register.component` passa a usar os componentes extraídos.

Aceite
- `ng build` ok; tela principal funciona igual à de hoje (regressão manual).

---

### F2 — Reestruturação da tela principal
**Depende de:** F1 · **Spec:** 1.5, 2, 2.1, 2.3

Tarefas
- [x] Toolbar: só o **número do card** no `pr-toolbar` e o **`pr-info-card` ao lado** (sai de baixo). Branch/repo/toggle saem da tela (vão para o modal).
- [x] Remover o painel de **Descrição** da tela principal (vai para o modal — 2.1).
- [x] Root Cause: botão no `pr-info-card` que abre **popover grande** (`p-popover` do PrimeNG, largura/altura equivalentes ao painel atual) com `app-root-cause-panel` (2.3).
- [x] "Salvar" → `POST /PullRequest` só com dados do card (sem exigir description/rootCause, sem branch/repo) (1.5).
- [x] `getPullRequestByCardNumber` sem `repositoryId`; popular `card-pr-state` (inclui PRs do GitHub).
- [x] "Abrir PR" passa a abrir o modal (F3) — até F3 estar pronta, manter um stub.
- [x] Ajustar `copyCustomButtons`/"Copiar", `openHandover` e "Gerar com IA" que hoje leem branch/repo da toolbar.

Aceite
- Layout conforme item 2; editar root cause no popover reflete no botão "Salvar RC no DevOps" e no copiar.

---

### F3 — Modal "Abrir PR"
**Depende de:** F1 (e contrato; integração real após B3) · **Spec:** 1.1, 1.2, 2.1, 2.2, 2.2.1, 3.1

Tarefas
- [x] Novo `components/open-pr-dialog/` (MatDialog, padrão dos demais dialogs): `app-branch-input`, `app-repo-autocomplete` (fonte: `GET /GitHub/repositories`), `app-target-branch-toggle`, campo título (D9), `app-pr-description-panel`.
- [x] Botão de ícone com tooltip "Ver descrição do card" → **popover grande** com o painel de descrição (2.2).
- [x] Seção estilo `pr-info-card` (2.2.1): aberto por, datas, botões Descrição/Root Cause (D5); reage à troca de branch/card/repo (procura em `githubPrs` o PR correspondente a repo+branch).
- [x] Ação "Abrir PR" → `POST /PullRequest/{card}/github` → copia `url` para a área de transferência (`cliipboard.service`) + snackbar com link; atualiza `githubPrs` no estado.
- [x] Modo edição (3.1 / D4): recebe um `PullRequestGithub`, preenche tudo, repo/branch readonly, ação "Atualizar PR" + "Copiar link".
- [x] Tratamento de erro (branch inexistente, PR já existente → mensagem "PR já existia, link copiado").

Aceite
- Abrir PR real num repo de teste; link copiado; linha aparece no `GET .../github`.

---

### F4 — Painel de PRs abertos do card
**Depende de:** F3, B4 · **Spec:** 1.4, 3, 3.1

Tarefas
- [ ] `app-card-panel` "Pull Requests" na tela principal com `p-orderList` **sem controles de ordenação** (`[dragdrop]=false`, ocultar botões), item: branch (`prefix+name`), repositório, data, `app-user-avatar` + nome (usuário CIME — ver 1.3.1), **status à direita**: `OPEN` verde, `MERGED` lilás, `CLOSED` vermelho (+ badge "draft").
- [ ] Carregar via `GET .../github?refreshStatus=true` ao buscar o card; skeleton enquanto carrega; ícone de "status desatualizado" se `statusStale`.
- [ ] Resolver nomes/fotos em lote (`getPhotosByExternalIds` com todos os `userId`).
- [ ] Clique no item → abre o modal F3 em modo edição (3.1).
- [ ] Atualizar `recent-cards` para o novo formato de `GetRecentByUser`.

Aceite
- Card com PRs em 3 estados mostra cores corretas; 1 request para listar.

---

### F5 — IA: stepper vertical multi-repositório
**Depende de:** F1 (e contrato; integração real após B3) · **Spec:** 4.2

Tarefas
- [ ] `dialog-prompt`: dentro do step "Commit", `mat-stepper orientation="vertical"` com **um step por repositório** de `githubPrs` (título = id do repo). Cada step usa a branch do PR daquele repo para listar commits (`GET /GitHub/commits`).
- [ ] Visual de autor/data/SHA/descrição/diff igual ao atual, dentro de cada step.
- [ ] Armazenar `Map<repo, { commit, diff }>`; step com diff buscado → `completed` (ícone de concluído).
- [ ] Botão "Adicionar repositório": `app-repo-autocomplete` com `[exclude]` = repos já no stepper + botão `+`; novo repo pede a branch (default `prefix+card`) e entra no stepper. Não permitir duplicado.
- [ ] Avançar para o próximo passo exige **≥ 1 diff** buscado.
- [ ] Dados do dialog passam a ser `{ cardNumber, cardType, githubPrs }` (não mais repo/branch únicos).

Aceite
- Buscar diff em 2 repos distintos; ambos marcados como concluídos; repo duplicado bloqueado.

---

### F6 — IA: passo "Resumo" e prompt multi-repo
**Depende de:** F5 · **Spec:** 4.1, 4.3

Tarefas
- [ ] Novo step **Resumo** entre Commit e Resultado: lista cada repositório com o commit selecionado (Autor, Data, SHA, mensagem) no mesmo visual da tela de commit (D7).
- [ ] `gerarPrompt()`: `{githubCommitDiff}` recebe bloco multi-repo compacto:
  ```
  ## Repository: edv-solvace-apps (branch hotfix/73001, commit 835ff36)
  <files: filename, status, patch>
  ```
  Só `filename/status/additions/deletions/patch`; truncar patch > ~15 kB por arquivo com aviso (1.3.5).
- [ ] Resultado gera **um** RCA para o card (grava em `card-pr-state.rootCause`) e a descrição do PR (usada como default no modal F3).

Aceite
- Gerar com 2 repos → RCA menciona ambos; texto cai no root cause único do card.

---

### Q1 — Integração, regressão e publicação
**Depende de:** B1–B4, F1–F6

Tarefas
- [ ] Regressão: busca de card, Salvar, Salvar RC no DevOps, Handover (usa `repositoryId` — ajustar se necessário), recent cards/handovers, timeline, mobile (`mobileButtons`).
- [ ] Atualizar skill `~/.claude/skills/gerar-prmake` (`prmake-publish.sh`): `POST /PullRequest` sem branch/repo e, opcionalmente, `POST /PullRequest/{card}/github`.
- [ ] Atualizar `PromptBug`/`PromptUS` no plugin AI (D10) em QA e prod.
- [ ] Aplicar migrações em QA (auto-migrate no startup) e conferir consolidação (D1).
- [ ] PRs `feature/0001 → master` nos dois repos (deploy backend antes do front).
- [ ] Planejar remoção das colunas obsoletas (`BranchPrefix/BranchName/RepositoryId` em `PullRequests`) numa feature futura.

---

## 5. Como executar (humano ou agente)

1. Abrir [`status.md`](./status.md), escolher uma fase `⬜ pendente` cujas dependências estejam `✅`.
2. Marcar a fase como `🟡 em andamento`, preencher **Responsável** (nome do agente/pessoa) e **Início**. Commitar só essa alteração (`chore(0001): inicia <fase>`) para "reservar" a fase.
3. Trabalhar na branch `feature/0001` do repo da fase. Com agentes em paralelo **no mesmo repo**, usar worktree com branch `feature/0001-<fase>` e fazer merge de volta em `feature/0001` ao concluir.
4. Ao terminar: todos os critérios de aceite ok → marcar `✅ concluída`, preencher **Conclusão** e **Commits**, marcar os checkboxes da fase neste arquivo e escrever as **Notas de handoff** em `status.md` (desvios do plano, mudanças no contrato, pendências).
5. Se travar: `🔴 bloqueada` + motivo nas notas.
6. Qualquer mudança no contrato (seção 3) deve ser feita **neste arquivo** e registrada no log de `status.md`, para o outro lado (front/back) acompanhar.
