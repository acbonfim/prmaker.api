Tipo: Melhoria (integração Azure DevOps → CIME em tempo real)
Prioridade: backlog

Objetivo: Quando um card (work item) for atualizado no Azure DevOps, o DevOps avisa o CIME por **Service Hook (Web Hook)** e a tela do card no PRMake se atualiza sozinha, via SignalR, sem precisar recarregar a página.

Situação atual (confirmada no código em 2026-09-25):

1. O tempo real já existe: `Cime.BuildingBlocks.RealTime` (hub `/ws`, `IRealTimeNotifier`) e o helper `NotifyCardUpdatedAsync(cardNumber, action, id)` (`PullRequestRealTimeNotifications`), que emite `pullRequestCardUpdated` para o grupo `pullrequest:{cardNumber}` (`PullRequestRealTimeEvents`).
2. O front (`register.component.ts`, `onCardUpdated`) só distingue `register-saved` (recarrega o registro); **qualquer outra action recarrega a lista de PRs**. Os dados do DevOps (`cardFull`: título, estado, área, responsável…) só são lidos em `loadCardDetails()` (`GET /Azure/card/{id}/full`), quando o card é aberto.
3. O CIME só **escreve** no DevOps (root cause; na 0011: mover estado/área, comentar). **Não há webhook do DevOps** avisando o CIME de mudanças feitas lá.
4. A configuração do DevOps fica no plugin "AzureDevOps Configurations" (lido via `IPluginConfigurationResolver`). Um webhook chega sem usuário, então o segredo precisa ser um campo **global do admin**, não pessoal.

Features:

1. Endpoint novo que recebe os eventos de work item do Azure DevOps (ex.: `POST /api/v1/Azure/webhooks/workitem`, rota final no plano).
2. Eventos tratados:
   - `workitem.updated` (principal);
   - `workitem.commented` (comentário novo na discussion);
   - opcional (avaliar na análise): `workitem.created`, `workitem.deleted`, `workitem.restored`.
3. Ao receber um evento, emitir `pullRequestCardUpdated` para o grupo do card (`resource.workItemId`) com uma **action nova**, ex.: `devops-card-updated`. O payload leva, além de `{ cardNumber, action, id }`:
   - `changedFields`: nomes dos campos alterados (ex.: `System.State`, `System.AreaPath`, `System.AssignedTo`, `Custom.RCATechnicalCategorytext`);
   - `revisedBy`: nome de quem alterou no DevOps;
   - `rev`: revisão do work item.
4. Front: tratar a action nova em `onCardUpdated`, recarregando os detalhes do card (`loadCardDetails()`) **sem** recarregar a lista de PRs nem o registro. Hoje, uma action desconhecida recarrega a lista de PRs.
5. Front: aviso discreto (toast/snackbar) "Card atualizado no DevOps por {revisedBy}" com os campos que mudaram (ex.: "Estado: Active → Test in production").
6. Opcional (avaliar na análise): registrar na Timeline do card as mudanças relevantes vindas do DevOps (troca de estado/área), para ficar no histórico do PRMake.

Regras:

1. **Autenticação do webhook:**
   - O endpoint é anônimo para o esquema `X-API-Key` de usuário, mas exige um **segredo compartilhado**, `DevOpsWebhookSecret`, campo **global do admin e sensível** no plugin "AzureDevOps Configurations".
   - O DevOps manda o segredo como header customizado (campo *HTTP headers* da assinatura, ex.: `X-Cime-Webhook-Secret: <valor>`) ou via Basic auth; o plano escolhe o formato.
   - Segredo ausente/errado: `401`. Segredo não configurado no plugin: `503`, com log, sem processar.
   - Comparação em tempo constante; o segredo nunca aparece em log.
