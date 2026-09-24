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
| F2 | Reestruturação da tela principal | front | F1 | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 721cb52 |
| F3 | Modal "Abrir PR" | front | F1 (+B3 p/ integrar) | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 46043bd |
| B4 | Sync de status e hardening | back | B3 | 3 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 31fdf7b |
| F5 | IA: stepper vertical multi-repo | front | F1 (+B3 p/ integrar) | 3 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front dd1c79d |
| F4 | Painel de PRs abertos do card | front | F3, B4 | 4 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front d553331 |
| F6 | IA: passo Resumo + prompt multi-repo | front | F5 | 4 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front eb29ffd |
| Q1 | Integração, regressão e publicação | ambos | todas | 5 | 🟡 | Claude + usuário | 2026-09-24 | | b3f0a52, 4f3d469, 1aa91ad, 99a5370, front f38d385, bcdd812, ae82f80, f8b719d |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | ✅ confirmada | Mantém a linha mais recente do card; desc/RC = mais recente não vazio. Nada é apagado sem backup: tabela `PullRequestsLegacyBackup` guarda tudo como estava. |
| D2 | **alterada pelo usuário** | "Manter dados antigos setando valor padrão": cada linha antiga vira registro `LEGACY` em `PullRequestsGithub` (defaults: repo `edv-solvace`, branch `hotfix/<card>`, título `AB#<card>`, destino vazio, sem nº de PR). |
| D3 | ✅ confirmada | O modal envia a descrição **do card** (estado compartilhado); cada PR guarda o snapshot enviado. |
| D4 | ✅ confirmada | Clique num PR → modal em modo edição: repo/branch/destino somente leitura; "Atualizar PR", "Copiar link", "Abrir no GitHub". |
| D5 | definida pela spec | 2.2.1 diz que a seção do modal é "parecida com o pr-info-card… com esses dois botões" → o pr-info-card (tela e modal) tem os 2 popovers: Descrição e Root Cause (RC oculto para US). |
| D6 | ✅ alterada pelo usuário | Lista **todos os repositórios que o usuário do token acessa** (`GetAllForCurrent`, afiliação all, sem arquivados). Id = `nome` para repos do Owner do plugin (compatível com os dados existentes) e `dono/nome` para os demais; todas as operações (abrir/atualizar PR, status, commits, diff) resolvem os dois formatos. |
| D7 | ✅ sem pendência | A 4.3 não estava truncada: o trecho final é o exemplo do card de commit (a última linha é a mensagem do commit). O passo Resumo já atende. |
| D8 | ✅ confirmada | Draft = OPEN + chip DRAFT. |
| D9 | ✅ alterada | Título padrão segue a convenção que já existia no código: `AB#<card> <LABEL DA BRANCH DE DESTINO>`; acompanha a troca de destino até o usuário editar. |
| D10 | ✅ sem mudança | Prompts `PromptBug`/`PromptUS` ficam como estão: o `{githubCommitDiff}` passa a receber o bloco multi-repo sem precisar alterar o texto. |

**Outras respostas do usuário (2026-09-24)**: (1) RC das linhas descartadas visível só no backup — ok; (2) manter `PullRequestsLegacyBackup` definitivamente; (3) defaults do LEGACY — ok; (10) registrar na timeline ao abrir PR — **sim** (feito); (11) remoção de "Descrição montada"/"RCA" da tela — ok; (12) atualizar a skill `gerar-prmake` — **perguntar de novo antes de finalizar a feature**, depois do teste do usuário.

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

### F3 — Modal "Abrir PR" ✅ (repo front)
- **Feito**: `components/open-pr-dialog/` (dados em `OpenPrDialogData`; retorna o `GithubPullRequest` no `afterClosed`). Campos: `app-branch-input`, `app-repo-autocomplete` (fonte `GET /GitHub/repositories` via `CardPrStateService.loadRepositories`, fallback `ActiveRepositories`), `app-target-branch-toggle`, título, toggle Draft. Seção 2.2.1 com `app-pr-info-card` (autor/datas do PR já registrado para o **repo + branch** selecionados; muda ao trocar qualquer um) e os 2 popovers. Prévia (somente leitura) da descrição que será enviada. Sucesso → `upsertGithubPr` no estado + copia a URL (snackbar com ação "Abrir"); PR que já existia → mensagem própria. Erro do backend (`{ error }`) aparece dentro do modal.
- **Componentes novos reutilizáveis**: `app-pr-info-card` (autor/datas + slot `[info-actions]`) e `app-panel-popover-button` (`kind="description" | "rootCause"`; `p-popover` com `appendTo="body"` e `baseZIndex=1100` para ficar acima do MatDialog; tamanho em `.cime-panel-popover__body` no `styles.scss`).
- **Riscos a conferir na Q1** (não testei no navegador): (1) popover do PrimeNG sobre o MatDialog — foco/cliques dentro do editor; (2) `navigator.clipboard.writeText` depois do HTTP pode ser negado (sem gesto do usuário) → cai no fallback do `CliipboardService`, e o snackbar sempre oferece "Abrir".

