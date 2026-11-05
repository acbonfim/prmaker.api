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
