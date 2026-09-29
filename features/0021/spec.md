Tipo: Melhoria (front + backend pequeno)
Prioridade: média
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário).

Objetivo: dar suporte melhor a **telas menores e celular** no PRMake, começando pela tela do card (tratamento do card / `auth/register`), e fazer o front **atualizar em tempo real** quando um PR é criado ou um registro é salvo por fora da tela (skills `gerar-prmake`, `gerar-handover`, outro usuário).

Requisitos:
1. **Detalhes do card dentro de Ações DevOps**: o botão "Detalhes do Card" sai do rodapé e vira uma opção a mais no popover **Ações DevOps**.
2. **Sem o botão Copiar no rodapé**: o split button "Copiar" (descrição completa / só descrição / root cause / template) é removido. Copiar passa a existir onde o texto está: botão de copiar no cabeçalho dos painéis de **Descrição** e **Root Cause** (popovers da tela, modal "Abrir PR" e qualquer outro lugar que use esses painéis).
3. **Ações inteligentes**: novo botão com popover (mesmo visual do Ações DevOps) com **Gerar com IA** e **Handover**; os dois botões saem do rodapé.
4. **Rodapé responsivo**: os botões ocupam o espaço disponível; o que não couber (janela estreita, celular) vai para um botão **"Mais"** (⋯) que, ao abrir, lista os botões escondidos — inclusive Ações DevOps e Ações inteligentes, que abrem o próprio popover a partir do "Mais". Some o modo celular antigo (split button "Salvar" com itens desabilitados).
5. **Popovers dentro da tela**: popovers que abrem para fora da tela em janelas pequenas (Ações DevOps, atalhos ⚡ da lista de PRs, filtros, Descrição/Root Cause) devem ficar sempre visíveis — reposicionar quando o conteúdo muda de tamanho (ex.: "Alterar status", "Abrir PR rápido", confirmação), limitar a altura à janela com rolagem interna e, no celular, abrir Descrição/Root Cause ocupando a largura da tela.
6. **Tempo real a partir das skills**: PR criado (pela skill ou por outro usuário), registro salvo ou handover salvo devem atualizar o front sem F5:
   - tela do card: registro, lista de PRs e — novo — abrir PR num card ainda não salvo (o backend cria o registro) passa a carregar o registro/autor;
   - modal de Handover aberto: recarrega quando o handover do card é salvo em outro lugar;
   - home: **Últimos cards** e **Últimos handovers** recarregam quando um card é salvo, um PR é aberto ou um handover é salvo.

Fora de escopo: redesenho das demais telas (férias, plugins, usuários); menu lateral/topo no celular (só conferir que não quebram a tela do card).