### F2 — Reestruturação da tela principal ✅ (repo front)
- **Feito**: topo = `pr-toolbar` só com o número do card + `app-pr-info-card` ao lado (quem abriu/datas + popovers Descrição/Root Cause). Branch, repositório, destino e os dois editores saíram da tela. `branchPrefix/branchName/selectedRepositoryObj/environmentName` continuam no componente como **defaults do modal** (última escolha / PR mais recente / config).
- **Salvar** → `PullRequestService.saveCard` só com dados do card (`description`/`rootCause` do estado; `null` mantém no backend). Habilitado após buscar o card.
- **Abrir PR** → `openPrDialog()`; habilitado após buscar o card (`canOpenPr`). Card não salvo mostra o info-card vazio ("Card ainda não salvo") para liberar popovers e Abrir PR.
- **Lista provisória** "Pull Requests" (`app-card-panel`) com os PRs de `prState.githubPrs()` (vindos no `GetByCardNumber`, sem refresh de status); clique abre o modal em modo edição. **A F4 substitui por `p-orderList` com status/avatares/refresh.**
- **Removidos**: `openGithubPullRequestPage`/`makeUrlLink`/`link` (substituídos pelo modal), `openDialogFullDescription`/`openDialogRCA` e seus botões (o conteúdo agora está nos popovers), `getBranchLabelByBranch`.
- **Gerar com IA / Handover**: usam repo/branch do PR do GitHub mais recente (fallback: defaults). A F5 troca a IA para multi-repo.
- **Validação**: `ng build` ok (só warnings pré-existentes). Sem teste no navegador/integração real.
- **Deploy**: o front da F2 depende do backend B3 (o `POST /PullRequest` antigo exige descrição) — publicar backend antes.

### B4 — Sync de status e hardening ✅
- **Feito** (`31fdf7b`): `GitHubService.GetPullRequestsStatusAsync` com timeout de **5 s por PR** (`Task.WaitAsync` — Octokit não aceita `CancellationToken`); `TimeoutException`/falhas de rede viram `Error` no item (log Warning) em vez de exceção; log do rate limit restante após cada lote (`Warning` abaixo de 100, senão `Debug`). `PullRequestGithubApplication.ListByCard` agora captura falha total da chamada → PRs abertos saem com `statusStale: true` e o status persistido (nunca 500 por causa do GitHub). `ILogger` injetado nos dois.
- **Já garantido na B3**: terminais (MERGED/CLOSED) não são reconsultados; cache de 60 s por PR no `GitHubService` limita a frequência de consulta (por isso não usei `StatusSyncedAt` como throttle — ele registra a última **mudança** de status).
- **Validação**: teste descartável da B3 ampliado com "GitHub fora do ar" → 15/15 OK.
- **Pendente (Q1)**: medir o tempo real (critério: card com 5+ PRs < 1,5 s com cache frio, < 200 ms com cache quente) — exige GitHub real; não medido.

