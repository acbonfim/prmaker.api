# Feature 0007 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0007` em `prform.api` (backend) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Plugin "Teams Configurations" + plugin pessoal opcional | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 5529f7b |
| B3 | Alterar status do PR no GitHub (REST + GraphQL) | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 4641197 |
| B2 | Envio da aprovação para o Teams | back | B1 | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | d9ebd16 |
| F1 | Plugin opcional no front + estado do Teams | front | B1 | 2 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F2 | Botão Teams + menu de atalhos na linha do PR | front | B2, B3, F1 | 3 | ⬜ | | | | |
| F3 | Abrir PR rápido | front | F2 | 3 | ⬜ | | | | |
| Q1 | Workflow real + teste na tela | ambos | todas | 4 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Mecanismo de envio | ✅ confirmada | Workflow webhook; a URL do Workflow é a chave pessoal |
| D2 | Grupo "configurável" | padrão adotado, a confirmar | Definido no Workflow; plugin guarda o `GroupName` exibido |
| D3 | Quais PRs podem pedir aprovação | padrão adotado, a confirmar | Só `OPEN` e não draft |
| D4 | Registro na linha do tempo | padrão adotado, a confirmar | Sem registro de pedido de aprovação/troca de status |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### B1 — Plugin "Teams Configurations" + plugin pessoal opcional ✅ (5529f7b)
- `Plugins.IsOptional` (default false) em entidade, request, response (`get-all`, `get-all-by-id`) e `update-configuration`. `UserIntegration/status`: plugin opcional **não entra em `pending`**, portanto não bloqueia a tela de register nem acende o aviso do menu. `UserIntegration` (lista) traz `optional`.
- `SensitiveFieldPolicy`: palavra **`webhook`** sensível, então `WebhookUrl` é criptografado e nunca devolvido.
- Migração **`20260924173726_AddTeamsPlugin`**: `ADD IsOptional` + insert **idempotente** de "Teams Configurations" (`IsPersonal=1`, `IsOptional=1`, `AdminOnly=0`, `PersonalFields=["WebhookUrl"]`, `CreatedBy=migration:AddTeamsPlugin`) e da configuração: `WebhookUrl`, `GroupName`, `MessageTitle`, `MessageBody`, `ButtonText`, `TitleColor`, `IncludeDescription`, `DescriptionMaxLength`. JSON montado em C# e escapado para literal MySQL (348 bytes, cabe no `varchar(4000)`). O `Down` remove só o que a migração criou.
- Validado num **MySQL 8.0 descartável local** (porta 33907, dados no scratchpad): cadeia inteira de migrações ok; JSON válido e com quebra de linha real; inserts rodados de novo não duplicam; `Down` → `Up` ok. Gerada com factory de design-time temporária, removida.

### B3 — Alterar status do PR no GitHub ✅ (4641197)
- `GitHubService.SetPullRequestStatusAsync(repo, number, OPEN|DRAFT|CLOSED)`: lê o PR. MERGED → erro. Mesmo status → nada muda. `CLOSED` = REST `state=closed`. De `CLOSED`, reabre (REST) antes. `DRAFT` ↔ pronto = **GraphQL** `convertPullRequestToDraft` / `markPullRequestReadyForReview` (node id do PR, `Bearer` com o token do usuário). Erros GraphQL viram mensagem.
- Chave opcional `ApiBaseUrl` no plugin do GitHub (GitHub Enterprise; padrão api.github.com). O GraphQL vai para `api.github.com/graphql` ou `<host>/api/graphql`.
- `PUT /PullRequest/{card}/github/{id}/status { status }` → `PullRequestGithubApplication.SetStatus` (legado recusado; grava status/draft; evento realtime `GithubPrUpdated`). 400 `{ error }`.
- Teste contra **API do GitHub falsa** (HttpListener): **50/50**. Cobre as 8 transições com a sequência exata de chamadas (ex.: CLOSED(draft)→OPEN = GET, PATCH open, GQL ready, GET), no-op, MERGED, alvo inválido, erro GraphQL, repo `dono/nome`, bearer, node id e caminhos `/api/v3` e `/api/graphql`.

### B2 — Envio da aprovação para o Teams ✅ (d9ebd16)
- `TeamsApprovalService` (`prform.application/Teams`) + `TeamsController`:
  - `GET /Teams/status` → `{ available, configured, pluginId, groupName }`;
  - `POST /Teams/approval { cardNumber, pullRequestGithubId }` → `{ sent, groupName }`, 400 `{ error }` ou 403 da 0002 sem configuração.
- Só PR `OPEN` e não draft (D3); legado/outro card recusados.
- Adaptive Card 1.4 (`type: message` + attachment), no formato do Workflow "Enviar alertas de webhook para um chat": título (cor validada), corpo e descrição opcional (cortada em `DescriptionMaxLength`), botão `Action.OpenUrl` para o PR. Placeholders `{cardNumber}` `{prNumber}` `{prTitle}` `{prUrl}` `{repository}` `{branch}` `{targetBranch}` `{author}` `{status}`; desconhecidos ficam como estão. `{author}` = nome completo do usuário da api-key.
- **Anti-SSRF:** só `https` com host DNS terminando em `.logic.azure.com`, `.powerplatform.com`, `.powerautomate.com` ou `.webhook.office.com`. `HttpClient` nomeado `TeamsWebhook` (timeout 15 s). Mensagens de erro não expõem a URL; 401/403/404 orientam gerar uma nova URL.
- Teste (MySQL descartável + handler HTTP falso): **41/41**. Cobre o seed lido pelo próprio app, o cartão completo, modelo customizado, as regras de status, 403 sem configuração, 10 URLs (IP de metadados, localhost, http, sufixo falso…), 404/500/falha de conexão e o status com e sem plugin/configuração.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0006 mergeada (#11 back, #7 front). `feature/0007` criada a partir de `master` nos dois repos. Usuário escolheu Workflow webhook (D1). Plano criado; B1 iniciada. |
| 2026-09-24 | B1, B3, B2 | Concluídas (5529f7b, 4641197, d9ebd16). Testes: migração em MySQL descartável, 50/50 (status GitHub), 41/41 (Teams). F1 iniciada. |
