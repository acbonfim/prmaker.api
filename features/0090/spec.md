# 0090 — Compositor do Especialista: `#item`, comandos `/` e `/clear`

Pedido (2026-11-05):

1. No especialista, ao digitar `#` + número, buscar o **item de trabalho** do PRMake e mostrar o título. Ao enviar, o
   `#nn` vira **link** na conversa e abre um **popover** daquele item.
2. Ao digitar `/` no **início** da mensagem, listar as **ferramentas do especialista** (nome + o que faz) para usar.
3. Comando novo **`/clear`**: limpa toda a conversa.
4. Ferramenta **`/cancelar-item-de-trabalho #nn`**: cancela o item e, se precisar, desfaz o que ele já fez.

## Decisões (tomadas na implementação — revisar)

- **Onde**: nos dois compositores do especialista — a conversa grande (modal) e a compacta (coluna da direita do card).
- **`#nn` ao digitar**: sugestões primeiro dos itens **do card** (filtra pelo começo do número e também pelo título:
  `#prom` acha "Promover…"); número que não é do card busca o item direto (`GET WorkItems/{n}`) e mostra o título com o
  card dele. Enter/Tab escolhe, ↑/↓ navega, Esc fecha.
- **`#nn` na mensagem**: só nos textos da conversa (pessoas e especialista) — o markdown do plano não muda. `#nn` colado
  em letra/número, em URL ou em `&#nn;` não vira link. Clique abre o popover do item (tipo, estado, título, card,
  progresso, sub-tarefas, PRs/links, motivo do cancelamento) com "Abrir na Bancada"; item que não existe mostra
  "não encontrado".
- **`/`**: só com `/` no começo da mensagem. Lista os comandos do compositor (`/clear`, `/cancelar-item-de-trabalho`) e
  as ferramentas que se aplicam ao card (nome + descrição, filtrando pelo que foi digitado — chave ou título).
  Escolher uma ferramenta abre o formulário dela no painel de Ferramentas (na conversa compacta, abre a conversa
  grande já nela). As que não se aplicam não aparecem (como o painel faz sem busca).
- **`/clear`**: pede confirmação. Para **todos** da conversa: as mensagens e os arquivos/pontos/propostas/ações da
  conversa somem da tela e do contexto do especialista, a resposta em andamento para, e a **sessão do Claude do
  especialista recomeça do zero**. Nada é apagado do banco (as mensagens ficam com `DeletedAt`, o resto é filtrado pela
  data da limpeza) e fica a mensagem do sistema "Conversa limpa por Fulano". Proposta pendente vira "substituída" e
  ação pendente "não autorizada" (não dá para decidir algo que sumiu). Plano, itens e comentários antigos do plano não
  mudam.
- **`/cancelar-item-de-trabalho #nn`**: só itens **deste card**. Abre o diálogo de cancelar já existente (0085) com
  "Desfazer o que já foi feito" **marcado quando o item tem sub-tarefa concluída** ("caso precise"). Item já concluído
  ou cancelado com algo feito → oferece só desfazer; já cancelado sem nada feito/desfeito → avisa. Feito pela API da
  conversa (`POST CaseChat/{card}/work-items/{n}/cancel`), que registra a mensagem do sistema "Fulano cancelou o item
  #nn…" (o especialista e as pessoas ficam sabendo).
