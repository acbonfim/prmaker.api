# Feature 0011 — Menu "Ações DevOps" na tela do card

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0011` nos dois repos, a partir de `master`.

## 1. Análise

### Plugins
- **Não existe nome amigável de campo**: `UserIntegrationFieldResponse` só tem `Key/Value/HasValue/Sensitive/Suggested/Editable`, e o front mostra `field.key` como rótulo ("Minhas integrações" e tela de plugins).
- **Não existe campo pessoal opcional com valor padrão**:
  - `IsConfigured` exige **todos** os campos do usuário.
  - `BuildEffectiveValues` não cai para o global (decisão D1 da 0002).
  - O valor global só aparece como *sugestão* na tela (`Suggested`).
  - `IsOptional` vale para o plugin inteiro, não por campo.
- **"AI Configurations" (id 3) é global** e foi criado pela tela (sem migração).
  - O backend lê só `Provider` (`PluginAIService`, via resolver).
  - O front e a skill leem `PromptBug`/`PromptUS` por `GET PluginConfiguration/get-all-by-id?id=3`.
  - Se ele virar pessoal, esse endpoint devolve os campos **fixos com o valor global** (`PersonalValuesAsync` → `ToResponse`), então os prompts continuam acessíveis.
- `PluginConfigurations.Options` é **`varchar(4000)`**. O "AI Configurations" já guarda os prompts longos, e somar mais um prompt pode estourar o limite. É preciso aumentar a coluna para `longtext`.
- `PUT update-configuration/{id}` substitui a configuração inteira a partir do `PluginRequest`. Qualquer coluna nova de plugin precisa ser **preservada** quando não vier no request.

### DevOps
- `AzureService` tem só `GetCardAsync`, `GetCardFullAsync` e `UpdateRootCauseAsync` (JSON Patch em `workitems/{id}`).
- **Não há** endpoint para comentar na discussion. Só a skill faz isso (`azure-comment.sh`: `POST/PATCH .../workItems/{id}/comments?api-version=7.1-preview.4`, corpo `{ text: html }`).
- `GetCardFullAsync` já traz todos os campos (`$expand=all`) e os alertas (`BuildAlerts`): `MissingRootCause`, `MissingResolutionType`, `MissingGeneralClassification`, `MissingClassification` e `RemainingNotZero`.
- O backend não decide BUG/US. O front usa `System.WorkItemType === 'User Story'` → US, e o resto é BUG (`dialog-prompt.ts`).
- `UpdateStatusRequest`/`UpdateHoursRequest` são código morto.

### Card (PRMake)
- `PullRequestRegister` (tabela `PullRequests`, um registro por card), com `UpdateContent(description, rootCause)`.
- Depois de salvar, emite `NotifyCardUpdatedAsync(..., RegisterSaved)`.

### Front
- **Botão "Salvar RC no DevOps"**: `register.component.html:147-153`. O handler `saveRootCauseToDevOps()` (716-754) converte o RC com `marked` e faz `POST Azure/card/{n}/rootcause`.
  - Habilita com: `!isAzureLoading && cardNumber && rootCause && cardType !== 'us'`.
  - No mobile há um placeholder desabilitado no split-button (273-276).
- **`cardType`** do register só muda quando o diálogo "Gerar com IA" fecha. Até lá, todo card é tratado como bug. A 0011 deriva o tipo de `cardFull.fields['System.WorkItemType']`.
- **Pendências**: `cardFull.alerts` e o getter `hasCardNonConformity` (228-238). Card encontrado = `!!cardFull`.
- **Editor**: `app-markdown-editor` (Quill, valor em Markdown), usado por `pr-description-panel` e `root-cause-panel`. Estado em `CardPrStateService` (signals).
- **Menus**: padrão `cc-popover` + `button.qa-item` (`pr-quick-actions`, 0007). Tooltip em botão desabilitado = `<span [matTooltip]>` em volta. Ícone `lock` já é usado em "Minhas integrações".
- **IA**: o front monta o prompt e chama `POST AI/generate`. Modelo mais próximo: `handover-dialog` (`firstValueFrom`, signal `generating`).

### Skill `gerar-prmake`
- Hoje ela posta o resumo PT/EN direto no DevOps (`azure-comment.sh`), sempre como comentário **novo**, e não grava o resumo no PRMake.

## 2. Solução

### 2.1 Configuração por campo nos plugins (genérico)
Nova coluna `Plugins.FieldSettings` (JSON, `longtext`, nula): `{ "<chave>": { "label": "...", "optional": bool, "useGlobalDefault": bool, "hidden": bool } }`.
- **`label`**: nome amigável. O front mostra `label ?? key` em "Minhas integrações" e na tela de plugins.
- **`optional`** (só para campos do usuário): não conta no `IsConfigured`.
- **`useGlobalDefault`** (com `optional`): sem valor do usuário, o efetivo é o valor global (o "vem informado por padrão" da spec). Sem essa flag, o valor global é só **sugestão** (como hoje), e o campo fica vazio até o usuário salvar.
- **`hidden`** (só para campos fixos): não aparece em "Minhas integrações". Serve para os prompts longos e o `Provider`.
- Aplicado em `IsConfigured`, `BuildEffectiveValues`, `ToResponse` (`Label`, `Optional`, `Hidden`) e `PersonalValuesAsync` (que devolve o efetivo, com default quando houver).
- `PluginRequest`/`PluginRespose` ganham `FieldSettings`. Se vier `null` no update, o valor atual é mantido.

### 2.2 Campos no "AI Configurations"
As chaves levam o prefixo `Bug` porque a US terá outras chaves depois:

| Chave | Rótulo | Tipo | Padrão |
|---|---|---|---|
| `BugSummaryPrompt` | Prompt do resumo não técnico (Bug) | fixo (admin), oculto | prompt PT/EN (ver B2) |
| `BugTestInProductionRequiredArea` | Área exigida para mover para Test in production | fixo (admin) | `Solvace Product Improvement\Product Development Team` |
| `BugTestInProductionArea` | Área ao mover para Test in production | pessoal, opcional, com padrão | `Solvace Product Improvement\Release Management` |
| `BugTestInProductionState` | Estado ao mover para Test in production | pessoal, opcional, com padrão | `Test in production` |
| `BugTestInProductionComment` | Comentário ao mover para Test in production | pessoal, opcional, com padrão | `moving to test in production` |
| `BugReadyForQaState` | Estado ao mover para Ready for QA | pessoal, opcional, com padrão | `In Development (done)` |
| `BugInitialOriginalEstimate` | Estimativa inicial — Original Estimate | pessoal, opcional, **sem padrão** (sugestão `6`) | — |
| `BugInitialRemainingWork` | Estimativa inicial — Remaining Work | pessoal, opcional, sem padrão (sugestão `6`) | — |
| `BugInitialCompletedWork` | Estimativa inicial — Completed Work | pessoal, opcional, sem padrão (sugestão `0`) | — |

- **Plugin**: `IsPersonal = 1`, `IsOptional = 1`, `PersonalFields` = as 7 chaves pessoais. Como todas são opcionais, o plugin continua "configurado" para todos: nenhuma chamada de IA passa a dar 403 e a tela não bloqueia.
- **Rótulos** para as chaves que já existem: `Provider`, `PromptBug`, `PromptUS` (fixas e ocultas para o usuário).
- **Estimativa inicial**: a opção só habilita quando o usuário **salvou os três valores** (resposta do usuário: "igual às integrações").

### 2.3 Resumo não técnico
- **Colunas** novas em `PullRequests`: `Summary` (longtext, nula; Markdown) e `SummaryCommentId` (int, nula; id do comentário na discussion).
- **`POST api/v1/PullRequest/{card}/summary`** `{ summary, html }`:
  1. exige o registro do card já salvo (senão `400`);
  2. publica na discussion: `PATCH` do comentário `SummaryCommentId` quando existir (se o DevOps responder `404`, faz `POST` novo), ou então `POST`;
  3. grava `Summary` + `SummaryCommentId`;
  4. emite `RegisterSaved`.
  Se o DevOps falhar, não grava nada e devolve `502` com a mensagem do DevOps.
- **Contrato**: `PullRequestRegisterResponse` ganha `summary` e `summaryCommentId`. O `POST /PullRequest` **não** mexe no resumo, então o "Salvar" do rodapé não o apaga.
- **Geração** (front, mesmo padrão do handover):
  - `BugSummaryPrompt` (lido de `get-all-by-id?id=3`) com `{cardNumber}`, `{title}`, `{reproSteps}` (sem HTML), `{description}` (descrição do PR) e `{rootCause}`;
  - chamada a `POST AI/generate`.
- **Editor** (diálogo "Resumo não técnico"), com o mesmo `app-markdown-editor`:
  - sem resumo salvo → gera ao abrir;
  - com resumo salvo → mostra o texto, e dá para editar, **Gerar novamente com IA** (pede confirmação se houver edição) e **Salvar na discussion** (converte com `marked`, igual ao RC).
  - O rótulo do botão de salvar indica se vai criar ou atualizar o comentário.

### 2.4 Ações no DevOps (backend)
Novos endpoints no `AzureController`. Todos releem o card (`GetCardFullAsync`), **revalidam as regras no servidor** (tipo Bug, pendências, área, estimativa) e respondem `409 { error }` quando a regra não é atendida. Todos usam um único JSON Patch (uma revisão só) e registram na Timeline do card.

| Endpoint | O que faz | Regras |
|---|---|---|
| `POST Azure/card/{id}/actions/test-in-production` | `System.AreaPath` + `System.State` + `System.History` (comentário) | Bug; sem pendências; área atual = `BugTestInProductionRequiredArea` |
| `POST Azure/card/{id}/actions/ready-for-qa` | `System.State` | Bug; sem pendências |
| `POST Azure/card/{id}/actions/initial-estimate` | `OriginalEstimate`, `RemainingWork`, `CompletedWork` | Bug; 3 valores salvos pelo usuário (numéricos); card sem Original Estimate (ausente ou 0) |
| `POST Azure/card/{id}/actions/zero-remaining` | `RemainingWork = 0` | Bug; Remaining ≠ 0 (mesma regra do alerta) |
| `GET Azure/actions/config` | Valores efetivos do usuário para montar o menu e os tooltips | — |

- **Comentário do "Test in production"**: vai por `System.History` no mesmo PATCH. Aparece na Discussion e fica numa revisão só junto com a mudança de área e estado.
- **Erros do DevOps** (ex.: estado inválido para o tipo, regra de transição): `502 { error: <mensagem do DevOps> }`. Diferente do `UpdateRootCauseAsync` atual, o corpo do erro não é descartado.

### 2.5 Front — botão e menu
- **Botão**: "Salvar RC no DevOps" vira **"Ações DevOps"** (ícone `bolt`/`settings_suggest`). Habilita com `!!cardFull` (card encontrado). Desabilitado mostra o tooltip "Card não encontrado no DevOps".
- **Menu** (`cc-popover`, padrão `qa-item`). Cada opção tem um tooltip formatado (várias linhas) dizendo exatamente o que faz, com os valores efetivos (ex.: "Área → Release Management · Estado → Test in production · Comentário: 'moving to test in production'"). Desabilitada, mostra o ícone `lock` e o motivo no tooltip. Opções:
  1. **Salvar RC no DevOps**: comportamento atual. Desabilita sem RC.
  2. **Resumo não técnico**: abre o editor (2.3). Desabilita sem registro salvo ou sem `BugSummaryPrompt`.
  3. **Mover para Test in production**: desabilita com pendências ou fora da área exigida.
  4. **Mover para Ready for QA**: desabilita com pendências.
  5. **Realizar estimativa inicial**: desabilita sem os 3 valores salvos ("Preencha em Minhas integrações → AI Configurations") ou com Original Estimate já preenchido.
  6. **Zerar Remaining**: desabilita com Remaining já zerado.
- **Pendências**: usa o `hasCardNonConformity` existente. O tooltip lista quais pendências bloqueiam.
- **User Story**: o menu mostra só "Salvar RC" (desabilitado, US não tem RC) e o aviso "Ações para User Story serão configuradas em breve".
- **Depois de cada ação**: snackbar, recarregar `cardFull` (e as pendências) e registro/Timeline, conforme o caso. Ações que alteram o DevOps pedem **confirmação** inline no menu.
- **Mobile**: o placeholder do split-button passa a se chamar "Ações DevOps" (continua desabilitado no mobile).
- Serviço novo `devops.service.ts` (padrão `PullRequestService`).

### 2.6 Skill `gerar-prmake`
- O passo 4b usa o `BugSummaryPrompt` do plugin quando existir (o fetch grava `summary_prompt.txt`). Senão, mantém as regras atuais.
- O passo 6.4 troca o `azure-comment.sh` direto por `POST /PullRequest/{card}/summary` (`summary` em MD + `html` via `md2html.py`). O resumo fica gravado no PRMake, e rodar de novo **atualiza** o mesmo comentário em vez de criar outro.

## 3. Fases

Ondas: **1** → B1 · **2** → B2, B3, B4 (em sequência, mesmo projeto) e F1 (depois de B1) · **3** → F2, F3 (depois de B3/B4) e S1 (depois de B3) · **4** → Q1.

### B1 — Configuração por campo nos plugins (backend)
- [ ] Coluna `Plugins.FieldSettings` + `PluginConfigurations.Options` → `longtext` (migração).
- [ ] `Plugin`: parse de `FieldSettings`, `IsOptionalField`, `UsesGlobalDefault`, `IsHiddenField`, `GetFieldLabel`.
- [ ] `IsConfigured` / `BuildEffectiveValues` / `ToResponse` / `PersonalValuesAsync` respeitando as flags. `UserIntegrationFieldResponse` + `Label`, `Optional`, `Hidden`.
- [ ] `PluginRequest`/`PluginRespose` + `FieldSettings` (preserva quando `null`).
- [ ] `dotnet build`.

### B2 — Campos da 0011 no "AI Configurations" (backend)
- [ ] Migração idempotente:
  - `JSON_INSERT` das chaves novas em `Options[0]` (não sobrescreve);
  - `IsPersonal = 1`, `IsOptional = 1`, `PersonalFields` e `FieldSettings`;
  - só se o plugin existir;
  - `Down` remove as chaves e as flags.
- [ ] `dotnet build`.

### B3 — Resumo não técnico (backend)
- [ ] `Summary` + `SummaryCommentId` em `PullRequestRegister` (+ response + migração).
- [ ] `IAzureService.UpsertCommentAsync(card, html, commentId?)` (`POST`/`PATCH`, com `404` → `POST`).
- [ ] `POST PullRequest/{card}/summary` + `RegisterSaved`.
- [ ] `dotnet build`.

### B4 — Ações no DevOps (backend)
- [ ] `IAzureService.PatchFieldsAsync` com o erro do DevOps preservado.
- [ ] Serviço de ações: config efetiva (resolver do "AI Configurations"), validações e patches.
- [ ] Endpoints `actions/*` + `GET Azure/actions/config` + Timeline.
- [ ] `dotnet build`.

### F1 — Rótulos e campos opcionais em "Minhas integrações" / plugins (front)
- [ ] Interfaces com `label`, `optional`, `hidden` e `fieldSettings`.
- [ ] Diálogo: rótulo amigável (`label ?? key`), chip "Opcional" por campo, campos ocultos escondidos, placeholder "Padrão: X" quando `useGlobalDefault`.
- [ ] Tela de plugins do admin: rótulo amigável (a chave continua visível como dica).
- [ ] `ng build`.

### F2 — Botão e menu "Ações DevOps" (front)
- [ ] `devops.service.ts`, tipo do card a partir de `cardFull`, regras de habilitação.
- [ ] Popover com as opções, tooltips formatados, `lock` nas desabilitadas, confirmação inline e o aviso para US.
- [ ] "Salvar RC" movido para o menu. Recarga do card e das pendências após cada ação.
- [ ] `ng build`.

### F3 — Editor do resumo não técnico (front)
- [ ] `summary`/`summaryCommentId` no estado do card.
- [ ] Diálogo com `app-markdown-editor`: gerar, gerar novamente, editar e salvar na discussion.
- [ ] `ng build`.

### S1 — Skill `gerar-prmake`
- [ ] O fetch grava `summary_prompt.txt` (`BugSummaryPrompt`).
- [ ] O publish usa `POST /PullRequest/{card}/summary`. `SKILL.md` atualizado.

### Q1 — Publicação e teste (usuário)
- [ ] Merge dos PRs → deploy → as migrações rodam na subida (conferir o "AI Configurations" na tela de plugins).
- [ ] Em "Minhas integrações", conferir os rótulos amigáveis e os padrões, e preencher a estimativa inicial.
- [ ] Num card Bug de teste: resumo (gerar, salvar, editar, salvar de novo → mesmo comentário), estimativa, zerar remaining, Ready for QA e Test in production (na área exigida).
- [ ] Card US: aviso no menu.

## 4. Fora do escopo
- Ações e configurações para User Story (a spec diz que serão definidas depois; as chaves `Bug*` já deixam espaço para as `US*`).
- Editar `FieldSettings` pela tela de plugins (nesta entrega, só por migração).
- Webhook do DevOps para atualizar o card sozinho (feature 0012).
