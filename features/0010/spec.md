Tipo: Melhoria (evolução da feature 0007 — pedido de aprovação no Teams)
Prioridade: a definir

Objetivo: Quando um PR com pedido de aprovação enviado ao Teams for aprovado, mergeado ou fechado, **editar a mensagem original** no grupo, com o novo status, em vez de enviar uma mensagem nova.

Situação atual (feature 0007, confirmada no código em 2026-09-24):

1. `POST /Teams/approval` posta um Adaptive Card no Workflow do usuário (modelo "Enviar alertas de webhook para um chat"; URL em Minhas integrações → `Teams Configurations.WebhookUrl`). O Workflow responde `202` **sem corpo**: o CIME não recebe o id da mensagem postada e não tem como editá-la depois.
2. O CIME só conhece os status `OPEN` / `DRAFT` / `MERGED` / `CLOSED` (`PullRequestsGithub`). As **revisões** (aprovado / mudanças solicitadas) não são lidas.
3. O status só é atualizado quando alguém abre a lista de PRs do card ou clica em ⟳ (`GET /PullRequest/{card}/github?refreshStatus=true`, cache de 60 s), ou quando a troca é feita pelo próprio CIME (`PUT …/github/{id}/status`). **Não há webhook do GitHub** avisando o CIME.
4. No Teams, uma mensagem só pode ser editada pela mesma conta ou bot que a postou. Com Workflows, é o **Flow bot** do fluxo, pela ação "Atualizar um cartão adaptável em um chat ou canal".

Features:

1. Guardar a referência de cada mensagem de aprovação enviada: id da mensagem, o PR (`PullRequestsGithub`), quem pediu, quando, e o último status mostrado no cartão.
2. Atualizar o cartão original quando o PR:
   - receber **aprovação** (review `APPROVED`), mostrando quem aprovou;
   - receber **mudanças solicitadas** (review `CHANGES_REQUESTED`), mostrando quem pediu;
   - for **mergeado**, mostrando quem mergeou;
   - for **fechado** sem merge;
   - opcional (avaliar na análise): **reaberto** ou **voltou para DRAFT**.
3. O cartão atualizado mantém o conteúdo original (título, PR, branch → destino, botão "Abrir PR") e ganha:
   - um **selo de status** com cor (ex.: ✅ APROVADO verde, 🟣 MERGEADO roxo, ⛔ FECHADO vermelho, ✏️ MUDANÇAS SOLICITADAS laranja);
   - um **histórico curto**: "✅ Aprovado por Fulano · 24/09 14:32", "🟣 Mergeado por Beltrano · 24/09 15:10".
4. Detectar as mudanças na hora por **webhook do GitHub** (eventos `pull_request` e `pull_request_review`), num endpoint novo do CIME.
5. Como reforço, as trocas de status feitas pelo próprio CIME (0007, "Alterar status") e a atualização da lista (⟳) também atualizam o cartão, se o status mudou desde a última edição.
6. Textos e cores de cada estado configuráveis no plugin "Teams Configurations", como já acontece com o título e o corpo do pedido.
7. Na linha do PR (lista de PRs), indicar que existe pedido de aprovação enviado: ícone discreto com tooltip "Aprovação pedida por X em dd/MM HH:mm · cartão atualizado: APROVADO".

Regras:

1. **Nunca enviar uma mensagem nova** para informar o status: só editar a original. Se não der para editar (sem id, Workflow de atualização não configurado, mensagem apagada, erro do Teams), registrar no log e seguir, sem reenviar e sem travar nada.
2. Um PR pode ter mais de um pedido de aprovação (ex.: pedido de novo depois de ajustes). Todos os cartões daquele PR são atualizados.
3. A edição precisa sair pelo **mesmo bot e conexão** que postou. Por isso o Workflow de atualização usa a mesma conta e o mesmo grupo do Workflow de envio (ver o passo a passo).
4. Configuração pelo plugin "Teams Configurations":
   - Novo campo **pessoal e sensível** `UpdateWebhookUrl` (URL do Workflow de atualização). Quem não configurar continua pedindo aprovação normalmente, só sem atualização do cartão.
   - Campos do admin para os textos e cores de cada estado (ex.: `ApprovedText = "✅ Aprovado por {approver}"`, `MergedText`, `ClosedText`, `ChangesRequestedText`, `StatusColors`).
   - Migração para criar os campos novos no plugin existente, **sem apagar** os valores já configurados.
5. O Workflow de envio precisa **devolver o id da mensagem** para o CIME, com o gatilho HTTP e a ação "Resposta" (ver o passo a passo). Se ele continuar respondendo `202` sem corpo (Workflow antigo), o pedido funciona como hoje, mas aquele cartão não é atualizado.
6. Webhook do GitHub:
   - O endpoint é anônimo, mas a **assinatura HMAC** (`X-Hub-Signature-256`) é obrigatória, com o segredo `GithubWebhookSecret`, que fica num campo do admin no plugin do GitHub (sensível). Sem assinatura válida: `401`.
   - Idempotente pelo id da entrega (`X-GitHub-Delivery`): reentrega não duplica o histórico.
   - Responde rápido (o GitHub desiste após 10 s).
   - Ignora PRs que o CIME não conhece.