### F5 — IA: stepper vertical multi-repo ✅ (repo front)
- **Feito**: no `dialog-prompt`, o passo "Commit" é um `mat-stepper` vertical (`[linear]="false"`) com um step por repositório — título = id do repo + branch. Cada step usa `app-repo-commit-picker` (branch editável com recarregar, busca de commit por autocomplete, `app-commit-details`, `app-git-diff-viewer`); ao escolher o commit o diff é buscado e a seleção emitida. Step com diff = concluído (ícone ✓ via `STEPPER_GLOBAL_OPTIONS { displayDefaultIndicatorType: false }` + `[state]`; o lápis de "edit" foi trocado pelo número). "Adicionar repositório" = `app-repo-autocomplete` com `[exclude]` dos repos já no stepper + botão `+` (duplicado bloqueado); repos adicionados à mão têm "Remover". Botão "Gerar com IA" exige ≥ 1 diff.
- **Estado**: `CardPrStateService.aiRepositories` / `aiSelections` / `aiSelectedDiffs` (tipos `AiRepository`, `RepoCommitSelection`) — sobrevivem a fechar/reabrir o dialog e são zerados ao trocar de card (`loadRegister` com outro card, `reset`).
- **Entrada**: `DialogPromptData { cardNumber, isAiGenerate, cardType, repositories, defaultBranch, repositoryFallback }`; o `register` monta `repositories` a partir dos PRs do card (branch do PR mais recente por repo). **Desvio**: se o card não tem PR, o stepper já vem com o repositório/branch padrão (mantém o fluxo antigo de um repo só) em vez de vazio.
- **Prompt (provisório até a F6)**: `{githubCommitDiff}` recebe um JSON com `[{ repository, branch, commit, message, diff }]` de todos os repos com diff.
- **Extra**: `helpers/commit.ts` + `app-commit-details` (reusar no Resumo da F6). Corrigido bug: com o formato do backend (`title` + `description` separados) a descrição do commit perdia a 1ª linha.
- **Validação**: `ng build` ok (sem warnings novos). Não testado no navegador.

### F4 — Painel de PRs abertos do card ✅ (repo front)
- **Feito**: `components/github-pr-list/` substitui a lista provisória da F2 no painel "Pull Requests". `p-orderList` só como lista selecionável (`.p-orderlist-controls` escondido via CSS, `dragdrop=false`, sem manter seleção). Item: avatar + nome (usuário do CIME, `getPhotosByExternalIds` **em lote** com cache por componente), branch, `repo → destino · #número`, data de abertura e status à direita (`OPEN` verde, `MERGED` lilás, `CLOSED` vermelho — cores do GitHub; chip `DRAFT`; ícone `sync_problem` quando `statusStale`). Clique → `openPrDialog(pr)` (modo edição).
- **Carga**: na busca do card a lista vem do `GetByCardNumber` (status persistido) e, se houver PRs, `refreshGithubPrs()` chama `GET /PullRequest/{card}/github?refreshStatus=true` **uma vez** em segundo plano (descarta a resposta se o card mudou). Botão ⟳ no cabeçalho do painel força a atualização.
- **`recent-cards`**: sem mudança — o backend (B3) já devolve repo/branch do PR mais recente no mesmo formato.
- **Riscos (Q1)**: o tema Aura do PrimeNG não tem `darkModeSelector` configurado; sobrescrevi fundo/borda dos itens do listbox para o tema escuro da tela, mas conferir visualmente. Não testado no navegador.

### F6 — IA: passo Resumo + prompt multi-repo ✅ (repo front)
- **Feito**: passo **Resumo** entre Commit e Resultado (stepper: Commit → Resumo → Resultado): para cada repositório com diff — nome, branch, nº de arquivos e `app-commit-details` (autor, data, SHA, mensagem); lista os repos do stepper sem commit (ficam fora do contexto). Rodapé: Commit → "Resumo"; Resumo → "Commits" (voltar) / "Gerar com IA"; Resultado → Copiar/Concluir.
- **Prompt**: `helpers/ai-prompt.ts` → `buildMultiRepoDiffContext(selections)` substitui `{githubCommitDiff}` por markdown: por repo, cabeçalho (`## Repository`, branch, SHA, autor, mensagem completa) e, por arquivo, `### nome (status, +a -d)` + bloco ```diff```; sem URLs/blobs; patch > `MAX_PATCH_CHARS` (15 000) por arquivo é truncado com aviso; arquivo sem patch é sinalizado. O prompt padrão (fallback do código) passou a dizer que há vários repositórios e **um** root cause.
- **Resultado**: continua pelo `GlobalService.onAiGenerated` → `register` → `CardPrStateService.setContent` — o RC gerado é o único do card e a descrição gerada é a que o modal "Abrir PR" envia (D3).
- **Validação**: `ng build` ok; teste Node do `buildMultiRepoDiffContext` (2 repos + 1 sem diff, patch grande, arquivo binário) — 7/7 OK. Não testado no navegador nem com o provedor de IA real.
- **Pendente**: spec 4.3 está truncada (D7) — implementado só o que está escrito. **Q1/D10**: os prompts `PromptBug`/`PromptUS` da configuração do plugin AI (id 3) têm prioridade sobre o fallback do código — ajustar o texto deles para mencionar múltiplos repositórios.

