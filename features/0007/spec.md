Objetivo: Permitir integrar com o teams e enviar mensagem para um grupo especifico.

Features: 

1. Ter um botao na linha do PR onde mostra a lista de prs para enviar uma mensagem para o teams solicitando aprovacao
2. Ao clicar no botao, chamar a integracao com o teams e enviar a mensagem
3. Adicionar atalhos rapidos, apertando com botao direito no mouse na linha do pr para as acoes (sugerir via tooltip apertar o botao direito do mouse para acessar atalhos):
- Abrir no github (para prs ja abertos no github)
- Copiar link do pr para clipboard
- Abrir PR rapido (esse vai abrir um popover com o componente de branchs que fica no modal), vai permitir o usuario abrir um pullrequest para outra branch de forma rapida
- Alterar status (trocar de DRAFT para OPEN ou CLOSED por ex), clicar nesse botao vai abrir um menu popover com as opcoes de status, clicou no status ele ja atualiza o pr no github
4. Permitir acessar o menu de atalhos tambem clicando num botao no canto superior direito do item da lista de prs, com o icone de um Raio (atalhos rapidos), botao discreto mas visivel facilmente
Regras:

1. O grupo para qual vamos enviar a mensagem deve ser configuravel via plugin configuration
2. Crie uma migration para gerar esse plugin e preencher ele com so dados necessarios
3. Esse plugin vai ter a integracao com o teams, api key (esse de uso pessoal), o restante dos campos so edita por admin
4. Esse plugin tambem vai ter um layout de geracao dessa mensagem, como textos e opcoes de envio para formatar a mensagem
5. A configuracao pro teams nao eh obrigatoria para acessar a tela de registro do PR, mas o botao fica desabilitado ate o usuario configurar