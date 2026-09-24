1. Deve ser possivel abrir um pullrequest direto do backend
   1.1. No frontend tem o botao abrir PR, a acao dele agora vai ser abrir um modal. Dentro desse
   modal vai ter as informacoes da branch (mesmo componente da tela de pullrequest), que eh
   aquele input com branch/xxx que funciona duplo clique, vai ter a lista de repositorios
   (listando todos repos do edv-solvace ou repos que o usuario tem acesso), e o componente das
   branchs, onde o usuario seleciona pra qual branch quer enviar o PR.
   1.2. A aplicacao deve chamar um endpoint do prform para integrar com o github e abrir
   diretamente o PR, e retornar o id/link do pullrequest, e o frontend vai copiar para area de
   transferencia esse PR
   1.3. O prmake deve salvar esse pullrequest aberto em uma tabela para registrar os
   pullrequests abertos para aquele card. Salvar o card, id do pull request, repositorio e
   branch, salvar tambem a descricao e titulo do pullrequest.
   1.4. A tela deve carregar os pullrequests abertos. Nessa fase so vamos chamar um endpoint que
   vai retornar os pullrequests ja abertos para aquele card, em outra fase vamos exibir isso em
   tela.

   1.5. Essa tabela de pull requests (github) deve ter relacao com a tabela de pullrequests que
   existe hoje. A tabela pullrequest hoje salva um registro para cada cardxreposityId, vamos
   mudar essa dinamica. O botao salvar vai registrar as informacoes que temos do card, sem a
   necessidade de preencher um root cause ou description nesse momento, nem vai salvar branch
   prefix e branch name, isso tudo agora faz parte da tabela de pullrequestsGithub

2. as informacoes de branch serao movidas pra um novo modal, como falado no item anterior. E
   nessa linha que ocupava essas informacoes, vai ficar apenas o numero do card dentro do seu
   prtopbar, e ao lado dele vai ficar o pr-info-card que hoje fica abaixo.

   2.1. As informacoes de descricao do PR deverao ser movidas para esse modal que vai ser aberto ao clicar em abrir PR.

   2.2. No modal de pullrequest deve ter um botao de icone com tooltip para ver descricao do card. Ao clicar nesse botao, deve ser usado um menu popover, deve ser um popover grande, com conteudo do tamanho do componente que ja existe hoje na tela principal do pullrequest. Basicamente vai abrir esse popover com o component app-card-panel de descricao. As alteracoes que o usuario fizer, deve refletir em todos os lugares que usam descricao.
   2.2.1. Essa sessao deve ser bem parecido com o pr-info-card, ele deve exibir o aberto por, as datas e esses dois botoes. Esses valores devem refletir a branch,card e repositorio selecionado, ou seja, ao alterar qualquer uma dessas informacoes, ele deve alterar.

   2.3. No pr-info-card da tela principal deve ter um botao para o root cause. Ao clicar nesse botao, deve ser usado um menu popover, deve ser um popover grande, com conteudo do tamanho do componente que ja existe hoje na tela principal do pullrequest. Basicamente vai abrir esse popover com o component app-card-panel do root cause. As alteracoes que o usuario fizer, deve refletir em todos os lugares que usam root cause.

3. Na tela principal de pullrequests deve ter um app-card-panel para listar os pullrequests abertos para o card selecionados. Como usamos tambem o primeng, voce pode usar o p-orderList, mas sem usar o order, apenas para ter uma lista interativa que vai mostar o nome da branch, repositorio, data, avatar + nome de quem abriu de cada pr aberto + status atual todo a direita. Um PR pode estar com status OPEN (verde), MERGED(lilas), CLOSED(vermelho), deve consultar isso no github usando os dados desse pr e o backend chamando a api do github para consultar. Pensar em performance, caso seja melhor recuperar essa lista de prs e consultar todos de vez, ou se eh melhor chamar um por um em consultas paralelas

3.1. Clicar em um pr desses vai abrir o modal de abrir PR, porem com as informacoes de branch/prefix, repositorio e descricao preenchidos

4. Com essas alteracoes, o rootcause vai ser um para todos os repositorios (pensando que um bug pode envolver alteracao em mais de um repositorio). Entao vamos ter apenas um registro na tabela pullrequest.

4.1. Com isso, o gerar root cause precisa ser refeito, agora ele deve usar o contexto de multiplos repositorios.

4.2. A tela de gerar com IA vai ter que ter um stepper dentro ja do stepper commit, e o usuario devera buscar os diffs de cada repositorio. Vai funcionar assim:

- o stepper vai listar todos os repos que ha pullrequest aberto (tabela pullrequestsGithub), o usuario devera buscar ao menos o diff de um deles, mas ele pode buscar o diff de todos. Cada diff que buscou de cada commit de cada repositorio deve ser armazenado e passado no contexto para geracao de pull request e
- o stepper deve ter o titulo com o id do repositorio
- a visualizacao de autor, data, sha e description e diff pode ficar igual, mas agora dentro de um stepper vertical daquele repositorio que ja esta dentro do stepper orizontal de commit - resultado.
- deve existir uma funcao nessa tela, para adicionar um repositorio que nao esteja listado. Vai abrir um input com auto complete, o usuario vai buscar um repositorio e clicar em um + para aquele repositorio aparecer dentro dessa lista, pois na maior parte das vezes, o usuario vai tratar o frontend (um repo), o backend (outro repo) e pode fazer algum ajuste no legado (outro repo pra aspnet e outro pra core), nesses casos deve ser possivel adicionar pra quais repositorios ele ja fez commit pra que consiga buscar o diff de cada. Nao deve ser possivel adicionar um repositorio que ja existe na lista do stepper
- commits ja buscados deve marcar o icone do stepper como concluido

4.3. Adicionar um step intermediario entre commit e resultado, vai ser o resumo. Nesse vai listar os repositorios e commit selecionado de cada repo, com a mesma visualizacao que tem na tela de commit: Autor:
acbonfim
Data:
22/09/2026, 16:28:25
SHA:
835ff36840b4
AB#73001
do registro de checklist. Check