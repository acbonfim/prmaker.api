Objetivo: Implementar o uso da api do claude como um dos plugins da estutura que existe.

1. Existe ja uma configuracao no Solvace.AI com a implementacao do ClaudeService.
2. Precisa validar se esta tudo correto e/ou configurar e implementar o que precisa
3. Essa tarefa vem depois da 0002, entao aqui a api key vem do api key do usuario.
4. Existe um plugin configuration chamado Claude Plugin, com ApiKey, BaseUrl e Model. Caso precise de mais configuracoes, preciso que deixe isso mapeado
5. Sugira qual melhor modelo que podemos usar gastando pouquissimo, somente para analisar texto e gerar. O contexto sempre vai ser o texto enviado, entao nao precisa guardar nada em memoria.
6. Sugira se estamos usando a melhor abordagem para fazer isso? Usamos para gerar descricao de pr e root cause com base no que foi alterado em codigo (diff do git), textos inseridos na timeline, e todo historico do card no devops