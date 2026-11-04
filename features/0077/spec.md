# 0077 — Tela do card: seção de Pull Requests só quando houver PR

## Pedido (do usuário, 2026-11-04)
1. Ao abrir a tela de PullRequest, a seção de **Pull Requests** vem por padrão **escondida** quando não há nenhum PR
   aberto associado ao card. Nas etiquetas do card (cartão do topo, onde ficam os itens do card) entra uma etiqueta
   informando que **não há pull requests** para o card.
2. A seção do **plano de análise/execução** ganha a visualização maior, ocupando o espaço que era do Pull Requests.
   Ao ter um pull request aberto, ela volta ao tamanho original.
3. As opções de abrir em **tela cheia** continuam iguais.

## Decisões
- "PR aberto associado ao card" = qualquer PR na lista do card (a mesma lista do painel: aberto, mesclado, fechado ou
  legado). Lista vazia → painel escondido; o primeiro PR (Abrir PR, tempo real, vigia) faz o painel voltar.
- Ao rebuscar o mesmo card que já tinha PRs, o painel não some durante o carregamento (sem pulo de layout).
- Celular (abas): sem PRs, a aba "PRs" some e a aba aberta passa a ser o Plano.
- A etiqueta "Sem pull requests" só aparece com o card carregado (não durante a busca).
