# 0085 — Ajustes (pedido do usuário depois de ver a Bancada, 2026-11-05)

Branch `feature/0085-ajustes` nos dois repos (a partir da master com a 0085 mesclada).

## Pedido
1. A timeline vira um botão (como na tela de PR, abre em tela cheia); o espaço dela fica para o Especialista.
2. Card Bug aberto sem plano/itens: a Bancada **sugere a análise inicial**; a própria análise cria os itens de trabalho e
   as sub-tarefas.
3. US: idem (análise inicial de US, cria itens/sub-tarefas como US).
4. Item com botão **play**; ao executar, a tela passa a mostrar a **sessão de execução** daquele item ao lado: tudo o que
   está sendo feito em tempo real; o item com animação de "trabalhando" e cada sub-tarefa mudando para em andamento /
   concluída ao vivo.
5. As mesmas sugestões da tela de PR (situação do card, assistente/sugestões), o **robô animado** quando o Claude está
   trabalhando e o ícone de **perda de conexão**.
6. A barra com as tags de status do card, como na tela de PR.

## Contrato (novo)
- Plano de **análise** também gera item automático: `origin=analysis`, título "Análise inicial do bug #<card>" /
  "Análise inicial da US #<card>", tipo pelo Kind do plano (`analisar-us` → user-story), uma sub-tarefa por etapa,
  sincronizado como o de correção. Ao concluir a análise, o plano de correção gera os itens da entrega (já existe).
- `GET api/v1/WorkItems/{number}/live` → `{ number, status, request: {id, kind, status, workerName?, claimedAt?,
  startedAt?, lastHeartbeatAt?, waitCode?} | null, activity: {label, tool?, at} | null, recent: [{label, tool?, at}],
  logs: [{at, kind, text, taskNumber?}] (últimos 50, mais novo por último), executorOnline: bool }`.
  Item de plano → usa o pedido ativo da pista main do plano. Sem pedido → `request:null`.
- Tempo real: `workItemsUpdated` action `activity` também a cada mudança de atividade do pedido **do próprio item**
  (heartbeat), além do que já existe para itens de plano.

## Fases
| Fase | O quê | Dono |
|---|---|---|
| BA | item automático do plano de análise + `GET {number}/live` + activity do pedido do item | agente BA |
| FA | página: timeline em botão, lateral = Especialista (+ Card recolhido), sugestão de análise inicial (Bug/US), barra de tags do card, robô animado + perda de conexão, sugestões (situação/assistente) | agente FA |
| FB | `app-work-item-execution` (sessão de execução ao vivo) + play nos itens + modo "executando" na lista (item ao lado, animação, sub-tarefas ao vivo) | agente FB |
| QA | build, e2e local, PRs e merge | Claude |

## Rodada 3 (branch `feature/0085-r3`, depois de #156/#86 em produção)
Pedido:
1. Na Bancada acaba "Correção v1 / Análise v1": **o item de trabalho é a versão**. O histórico é do item (a execução
   dele). Precisa mudar → cancelar o item ou criar outro (na tela ou pelo especialista).
2. Tarefas conhecidas (abrir PR etc.) sem digitar: **repositório e branch com lista pesquisável/autocomplete** (repos
   configurados + branches do GitHub), destino sugerido pelas regras.
3. Mover o card: **listar/sugerir estados**; cherry-pick: escolher commits; tipo novo **abrir chamado** (e afins).

Contrato:
- `GET api/v1/Azure/card/{id}/states` → `{ type, current, states:[{name, category?, suggested?:bool}] }` (estados válidos
  do tipo do card no DevOps; `suggested` = próximos usuais, ex.: das ações configuradas dev-test-in-qa/ready-for-qa/test-in-production).
- `GET api/v1/GitHub/branches?repository=<r>&search=<q>&limit=50` → `[{name, protected?, isDefault?}]` (com a integração do usuário; cache curto).
- Commits para cherry-pick: o existente `GET GitHub/commits?repository=&branch=` (+ commits dos PRs do card, se houver endpoint).
- `WorkItemTaskKinds` ganha `ticket` (Abrir chamado — params: system?, title, description, priority?) e `notify`
  (Avisar no card/Teams — params: message) via migração idempotente que **acrescenta** kinds que faltam ao array (não
  sobrescreve o do admin) + modelo `abrir-chamado`.
- Params com tipo para o editor: cada param do kind ganha `input` (`repository|branch|target-branch|state|commits|text|textarea|select`) e `options?`.

## Rodada 4 — Ferramentas do especialista (branch `feature/0085-r4`)
Pedido: o especialista tem uma lista de **ferramentas (skills rápidas)**, num botão "Ferramentas" ao lado dele, que criam
coisas rápidas (ex.: abrir PR para a branch X listando as branches do repositório; destinos conhecidos release-version,
hotfix-version, edge quando for o caso) + ferramentas de dia a dia tiradas dos cards já tratados
(`ferramentas-analise.md`, 30 cards).

Contrato:
- Modelos (`WorkItemTemplates`) ganham campos opcionais: `when {type[], flow[], repoKinds[], states[]}`, `ask
  [{key,label,input,options?,default?,required?}]`; marcadores `{ask:<key>}`, `{suffix:@<key>}` (sufixo do destino
  escolhido), `{promotionTargets}` (destinos do `PromotionProfiles` do repo), `{base}`, `{qaBranch}`, `{repoKind}`;
  `{repository}` = repositório do PR principal do card (PRs registrados / etapas `pr-<repo>`), reserva `DefaultRepository`.
- `GET WorkItems/catalog?card=` filtra por `when` e devolve `templates[].ask` com `options` resolvidas e `default`;
  `templates[].applicable` (bool) + `reason` quando não se aplica (mostrar desabilitado com o motivo).
- `POST WorkItems/card/{card}/from-template {templateKey, answers:{key:value}, title?, allowDuplicate?, duplicateReason?}`
  → renderiza no servidor e cria o item (origin `template`; `specialist` se token do caso); 409 duplicate igual ao create.
- MCP: `prmake_tools(card)` (lista com ask/applicable) e `prmake_tool_run(card, key, answers)` (cria o item; liberado
  ao especialista).
- Kinds novos (acrescentados): `devops-action`, `pr-text`, `sql-query`.
- Semear (por `key`, só se faltar; `ajuste-pos-qa` só troca se ainda for o semeado; `pr-release-version`/`pr-hotfix-version`
  ganham `when`): F1 `pr-para-branch`, F2 `prs-fluxo-producao`, F3 `promover-revamp`, F6 `resolver-conflito-promocao`,
  F7 `ajuste-pos-qa` (corrigido: cherry-pick para -dev/-qa), F8 `usar-termo-existente`, F9 `fechamento-code-fix`,
  F10 `validar-em-qa`, F11 `fechamento-sem-codigo`, F12 `script-com-chamado`, F13 `diagnostico-usuario`,
  F14 `novo-resumo`, F15 `corrigir-sonar` (JSON em `ferramentas-analise.md` §5).
- Front: botão **Ferramentas** no topo da coluna do Especialista (Bancada) → painel lateral/pop-up com busca, grupos
  (PRs e promoção, Fechamento, QA, Dados/Chamado, Textos), cada ferramenta com descrição; clicar → formulário curto com
  os `ask` (pickers do FD) → "Criar item" (from-template) ou "Pedir ao especialista" (mensagem com a ferramenta e as
  respostas). Também no menu do "+ Novo item".