2. **Responder rápido (`200`)**: o DevOps tenta de novo em caso de falha e, depois de falhas seguidas, coloca a assinatura em *probation* ou a desativa. A notificação SignalR é best-effort: erro nela não muda a resposta.
3. **Filtro por organização/projeto**: ignorar (com `200`) eventos de outra organização/projeto que não o configurado no plugin (`resourceContainers`/`System.TeamProject`).
4. **Só notificar, não gravar**: nesta feature o webhook não altera nada no banco (exceto a Timeline, se o item 6 entrar). O front busca os dados atualizados em `GET /Azure/card/{id}/full`.
5. **Eco das ações do próprio CIME**: quando o CIME altera o card (RC, 0011 "Mover para Test in production"), o DevOps também dispara o webhook. Só recarregar os detalhes não causa problema. Se o item 6 entrar, ignorar na Timeline eventos cujo `revisedBy` seja o usuário do PAT do CIME, para não duplicar o que o CIME já registrou.
6. **Ruído**: não notificar quando só mudarem campos de sistema irrelevantes (ex.: `System.Rev`, `System.ChangedDate`, `System.Watermark`, `System.AuthorizedDate`, `System.RevisedDate`, `System.AuthorizedAs`).
7. **Idempotência**: reentrega do mesmo evento (mesmo `workItemId` + `rev`) não deve gerar notificação ou entrada de Timeline duplicada. Na parte de SignalR, notificar duas vezes é aceitável; na Timeline, não.
8. **Cards desconhecidos**: se nenhum usuário estiver com o card aberto, o grupo SignalR está vazio e nada acontece. Não é preciso consultar o banco para saber se o card existe no PRMake.
9. **Configuração do plugin**: migração para criar `DevOpsWebhookSecret` no plugin existente **sem apagar** os valores já configurados. Mudança de schema só no MySQL (`DefaultContext`).

Validar antes de implementar:

1. Quem tem permissão no projeto do DevOps para criar Service Hooks (Project Administrator ou "Edit subscriptions")?
2. A URL pública da API (Cloud Run, projeto `cime-prod`) está acessível a partir do Azure DevOps (sem restrição de IP/ingress)?
3. Com "Resource details to send = All", o payload de `workitem.updated` traz `resource.fields` com `oldValue`/`newValue` e `resource.revision.fields`? Conferir com o botão **Test** da assinatura e guardar um exemplo real no plano.
4. O número do card no PRMake é sempre o id do work item do DevOps (`resource.workItemId`)?

---

## Passo a passo de configuração (como vai ficar depois da feature)

### A. Segredo no CIME — admin (uma vez)

1. Em **Plugins → AzureDevOps Configurations**, preencha **DevOpsWebhookSecret** com um valor longo e aleatório (ex.: gerado por um gerenciador de senhas) e salve.

### B. Service Hook no Azure DevOps — Project Administrator (uma vez por evento)

1. No projeto do DevOps: **Project Settings → Service hooks → + Create subscription**.
2. Serviço: **Web Hooks** → **Next**.
3. **Trigger:** "**Work item updated**".
   - **Area path:** opcional (ex.: `Solvace Product Improvement`), para limitar às áreas usadas no PRMake.
   - **Work item type:** opcional (ex.: Bug).
   - **Field:** deixar em branco (qualquer campo). Os campos irrelevantes são filtrados no CIME.
   - **Next**.
4. **Action:**
   - **URL:** `https://<api-cime>/api/v1/Azure/webhooks/workitem` (rota final definida no plano).
   - **HTTP headers:** `X-Cime-Webhook-Secret: <o mesmo valor de DevOpsWebhookSecret>` (formato final definido no plano).
   - **Resource details to send:** All.
   - **Messages to send / Detailed messages to send:** None.
5. **Test**: deve voltar `200`. **Finish**.
6. Repetir os passos 1–5 com o trigger "**Work item commented on**" (e os opcionais, se entrarem).

### C. Conferir

1. Abra um card no PRMake.
2. Em outra aba, mude o **State** do mesmo card no DevOps.
3. Em poucos segundos, os detalhes do card no PRMake se atualizam e aparece o aviso "Card atualizado no DevOps por …".
4. Em **Project Settings → Service hooks**, a assinatura mostra a entrega com sucesso (**History**).