7. Retrocompatibilidade: mensagens enviadas antes desta feature (sem id guardado) não são atualizadas.
8. Mudança de schema só no MySQL (`DefaultContext`), com migração. Exemplo de tabela: `TeamsApprovalMessages` (PullRequestGithubId, MessageId, RequestedBy, RequestedAt, LastStatus, LastUpdatedAt, History).
9. Segurança: a lista de hosts permitidos para as URLs de Workflow (0007) vale também para `UpdateWebhookUrl`; nenhuma URL ou segredo aparece em log.

Validar antes de implementar (bloqueia o desenho):

1. A conta que cria os Workflows tem acesso ao gatilho **"Quando uma solicitação HTTP é recebida"** e à ação **"Resposta"** do Power Automate? Em geral são **premium** (licença). Sem isso, o envio não consegue devolver o id da mensagem, e o caminho passa a ser o Microsoft Graph com login Microsoft (opção (b) da 0007, que exige app no Azure AD).
2. A ação **"Postar cartão em um chat ou canal"** devolve o id da mensagem na saída (`Message ID`) para chat em grupo?
3. A ação **"Atualizar um cartão adaptável em um chat ou canal"** consegue editar, num chat em grupo, o cartão postado pelo Flow bot?
4. Há alguém com permissão de admin nos repositórios (ou na organização) do GitHub para cadastrar o webhook?

---

## Passo a passo de configuração (como vai ficar depois da feature)

### A. Workflow de ENVIO (substitui o Workflow atual) — cada pessoa, ou um só para o time

1. Acesse **https://make.powerautomate.com** com a conta da empresa.
2. **Criar → Fluxo da nuvem instantâneo**, nome "CIME – Pedido de aprovação", e pule a escolha do gatilho.
3. **Gatilho:** "**Quando uma solicitação HTTP é recebida**" (conector *Request*).
   - **Quem pode disparar o fluxo:** Qualquer pessoa.
   - **Esquema JSON do corpo:** `{ "type": "object", "properties": { "attachments": { "type": "array" } } }`
4. **Ação:** Microsoft Teams → "**Postar cartão em um chat ou canal**".
   - **Postar como:** Flow bot
   - **Postar em:** Chat em grupo (ou Canal)
   - **Chat em grupo:** o grupo de aprovações
   - **Cartão adaptável:** expressão `string(triggerBody()?['attachments'][0]['content'])`
5. **Ação:** "**Resposta**".
   - **Código de status:** `200`
   - **Corpo:** `{ "messageId": "<saída 'Message ID' da ação anterior>" }`
6. **Salvar.** Copie a **URL HTTP** que aparece no gatilho.
7. No CIME: **Minhas integrações → Teams Configurations → WebhookUrl**, cole a URL nova e salve.

### B. Workflow de ATUALIZAÇÃO — mesma conta e mesmo grupo do Workflow de envio

1. Em **make.powerautomate.com**: **Criar → Fluxo da nuvem instantâneo**, nome "CIME – Atualizar aprovação".
2. **Gatilho:** "**Quando uma solicitação HTTP é recebida**".
   - **Quem pode disparar o fluxo:** Qualquer pessoa.
   - **Esquema JSON:** `{ "type": "object", "properties": { "messageId": { "type": "string" }, "card": { "type": "object" } } }`
3. **Ação:** Microsoft Teams → "**Atualizar um cartão adaptável em um chat ou canal**".
   - **Postar como:** Flow bot (igual ao envio)
   - **Postar em:** Chat em grupo (igual ao envio)
   - **Chat em grupo:** o mesmo grupo do envio
   - **ID da mensagem:** `triggerBody()?['messageId']`
   - **Cartão adaptável:** `string(triggerBody()?['card'])`
4. **Ação:** "**Resposta**", código `200`.
5. **Salvar** e copiar a **URL HTTP**.
6. No CIME: **Minhas integrações → Teams Configurations → UpdateWebhookUrl**, cole e salve.

### C. Webhook do GitHub — admin do repositório ou da organização (uma vez)

1. No CIME (admin): em **Plugins → Github Configurations**, preencha **GithubWebhookSecret** com um valor longo e aleatório (ex.: gerado por um gerenciador de senhas).
2. No GitHub, abra **Organização → Settings → Webhooks → Add webhook** (vale para todos os repositórios), ou **repositório → Settings → Webhooks** (um por repositório).
3. **Payload URL:** `https://api.softhouse.app.br/api/v1/GitHub/webhook` (rota final definida no plano).
4. **Content type:** `application/json`.
5. **Secret:** o mesmo valor de `GithubWebhookSecret`.
6. **Which events?** → "Let me select individual events" → marque **Pull requests** e **Pull request reviews**.
7. **Active** marcado → **Add webhook**.
8. Confira em **Recent Deliveries**: o *ping* inicial deve voltar `200`.

### D. Textos do cartão atualizado — admin (opcional)

Em **Plugins → Teams Configurations**, ajuste os campos novos (exemplos):

| Campo | Exemplo |
|---|---|
| `ApprovedText` | `✅ Aprovado por {approver}` |
| `ChangesRequestedText` | `✏️ Mudanças solicitadas por {reviewer}` |
| `MergedText` | `🟣 Mergeado por {mergedBy}` |
| `ClosedText` | `⛔ PR fechado sem merge` |

### E. Conferir

1. Peça aprovação de um PR pelo CIME: o cartão aparece no grupo.
2. Aprove o PR no GitHub: em poucos segundos o **mesmo cartão** mostra "✅ Aprovado por …".
3. Faça o merge: o cartão passa a mostrar "🟣 Mergeado por …", sem nenhuma mensagem nova no grupo.
