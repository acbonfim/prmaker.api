# 0088 — Meus cards na Bancada (home)

## Pedido

A tela de Pull Requests vai cair em desuso. Na home, trazer os meus cards da Bancada, ordenados pela data da última
atualização no card, com destaque em relação às outras listas.

## Decisões

- **Quais cards**: todos em que o usuário atuou — registrou/salvou o card, criou item de trabalho, criou/respondeu/
  continuou plano, escreveu na Timeline, comentou no plano, abriu PR ou salvou handover (mesma coleta do "participei",
  agora com itens de trabalho e com os cards registrados por ele).
- **Ordem**: última atualização no card, de **qualquer pessoa** (inclusive o executor) — registro, PRs, handover,
  Timeline, comentários, `UpdatedAt` do plano e dos itens de trabalho. Não é a última vez que o usuário mexeu.
- **Destaque**: seção "Meus cards na Bancada" no topo, largura total, cartões em grade, borda/fundo na cor primária;
  "Seus últimos cards" e "Cards que você participou" continuam abaixo, como antes.
- **Navegação**: cartão da lista nova abre na Bancada (`/auth/workbench?card=`), atalhos com `focus` (plano, Timeline,
  itens); a linha do item de trabalho abre o item (`&item=`). As listas antigas seguem abrindo a tela de PR, exceto o
  atalho de itens, que vai para a Bancada.

## Backend

- `GET Home/cards?scope=workbench&take=12` (`HomeCardsScope.Workbench`).
- `HomeCardResponse` ganhou `UpdatedAt` (última atualização) e `WorkItems { count, open, current }`; itens de trabalho
  viram atividade (`kind/role = workitem`) — "Criou o item #N" / "Item #N: <status>".
- Ordem calculada no banco: `LastUpdatesAsync` (uma consulta por banco, UNION ALL + GROUP BY, só datas).

## Teste

Postgres isolado (`.t0088`): 1001 (item meu antigo + Timeline de outra pessoa há 1 h) fica antes de 1002 (item meu
rodando, atualizado há 2 h), 1003 (plano meu), 1005 (registrado por mim); 1004 (só de outra pessoa) não aparece.
`mine`/`participated` continuam iguais. Home conferida no Chrome headless (1440 px e 390 px).
