objetivo: Hoje a aplicacao esta usando as configuracoes de plugins para buscar coisas no git, azure devops e tudo mais. O problema eh que isso faz todo mundo escrever ou ler com o mesmo access token
e parece que a aplicacao esta sendo usada apenas por uma pessoa, e devido ha algumas mudancas recentes, vamos precisar que cada um salve com seu proprio token.

1. Preciso que as configuracoes de plugins sejam mantidas, mas que elas recebam uma flag chamada "Uso pessoal". E um tooltip explicando que 
configuracoes que tem essa marcacao, sao configuracoes que todo usuario precisa fazer para usar toda a aplicacao
2. Essa configuracao ficara dentro do menu ao clicar no nome do usuario, deve ter um novo item chamado "Minhas integracoes"
2.1 Deve abrir um modal e listar todas configuracoes obrigatorias necessarias
2.2 Devem ser as mesmas configuracoes e campos cadastrados la nos plugins
2.3 Usuario deve conseguir preencher todos os campos e todas configuracoes que sao listadas nessa tela
3. A aplicacao deve passar a usar essas configuracoes para as integracoes com devops e github por hora.
4. Usuario que nao tem essa configuracao configurada, nao deve conseguir usar as funcoes que usam essas informacoes, por ex a tela de pull request register nao deve ficar habilitada,
backend tambem deve bloquear
5. Deve ser usar a estrategia de cache da mesma maneira que sao usados os plugins globais
6. Essas configuracoes devem ficar salvas em uma tabela a parte
7. Deve ser dinamico assim como sao os plugins globais, mas deve ser compeltamente atrelado aos plugins globais
8. Remover ou desativar um plugin global, ou desmarcar ele como de usuario, ele nao deve aparecer para o usuario consultar e tambem nao deve ser lido como dado pessoal
9. Plugins nao marcados como pessoais devem continuar sendo lidos como ja eh hoje.