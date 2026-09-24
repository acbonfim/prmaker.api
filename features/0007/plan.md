# Feature 0007 — Pedir aprovação no Teams + atalhos rápidos na lista de PRs

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0007` nos dois repos, a partir de `master` (com 0001–0006).

## 1. Análise

### 1.1 Envio para o Teams — decisão: **Workflow webhook** (escolhida pelo usuário)
- O Teams não tem "api key" de envio. As opções eram:
  - **(a) Workflow webhook**: no grupo do Teams, o modelo *"Enviar alertas de webhook para um chat"* gera uma URL assinada; um `POST` com um Adaptive Card publica no grupo.
  - **(b) Microsoft Graph** com login Microsoft (MSAL). Exige app registrado no Azure AD com `ChatMessage.Send`; o `clientId` está vazio em produção, e a importação do Teams existente nunca foi ativada.
- **Escolhida (a):**
  - A **URL do Workflow é a "api key de uso pessoal"** (spec regra 3). Cada pessoa cria o próprio Workflow e cola a URL em *Minhas integrações*.
  - O backend faz o `POST`, então não há CORS e a URL não vai para o navegador.
  - O grupo de destino é definido **no próprio Workflow**. O campo "grupo" do plugin (regra 1) serve para nomear e orientar ("Aprovações de PR"), sem roteamento.
  - A mensagem aparece como *"<pessoa> via Workflows"*.

### 1.2 Plugins (0002) e a regra 5
- O plugin pessoal tem `IsPersonal` + `PersonalFields`: o usuário preenche só as chaves pessoais, e as demais vêm fixas do plugin global, editáveis só pelo admin. Isso atende a regra 3.
- **Problema:** hoje **qualquer** plugin pessoal pendente **bloqueia a tela de register** (`UserIntegration/status` → `ready=false` → `integrationsBlocked`). A regra 5 pede o contrário para o Teams.
- **Solução:** nova coluna `Plugins.IsOptional`. Plugin pessoal **opcional** aparece em *Minhas integrações*, mas não entra no `pending` que bloqueia a tela nem no aviso do menu do usuário.
- `SensitiveFieldPolicy` (segredos criptografados e nunca devolvidos) não reconhece "WebhookUrl". Passa a tratar a palavra **webhook** como sensível.
- `PluginConfigurations.Options` é `varchar(4000)`: o modelo da mensagem precisa caber (fica em ~1 KB).

### 1.3 Status do PR no GitHub
- `OPEN` ↔ `CLOSED`: API REST (Octokit `PullRequest.Update`, `State`).
- `DRAFT` ↔ `OPEN` (pronto para revisão): **só na API GraphQL** (`markPullRequestReadyForReview` / `convertPullRequestToDraft`, com o `node_id` do PR). O Octokit 9.1 não tem GraphQL, então é um `POST https://api.github.com/graphql` com o mesmo token (pessoal) do usuário.
- `MERGED` e `LEGADO` não mudam de status.

### 1.4 Front
- Lista de PRs: `components/github-pr-list` (p-orderList). Clicar na linha abre o modal. O modal de PR usa `app-target-branch-toggle` para a branch de destino (opções = `ActiveBranchs` do plugin "PullRequest"; título padrão `AB#<card> <DESTINO>`).
- `cc-popover` (0005) serve para os popovers ancorados; `mat-menu` para o menu de atalhos.

## 2. Solução

### 2.1 Plugin "Teams Configurations" (criado por migração)
| Campo | Quem edita | Padrão | Uso |
|---|---|---|---|
| `WebhookUrl` | **usuário** (secreto) | — | URL do Workflow do grupo |
| `GroupName` | admin | `Aprovações de PR` | Nome do grupo exibido na tela ("Enviado para …") e nas instruções |
| `MessageTitle` | admin | `Aprovação de PR — AB#{cardNumber}` | Título do cartão |
| `MessageBody` | admin | ver abaixo | Corpo (markdown do Adaptive Card) |
| `ButtonText` | admin | `Abrir PR no GitHub` | Botão do cartão (abre o PR) |
| `TitleColor` | admin | `Accent` | Cor do título: `Default`, `Accent`, `Good`, `Warning`, `Attention` |
| `IncludeDescription` | admin | `false` | Incluir a descrição do PR no cartão |
| `DescriptionMaxLength` | admin | `1200` | Corte da descrição incluída |

- Placeholders: `{cardNumber}`, `{prNumber}`, `{prTitle}`, `{prUrl}`, `{repository}`, `{branch}`, `{targetBranch}`, `{author}`, `{status}`.
- `MessageBody` padrão: `**{author}** pede aprovação do PR **#{prNumber}**\n\n{repository}: {branch} → {targetBranch}\n\n{prTitle}`.
- Migração `AddTeamsPlugin`: coluna `IsOptional` e insert **idempotente** do plugin + configuração, só se não existir um plugin com esse nome. Fica `IsPersonal=1`, `IsOptional=1`, `AdminOnly=0`, `PersonalFields=["WebhookUrl"]`.

### 2.2 Endpoints novos
```
GET  /Teams/status                                → { configured, groupName }
POST /Teams/approval  { cardNumber, pullRequestGithubId }
     → 200 { sent: true, groupName }  |  400 { error }  |  403 PERSONAL_INTEGRATION_REQUIRED
PUT  /PullRequest/{card}/github/{id}/status  { status: "OPEN" | "DRAFT" | "CLOSED" }
     → 200 PullRequestGithubResponse  |  400 { error }
```
- **Aprovação:**
  - Monta o Adaptive Card com o modelo, os dados do PR salvo e o nome de quem pede (usuário da api-key).
  - Só para PR aberto (`OPEN`, não draft).
