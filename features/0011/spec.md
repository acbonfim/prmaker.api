Objetivo: Adicionar menu para acoes do devops, para automatizar processos que fazemos manualmente no devops

Essa funcionalidade eh opcional, o usuario precisa preencher os campos obrigatorios de integracao para que esse botao habilitite.
Realize a criacao desses campos na configuracao de plugin com nome AI Configurations.
De um nome amigavel a cada campo de configuracao desse
Essa configuracao eh somente pra BUG, quando for um card do tipo US, ele deve ter todas essas configuracoes diferentes e personalizadas pra US.
Essas opcoes abaixo so aparecem pra cards do tipo BUG, para tipo US vamos configurar depois. O frontend possui uma regra pra diferenciar um card do tipo BUG e US (user story). 
Esse botao so habilita caso o card seja encontrado
Quando esse botao estiver habilitado, casa opcao deve mostrar um tooltip detalhado e formatado com o que cada um deles faz

1. No botao Salvar RC no devops, devemos trocar para Ações devops
2. Ao clicar nesse botao, abrir um menu popover com as opcoes:
- Salvar RC no devops -> Salva o root cause no devops
- Gerar resumo (resumo não tecnico) -> Gera um resumo não-técnico (PT-BR + EN-US) para a discussion
  Além do texto técnico, **gere você (Agente de IA) um resumo não-técnico** que dê ao usuário um panorama
  do **problema e da solução adotada** em linguagem de negócio/usual — sem jargão técnico, sem nomes
  de arquivos/métodos, sem código. Foque no impacto para o usuário e no que passou a funcionar.
Esse prompt deve estar na configuracao de plugin AI Configurations, gere uma migration para adicionar esse item, esse campo o usuario nao preenche.
Apos gerar esse texto, apresentar em um editor de texto exatamente igual a descrição do PR ou root cause, e um botao de salvar que vai criar esse comentario direto na discussion
Esse resumo deve ser salvo no banco de dados, como uma nova coluna da tabela pullrequest (atualize a skill do gerar prmake para salvar esse resumo chamado api tambem)
  - Mover para Test in production -> realiza os passos: 
        a. Move o card para Area: Solvace Product Improvement\Release Management (esse campo deve ser configuravel no plugin, de uso pessoal e opcional, mas vem esse informado por padrao)
        b. Move o card para State: Test in production (esse campo deve ser configuravel no plugin, de uso pessoal e opcional, mas vem esse informado por padrao)
        c. Adiciona um comentario no card: moving to test in production (esse campo deve ser configuravel no plugin, de uso pessoal e opcional, mas vem esse informado por padrao)
        
  REGRAS: Esse botao so habilita caso o card esteja sem nenhuma pendencia (o frondend ja tem avisos sobre as pendencias). Coloque tooltip com aviso caso desabilitado com o mouse com icone de bloqueio
          Esse botao so habilita caso o card esteja em Area: Solvace Product Improvement\Product Development Team
  - Mover para Ready for QA
        a. Move o card para State: In Development (done) (campo personalizavel no plugin com nome amigavel)
    REGRAS: Esse botao so habilita caso o card esteja sem nenhuma pendencia (o frondend ja tem avisos sobre as pendencias). Coloque tooltip com aviso caso desabilitado com o mouse com icone de bloqueio
  - Realizar estimativa inicial -> Adiciona os valores aos campos: Original Estimate=6, Remaining Work=6, Completed Work=0 (so habilita se o card estiver sem original), esses valores sao parametrizaveis de maneira opcional por usuario, botao desabilita caso ele nao tenha esse campo especifico habilitado, colocar aviso no tooltip pra isso
  - Zerar remainig -> Zera o valor do Remaining Work (so habilita se o card estiver sem remaining zerado)

