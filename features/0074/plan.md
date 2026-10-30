# Feature 0074 — Histórico de versões + promoção de PRs em lote

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0074` a partir da `master` (worktree `../prform.api-0074`; no front, worktree
> `../solvace.prform.web/prform-app-0074` com `node_modules` linkado de `../prform-app-0019/node_modules`).

| Repo | Caminho | Sigla |
|---|---|---|
| Backend | `prform.api` | `B*` |
| Frontend | `../solvace.prform.web/prform-app` | `F*` |
| Executor | `tools/Cime.ExecutionAgent` (no back) | `E*` |

**Entrega em dois PRs por repo** (cada um independente, em produção sozinho): **PR-A** = histórico (B1, F1);
**PR-B** = promoção (B2–B6, E1, F2–F5, Q1). O histórico não depende de nada da promoção.

---

## 1. Análise (o que já existe e vamos reusar)

- **Histórico**: `execution-plan.component.html` (botão `history`, `mat-menu #historyMenu`), `.ts` (`history` signal,
  `loadHistory`, `selectHistory`, `formatDateTime`), `execution-plan.model.ts` (`ExecutionPlanSummary`). Entidade
  `ExecutionPlan` (`CreatedByUserId`, `CreatedBy`, `StatusChangedBy`, `Phase`, `ParentPlanId`); **`Version` é o xmin** — não usar.
  Foto: `authService.getPhotosByExternalIds` (padrão do `card-timeline`); `app-user-avatar`.
- **PR**: `IGitHubService.CreatePullRequestAsync(source, target, title, draft, desc, ct, repository)`,
  `GetBranchCommitsAsync`, `CompareRefsDiffAsync`; entidade `PullRequestGithub`; `OpenPullRequestGithubRequest`;
  `POST /PullRequest/{card}/github`. **Faltam**: listar/checar branches remotas e `merge-base` (B2).
- **Config**: Skills Configurations (`SkillsConfigurationKeys`, `BranchStrategy`, `BranchNamePattern`, `PrTitlePattern`),
  semeadas por migração (`20260929061656_SeedSkillsConfigurations`, `20261002144003_SeedIntegrationApiBranchStrategy`).
- **Executor**: fila `ExecutionRequest` (kinds `analyze`/`resume`…), `Cime.ExecutionAgent`, worktrees `.prmake-wt` (0067).
- **IA**: `IAIService.GenerateContentAsync` com a chave do usuário; prompts em AI Configurations.
- **Cópia**: `ClipboardService` (`cliipboard.service.ts`) já grava `text/html` + texto.
- **Realtime**: SignalR existente (0050 — atividade ao vivo) para o progresso.

### Pontos de atenção
1. GitHub sem cherry-pick por API → executor local (E1); sem executor, só PR direto.
2. Modelo novo (`PromotionBatch`/`PromotionItem`) vai em `DefaultContext` (schema `prform`), ao lado de `PullRequestGithub`;
   o pedido na fila (`ExecutionRequest.Kind = promote`, payload = `batchId`) vive no módulo ExecutionPlans. Confirmar em B2.
3. `ExecutionPhase` só aceita analysis/correction → promoção **não** é `ExecutionPlan` (spec, fora de escopo).
4. IA custa a chave do usuário: 1 chamada por colagem, cartões por regex (sem IA).
5. Migração com índice/colunas novas: checar o que roda no deploy (migração automática com advisory lock, fatal se falhar).

---

## 2. Fases

### Onda 1 (paralelas)
**B1 — Histórico: contrato** · `ExecutionPlanSummary` + `versionNumber` (janela por card+fase, ordem `CreatedAt`) e
`createdByUserId`; ajustar o mapeamento/consulta do histórico; teste do cálculo (card com 3 análises + 2 correções).

**F1 — Histórico: painel** · novo `plan-history` (standalone) no lugar do `mat-menu`: título/subtítulo, filtro por fase,
linhas com badge de versão, avatar, criada/alterada, status, progresso, "gerada da Análise vN"; popover (CDK overlay) e
folha inferior em tela estreita; teclado/ARIA; estados carregando/vazio/erro; fotos em lote. Usa B1 (mock enquanto não sai).