- **Segurança do webhook:**
  - Só `https`, com host terminando em `.logic.azure.com`, `.powerplatform.com`, `.powerautomate.com` ou `.webhook.office.com`. Isso evita que uma URL pessoal seja usada para o servidor chamar endereços internos.
  - Timeout de 15 s; a URL nunca vai para logs.
- **Status:**
  - `OPEN` a partir de `CLOSED` reabre (REST); a partir de `DRAFT` marca como pronto (GraphQL).
  - `DRAFT` a partir de `OPEN` via GraphQL. `CLOSED` fecha (REST).
  - Atualiza o registro local (status/draft) e devolve o PR atualizado.

### 2.3 Tela (lista de PRs)
- **Botão Teams na linha** (spec 1–2): ícone discreto, envia na hora e mostra snackbar "Aprovação pedida em <GroupName>".
  - Desabilitado com tooltip explicando o motivo: sem configuração → "Configure o Teams em Minhas integrações"; PR não aberto.
- **Atalhos (spec 3–4):** botão **⚡** no canto superior direito do item e **clique com o botão direito** na linha abrem o mesmo menu. Um tooltip na linha sugere o botão direito. Itens do menu:
  - **Abrir no GitHub** (só PR com link);
  - **Copiar link do PR** (só PR com link);
  - **Abrir PR rápido**: popover com `app-target-branch-toggle`. Mesmo repositório e branch da linha, destino escolhido, título padrão `AB#<card> <DESTINO>`, descrição do card. Abre e copia o link, como o modal;
  - **Alterar status**: submenu com os status possíveis a partir do atual; ao clicar, já atualiza no GitHub.
- **Admin:** toggle "Opcional" no plugin pessoal. **Minhas integrações:** o plugin opcional aparece com o selo "Opcional" e as instruções do Workflow.

### Decisões

| # | Tema | Situação | Padrão adotado |
|---|---|---|---|
| D1 | Mecanismo de envio | ✅ confirmada | Workflow webhook (URL pessoal) |
| D2 | Grupo "configurável" | padrão adotado | Definido no Workflow; o plugin guarda o nome (`GroupName`) exibido e usado nas instruções |
| D3 | Quais PRs podem pedir aprovação | padrão adotado | Só `OPEN` e não draft |
| D4 | Registro na linha do tempo | padrão adotado | Não registrar pedido de aprovação nem troca de status (a spec não pede; é fácil acrescentar) |

## 3. Fases

```
Onda 1:  B1 (plugin Teams + IsOptional)     B3 (status do PR: REST + GraphQL)
Onda 2:  B2 (envio da aprovação)            F1 (IsOptional no admin/integrações + TeamsService)
Onda 3:  F2 (botão Teams + menu de atalhos) F3 (Abrir PR rápido)
Onda 4:  Q1 (Workflow real + teste)
```

### B1 — Plugin "Teams Configurations" + plugin pessoal opcional
- [ ] `Plugin.IsOptional` (+ request/response/`UpdateConfiguration`); `UserIntegration/status`: opcional não entra em `pending`; lista de integrações traz `optional`.
- [ ] `SensitiveFieldPolicy`: palavra `webhook` sensível.
- [ ] Migração `AddTeamsPlugin` (coluna + insert idempotente), gerada sem conectar no banco; SQL conferido.

### B2 — Envio da aprovação para o Teams
- [ ] `TeamsApprovalService`: resolve o plugin (pessoal), valida a URL, monta o Adaptive Card pelo modelo, `POST` com timeout.
- [ ] `TeamsController`: `GET status`, `POST approval` (400 com mensagem clara; 403 da 0002 sem configuração).
- [ ] Teste contra servidor HTTP local falso (payload do cartão, placeholders, allowlist, erros do Workflow).

### B3 — Alterar status do PR no GitHub
- [ ] `GitHubService.SetPullRequestStatusAsync` (REST fechar/reabrir; GraphQL draft/pronto).
- [ ] `PUT /PullRequest/{card}/github/{id}/status` + atualização do registro.
- [ ] Teste contra API falsa (sequência de chamadas por transição; transições inválidas).

### F1 — Plugin opcional no front + estado do Teams
- [ ] Admin (`plugin/dialogEdit`): toggle "Opcional" para plugin pessoal.
- [ ] *Minhas integrações*: selo "Opcional" e instrução do Workflow.
- [ ] `TeamsService` (`status`, `requestApproval`) e `setGithubPrStatus` no `PullRequestService`.

### F2 — Botão Teams + menu de atalhos na linha do PR
- [ ] Botão Teams (habilitado conforme status/configuração) e botão ⚡ no item; clique direito na linha; tooltip de dica.
- [ ] Menu: Abrir no GitHub, Copiar link, Abrir PR rápido, Alterar status (submenu).

### F3 — Abrir PR rápido
- [ ] Popover com `app-target-branch-toggle` → `POST /PullRequest/{card}/github` com repositório/branch da linha; copia o link; atualiza a lista.

### Q1 — Validação (usuário)
- [ ] Criar o Workflow no grupo do Teams e colar a URL em *Minhas integrações*; ajustar `GroupName`/modelo no plugin (admin).
- [ ] Pedir aprovação de um PR real; trocar status (DRAFT → OPEN → CLOSED → OPEN); abrir PR rápido; atalhos pelo ⚡ e pelo botão direito.
