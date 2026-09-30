Objetivo: Melhorar as analises, ser mais assertivo e ter conhecimento das regras de negocio. Priorizar gastar menos tokens

1. Existe uma iniciativa que ainda nao esta 100% em producao, mas que ja podemos acessar as bases de dados e fazer essas consultas. Se chama KnowLedge center e esta no repo github.com/electradv/revamp-KnowledgeCenter, baixado na minha maquina.
1.1 A aplicacao deve consultar online essas informacoes de regra de negocio conforme necessidade.
1.2 Usar os bancos de dados cadastrados, incialmente essa iniciativa so existe no ambiente de dev https://api-knowledge-center-dev.solvacelabs.com, entao so deve existe no banco de DEV/QA. Deve ser possivel mudar no futuro para usar apontando pra producao
1.3 Consultar base de conhecimento para entender regras de negocio, usos do sistema e afins
1.4 A skill analisar-bug deve sempre consultar quando tiver necessidade para tirar duvidas sobre regra de negocio.
2. Gostaria que fizesse uma analise profunda em toda arquitetura solvace e desenhasse uma engenharia reversa. Seria uma consulta rapida para economizar token e ser mais assertivo nas analises.
2.1. O objetivo seria ter acesso facil as informacoes, mais rapido, facilitar as analises e gastar menos token
2.2. Analisar a arquitetura de todos projetos, jobs, aws, banco de dados, estrutura de dados e tudo que for possivel de todos projetos
2.3. Entender se faz sentido salvar isso no banco de dados do prmake e sempre acessar por la, ou usar as skills para baixar esses dados de engenharia reversa pra maquina do cliente e manter sempre atualizado. Ver se faz sentido usar as skills pra baixar as engenharias reversas.
2.4. Criar uma tela no prmake para ver exatamente essa engenharia reversa salva, projeto por projeto, sessao por sessao, ter partes onde analisa os modulos, como um se integra com o outro, parte de infra, aws, servicos de terceiros, login e afins
2.5. Deve ser possivel analisar tudo em tela, editar, sugerir melhorias (conectar com plugin de ia configurado e abrir um chat para solicitar melhoria com o especialista no solvace) Essa acao so permite se for um admin e tiver plugin configurado corretamente
2.6. A skill analisar-bug deve aproveitar toda essa engenharia reversa para usar menos tokens e melhorar a velocidade e qualidade das analises.
3. Promover solicoes nas skills atuais e fluxos de tratamento de bug, para reduzir custo de tokens, melhorar assertividade e velicidade nos tratamentos
4. Sugerir melhorias nas conexoes entre prmake e claude code no plano de execucao, pois muitas vezes precisamos pausar um tratamento e seguir para o proximo enquanto nao recebemos resposta, mas ao voltar pro anterior, precisamos copiar o comando e colar o /analisar-bug xxx no claude code novamente. Poderia ter alguma forma de retomar o raciocinio sem fazer isso, caso seja possivel 