**B2 — GitHub: branches e base** · em `IGitHubService`: `ListBranchesAsync(repo, filtro)`, `BranchExistsAsync`,
`GetMergeBaseAsync` (via `compare`), `GetCommitsSinceAsync(repo, branch, baseSha)` (sem merges). Cache curto, limites de taxa.

### Onda 2
**B3 — Config de perfis** · chave `PromotionProfiles` em Skills Configurations (+ rótulo), JSON `{ kind → [{target, draft, mode}] }`
por `kind` do `BranchStrategy`; **migração de seed** (netcore→`hotfix-{version}`; integrações→`release-{version}`,`master`;
angular→`release-{version}`,`edge`,`master` draft); resolvedor `{version}` → branch real (regex acima), ambiguidades devolvidas
para a tela. Teste do resolvedor.

**B4 — Lote: modelo, aplicação e endpoints** (dep. B2, B3) · `PromotionBatch`/`PromotionItem` + migração; `PromotionApplication`:
`POST /Promotion/preview` (cards → linhas: DevOps + repositórios com commits + perfil + validações), `POST /Promotion` (cria e
enfileira), `GET /Promotion/{id}`, `GET /Promotion` (histórico), `POST /Promotion/{id}/retry`. Endpoints do executor:
`claim`/`report` do item (resultado, branch criada, nº/URL do PR via `OpenPullRequestGithub`, erro). Idempotência por
`card+repo+destino`. Entrada na Timeline por PR. Progresso por SignalR.

**E1 — Executor: `promote`** (dep. B4) · `Cime.ExecutionAgent` trata o pedido: worktree em `.prmake-wt/promote/<repo>`, fetch,
`checkout -b` de `origin/<destino>`, `cherry-pick -x` na ordem, `push`, aborta em conflito e devolve os arquivos; um repositório
por vez; nunca `--force`/merge; subir `<Version>` do agente (autoatualização). Teste com repositórios git temporários
(conflito, destino ausente, já aberto).

### Onda 3
**B5 — Interpretar mensagem (IA)** (dep. B3) · `POST /Promotion/parse`: cartões por regex (+ conferência no DevOps), texto livre
pela IA do usuário com esquema JSON validado → versão, tipo, perfis do lote; prompt em AI Configurations (seed); recusa/erro
amigável sem chave. Teste com a mensagem de exemplo da spec e variações.

**F2 — Tela Promoções (manual)** (dep. B4) · rota `/promocoes` + item de menu; cabeçalho versão/tipo; grade com chips de destino,
perfil, rascunho, origem, validações em linha; `PromotionService`; botão Disparar lote.

**F3 — Colar mensagem** (dep. B5, F2) · diálogo "Colar mensagem do Release Manager" → preenche a F2; aviso de custo da chave.

### Onda 4
**F4 — Progresso e tabela final** (dep. F2, B4) · progresso ao vivo por linha, resumo, tabela final, copiar como texto/tabela,
reenviar linhas com conflito/erro, histórico de lotes.

**B6 — Sinalização e endurecimento** (dep. B4, E1) · executor offline detectado antes de disparar; expiração de itens parados;
limites (30 cards); logs "Requisição lenta"; revisão de segurança (sem merge/force, nomes de branch sanitizados, sem injeção de
argumentos no `git`).

### Onda 5
**Q1 — Integração** (todas) · e2e local (Postgres docker + repositórios git "remotos" em disco, harness da memória
`local-e2e-harness`): 3 cards × perfis, conflito, destino ausente, repetição; mensagem de exemplo ponta a ponta; regressão do
registro de PR e do plano; checagem das migrações no deploy; atualização do `CLAUDE.md`/docs; abrir PRs (nunca mesclar).

---

## 3. Riscos
| Risco | Mitigação |
|---|---|
| Conflitos frequentes em `edge`/`master` | Reporta e segue; "Retomar no Claude"; reenviar só o destino |
| Nome de branch diferente do esperado | Resolução pelo remoto + escolha na linha |
| Limite de taxa do GitHub com lote grande | Cache de branches, limite de 30 cards, sequência por repositório |
| Cherry-pick de commit já presente no destino | Detecta (`empty`) e marca `sem_mudanca` em vez de PR vazio |
| Executor offline | Bloqueia o disparo e avisa; PR direto onde couber |
