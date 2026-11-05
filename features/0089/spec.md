# 0089 — Ferramenta "Resumo do pedido" do Especialista do caso

Pedido (2026-11-05): nova ferramenta para o especialista do caso que analisa e resume o que está sendo **pedido** (User
Story) ou **reclamado** (Bug) no card, lendo descrição, repro steps, discussion, anexos e tudo mais. É um **racional do
pedido**, em **português** por enquanto, mesmo que o texto do card esteja em inglês.

Decisões do usuário:
- Roda como **ferramenta** (modelo de item de trabalho, botão Ferramentas/Bancada) executada pelo executor; pedido na
  conversa → o especialista cria e executa o item e responde com o número.
- Publicar na **Timeline é opcional** (pergunta da ferramenta, padrão "não"); nunca vai para o DevOps.

## O que entrou
- Sub-tarefa `card-brief` (executor Claude, só leitura) e modelo `resumo-do-pedido` (tipo `text`, Bug e US; asks `focus`
  e `timeline`) — migração `SeedCardBrief0089` (idempotente, só acrescenta; Down tira só o que pôs).
- Evidências do DevOps nos arquivos do **item** (`IWorkItemFileApplication.ImportEvidenceAsync`, MCP
  `prmake_work_item_evidence`) — antes só existia para plano (0076); nome `ev-<hash da chave>-<origem>-<arquivo>`.
- `prmake_work_item_files` devolve imagem como imagem.
- Skills: `executar-item-de-trabalho` (roteiro do `card-brief`: ler tudo, abrir evidências, estrutura Bug/US, gravar
  `resumo-do-pedido.md`, Timeline opcional) e `especialista-do-caso` (quando usar; reaproveita item concluído).