### Q1 — Integração, regressão e publicação 🟡 (em andamento)
- **Respostas do usuário (2026-09-24)**: pode rodar a migração desde que sem perda de dados; ele mesmo testa abrir PR no GitHub; ele abre o PR para a master e commita após testar localmente; dados antigos devem ser mantidos com valores padrão.
- **Migrações refeitas para não perder dados** (arquivos em `solvace.prform.infra/Migrations`):
  - `ConsolidatePullRequestPerCard`: antes de tudo `CREATE TABLE IF NOT EXISTS PullRequestsLegacyBackup AS SELECT * FROM PullRequests` (cópia integral); depois TRIM + consolidação por card (D1) + `varchar(50)` + índice único. **`Down` agora restaura tudo** a partir do backup (valores originais e linhas removidas) e apaga o backup.
  - `AddPullRequestGithub` **regenerada** (agora `20260924033240_…`; a `…024910_…` foi removida — nunca tinha sido aplicada): `GithubPrNumber`/`GithubPrId` anuláveis; ao final insere **um registro `LEGACY` por linha antiga** (lendo do backup) com branch/repo/descrição/autor/datas preservados e os defaults do D2.
- **Código**: `PullRequestGithubStatus.Legacy` (terminal — não consulta o GitHub); `PullRequestGithub.IsLegacy`/`PromoteLegacy(...)`; abrir PR para a mesma branch+repo de um LEGACY **promove** o registro (não duplica); `PUT` em LEGACY → 400. Front: `number` anulável, chip cinza tracejado "LEGADO" com tooltip; clique num LEGACY abre o modal em **modo criação** preenchido com repo/branch dele; LEGACY não conta como "PR já existe" no modal.
- **Ensaio das migrações em MySQL 8.0.46 local** (Homebrew, `mysqld` temporário, dados sintéticos com 9 linhas/5 cards cobrindo: card com 3 repos, desc/RC vazios na linha mais nova, card com espaços + linha sem repositório/branch, empate de data, unicode/emoji, descrição de 50 kB, repo só com espaços): Up → 11/11 checks (backup idêntico ao original campo a campo; 1 registro por card; 9 LEGACY; toda linha antiga tem seu LEGACY com repo/branch/desc/autor/datas; regras D1; defaults D2; FKs). **Rollback → tabela idêntica ao original** e tabelas novas removidas; re-Up ok.
- **Camada de aplicação** (teste descartável, agora também contra o MySQL local): 19/19 — inclui LEGACY (não consultado, update bloqueado, promoção ao abrir PR).
- **Não feito (bloqueado)**: leitura/diagnóstico do banco remoto foi negada pela política de permissões da sessão — nenhuma consulta nem migração foi executada no banco compartilhado. Rodar antes de subir a API desta branch:
  ```sql
  SELECT VERSION();                                                    -- 8.0+ (ROW_NUMBER)
  SELECT MAX(CHAR_LENGTH(TRIM(CardNumber))) FROM PullRequests;         -- <= 50
  SELECT MAX(CHAR_LENGTH(TRIM(RepositoryId))), MAX(CHAR_LENGTH(BranchPrefix)), MAX(CHAR_LENGTH(BranchName)) FROM PullRequests; -- <= 200, 50, 200 (acima disso é truncado no LEGACY; o backup guarda o original)
  SELECT COUNT(*) linhas, COUNT(DISTINCT TRIM(CardNumber)) cards FROM PullRequests;
  -- depois da migração: cards = COUNT(*) FROM PullRequests; linhas = COUNT(*) FROM PullRequestsLegacyBackup = COUNT(*) FROM PullRequestsGithub WHERE Status='LEGACY'
  ```
