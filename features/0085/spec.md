# Feature 0085 — Itens de trabalho e nova tela do card

> Pedido do usuário (2026-11-05), transcrito.

1. A tela de pullrequests já deixou de ser pull request há muito tempo, ela faz muito mais que isso. Quero que sugira um
   novo nome e uma nova rota para essa tela.

2. Quero que a aplicação permita criar itens de trabalho. Basicamente os itens de trabalho terão toda a sua descrição,
   integração com tudo que foi feito, card, anexos e tudo mais. Ele reaproveita os dados. Deve ser possível criar vários
   itens de trabalho para cada card, dentro dos itens de trabalho deve ter os seus sub-itens (tasks), cada uma com seu
   status e suas informações.

3. Objetivo: muitas vezes precisamos fazer pequenas ações, pequenos ajustes, como por ex.: abrir um PR da branch
   principal para a branch release version, o card voltou de QA e precisa de algum pequeno ajuste. Com isso deve ser
   possível o usuário criar um item de trabalho em tela, ou pedir para o especialista. O especialista deve ser o guardião
   de tudo, ele deve saber tudo que está sendo feito, o que foi feito, as regras do PRMake.

4. Percebi que o especialista sugeriu abrir um PR no card 77434 para a branch release-version com o nome
   `hotfix/77434-release` e o padrão deveria terminar com `xxx-rv`. Então o especialista deveria saber, carregar e usar
   todas as regras do PRMake e das skills. A aplicação normalmente salva nos arquivos do card esses dados, deixa
   pré-carregado para evitar chamadas desnecessárias.

5. Quando for feito um plano de análise/execução, ele agora deve gerar um item de trabalho com as suas sub-tasks e o
   plano vai executar essas sub-tasks até concluir e, ao finalizar tudo, ela deve trocar para concluída. Então
   basicamente o plano de execução agora vai ser para analisar e avaliar tudo que for necessário para gerar os itens de
   trabalho. Depois de ter tudo, a tela em evidência será a tela dos planos.

Essa tela precisa ser dinâmica: o usuário vai inserir um card e buscar e vai acontecer tudo que já acontece hoje, mas
agora ela vai carregar itens de trabalho, que devem ser uma lista expansível, ter botões para recolher/expandir cada um
deles, recolher todos, expandir todos. Ao clicar no item de trabalho deve abrir um modal com todas as informações dele;
dentro vai mostrar também as subtasks; ao clicar em uma sub-task deve abrir as informações dela.

- Deve ser possível clicar em executar cada item de trabalho desse em paralelo, e ele segue a ordem das suas subtasks.
- Deve ser possível gerar itens de trabalho em tela ou pelo especialista.
- O especialista precisa saber se não vai gerar um item repetido, com coisas que já foram feitas.
- Não vamos alterar nada na tela do pullrequest; vamos criar uma nova tela para isso com um nome que faça sentido. Ela
  deve ter retrocompatibilidade com cards antigos, deve ter o timeline, especialista e as informações do card.
- Pode sugerir um redesign dessa nova tela e um novo nome, com posicionamento de seções, modais e itens que
  reaproveitem melhor a tela.
- Deve ser possível alterar um item de trabalho que ainda não tenha sido iniciado, mas se já tiver iniciado ou
  concluído, daí só criando um novo. Deve ser possível cancelar um plano e desfazer o que já foi feito no momento em
  que for cancelado.
- Cada item de trabalho terá seu número, cada subitem deverá ter seu número no formato `#0000000` sequencial; reserve
  espaço para os ids que começam com 1 dígito até o final. Não precisa pôr os zeros na frente.
- Deve ser possível analisar uma US e criar os itens de trabalho como US. Essa nova tela deve mudar para quando for BUG
  ou US, deve se comportar para cada tipo. Deve ter ícone de bug (joaninha) e ícone de US (livrinho), assim como é no
  DevOps (azul para US e vermelho para bug), de maneira discreta.
- Liberdade para criar skills novas, sugerir novos componentes, novas telas e seções pertinentes. A tela é
  completamente nova; nada muda na de pull request antiga.
- Execução: subagentes em paralelo para frontend, backend e skills.
