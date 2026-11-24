# Feature 0099 — Mesa de gestão (épicos, tarefas administrativas, DevOps no PRMake)

> Pedido do usuário (2026-11-24, sessão do Claude Code), transcrito.

Quero que a aplicação tenha uma nova tela, parecida e reaproveitando a estrutura da bancada, mas que seja voltada pra
tarefas administrativas, como gerar relatórios de bugs do DevOps, itens duplicados, fazer entendimento de bugs e
histórias. Ela deve ter o especialista, deve ter tarefas de gerar relatórios, RCAs em textos e arquivos de PDF, deve
funcionar na mesma estrutura de itens de trabalho, com números compartilhados com os números que temos lá na bancada;
eles podem se comunicar e inclusive fazer referência (related) com o `#xxx`.

Ela deve conseguir:
- criar um card no DevOps;
- editar campos com pick list dos itens existentes quando possível + auto complete;
- tags (pick list de tags existentes com auto complete e busca + permitir criar tag nova);
- mover de coluna replicando as colunas que existem lá no board (atualizar automático caso tenha colunas e states
  novos) + lane, e ter drag and drop exatamente igual o DevOps.

O chat com o especialista deve saber fazer todas essas funções e muito mais. Liberdade criativa para trazer novas
funcionalidades. Ela permite buscar um card e trabalhar diretamente nele ou não.

Como essa tela vai precisar gerar algum registro pra manter seus itens de trabalho nela e poder tratar temas
separados, podemos chamar de **épicos** e registrar isso em uma tabela. A pessoa trabalha naquele épico, que pode ter
seus itens de trabalho, e pode buscar um card por dentro desse épico. Dentro dele podemos criar histórias e coisas do
tipo, pensando nessa gestão.

Crie uma feature e comece a desenvolver.