- **⚠️ Migrations**: o `StartupMigrator` só roda **fora de Development** (`Program.cs:78`) — local, em Development, é preciso aplicar à mão (`dotnet ef database update --context DefaultContext --startup-project .../solvace.prform.api`). O `appsettings.Development.json` aponta para o banco compartilhado: depois de aplicar, a versão antiga em produção passa a falhar ao salvar o mesmo card para um 2º repositório (índice único) e só enxerga o registro consolidado — publicar o backend logo depois.
- **Após as respostas**: `GitHubService.ListRepositoriesAsync` lista todos os repos acessíveis pelo token (D6) + `ResolveRepository("dono/nome" | "nome")` usado em criar/atualizar PR, status, commits e diff; cache `github:repositories:token:{owner}`. `PullRequestController` registra na **timeline** do card ao abrir PR ("PR #n aberto pelo CIME: repo (branch → destino) — url", ou "já existente registrado"), com o usuário logado (claim `ExternalId`) ou o `userId` do request; falha na timeline só gera log. Teste descartável: 19/19.
- **Teste do usuário (2026-09-24) — 2 problemas**: (1) não dava para escrever descrição/RC sem IA; (2) card legado sem a barra de "aberto por". **Causa**: API subiu em Development, as migrations não rodaram, `GetByCardNumber` falhava (tabela `PullRequestsGithub` inexistente) e o front escondia a barra — e com ela os botões de Descrição/RC. Usuário vai aplicar as migrations. **Correções mesmo assim** (1aa91ad, front bcdd812): rotas do card não dependem mais do plugin do GitHub (`[FromServices]` nas ações do GitHub/Timeline); a barra aparece mesmo com erro na busca (aviso + snackbar); botões **com texto** "Descrição"/"Root Cause" (✓ quando preenchido); no modal, a descrição do PR é **editável inline** (antes só prévia).
- **2º teste do usuário (2026-09-24)** — corrigido em 99a5370 / front ae82f80: (1) aviso "já existe PR" no modal agora exige também a mesma branch de destino; (2) `app-pr-info-card` sempre visível (vazio antes da busca, skeleton durante — input `loading`; botões desabilitados até buscar); (3) draft→aberto não atualizava: cache de 60 s do status — ⟳ agora usa `forceRefresh=true` (ignora o cache) e abrir/atualizar PR grava o status fresco no cache; (4) Salvar não atualizava a barra: `POST /PullRequest` passou a devolver o registro completo (a skill só imprime a resposta — compatível); (5) **SignalR**: evento `pullRequestCardUpdated` no grupo `pullrequest:<card>` com `action` = `register-saved` | `github-pr-opened` | `github-pr-updated` | `github-pr-status` (contrato em `PullRequestRealTimeEvents`); a tela assina o grupo do card buscado: PRs → recarrega lista do banco; registro → recarrega em silêncio, ou pergunta se houver edição local não salva (eco do próprio salvar é ignorado). Teste descartável: 25/25. Mudança de status feita direto no GitHub só aparece quando alguém lista/atualiza o card (sem webhook do GitHub).
- **Pendências**: teste manual do usuário (abrir PR, telas, popovers sobre o modal, cópia do link, IA multi-repo); prompts `PromptBug`/`PromptUS` (D10); skill `gerar-prmake`; medição de tempo da listagem (B4); commit/PR pelo usuário. Ferramentas locais usadas: `brew install mysql@8.0` (pode remover com `brew uninstall mysql@8.0`); Docker Desktop não conseguiu baixar imagens (proxy interno travado).

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
| 2026-09-24 | F3, F2 | Concluídas (front 46043bd, 721cb52). Onda 2 fechada; liberadas B4 e F5. D5 resolvida pela spec; D9 alterada. |
| 2026-09-24 | B4, F5 | Iniciadas (onda 3, sessão única, em sequência). |
| 2026-09-24 | B4 | Concluída (31fdf7b). Teste descartável 15/15; medição de tempo pendente (Q1). |
| 2026-09-24 | F5 | Concluída (front dd1c79d). Onda 3 fechada; liberadas F4 e F6. |
| 2026-09-24 | F4, F6 | Iniciadas (onda 4, sessão única, em sequência). |
| 2026-09-24 | F4 | Concluída (front d553331). |
| 2026-09-24 | F6 | Concluída (front eb29ffd). Onda 4 fechada; só falta a Q1. |
| 2026-09-24 | Q1 | Migrações refeitas sem perda de dados (backup + LEGACY), ensaiadas em MySQL local (Up/Down/Up ok). Banco remoto não acessado (bloqueado). |
| 2026-09-24 | Q1 | Decisões respondidas (D6 alterada, D7/D10 sem pendência, timeline ao abrir PR). Usuário autorizou os commits: b3f0a52, 4f3d469, front f38d385. Aguardando teste do usuário; perguntar sobre a skill gerar-prmake antes de finalizar. |
| 2026-09-24 | Q1 | Teste do usuário: migrations não rodaram (API em Development). Correções de robustez e de escrita manual (1aa91ad, front bcdd812). |
| 2026-09-24 | Q1 | 2º teste do usuário: 5 ajustes (aviso por destino, skeleton/barra fixa, cache de status, salvar atualiza barra, SignalR do card) — 99a5370, front ae82f80. |
| 2026-09-24 | Q1 | Loading no ⟳ da lista de PRs (spinner no botão + barra de progresso com itens esmaecidos) — front f8b719d. |
