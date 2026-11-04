# 0077 — O PRMake conversa com quem trata o card (assistente, textos gerados editáveis, horas)

## Pedido (do usuário, 2026-11-04)
- A aplicação conversar comigo **sem travar nenhum fluxo**: ao iniciar a análise, perguntar se quero a estimativa inicial;
  perguntar se quero que sejam inseridos os resumos dos passos que precisam aguardar alguma ação (chamado, replicar no
  ambiente).
- Como já editamos o resumo não técnico, poder **alterar root cause, comentários de resumo etc.**: um botão (como Análises,
  Dados, Anexos) para **todos os textos gerados**, com título e informações importantes, permitindo **editar**, **gerar de
  novo com IA** com contexto mais rico ou personalizado, **editar no DevOps** (comentário já enviado → revisar, editar e
  mandar a correção) e **enviar** se ainda não foi.
- Sugerir **horas em Completed**: por padrão, ao puxar o card, estimativa inicial 6h e 6h de Remaining; conforme a solução
  avança, sugerir +1h em Completed e −1h em Remaining. Média do time: 3h por card (varia com a complexidade).
- Liberdade para propor outras melhorias com base nessas necessidades.

## Como fica
### Assistente do card (sugestões que não travam)
- Lista de sugestões no card, cada uma com ações; ninguém espera a resposta — o plano e a skill seguem. Uma vez por card
  (chave de deduplicação); respondida, mostra o resultado; falhou (DevOps recusou), dá para tentar de novo; dispensável.
- **Estimativa inicial** — ao criar o plano de análise, se o card não tem Original Estimate: "Registrar estimativa"
  (valores do AI Configurations) · "Agora não".
- **Resumos de espera na discussion** — se a pessoa ainda não tem preferência: "Sim, neste card" · "Sim, sempre" ·
  "Não, eu publico" (por card, por usuário ou manual; o cartão da situação mostra de onde vem o modo).
- **Horas** — ao concluir as etapas configuradas (`AssistantHoursSteps`, padrão montar-analise, propor-solucoes,
  corrigir-*, validar-qa): "Lançar 1h" · "Lançar 2h" · "Agora não", com Completed/Remaining atuais e o que fica, e aviso
  quando passa da média do time (`AssistantAverageHours`, 3h). Ação nova no DevOps: `log-hours` (soma em Completed, tira do
  Remaining sem negativo); `GET Azure/card/{id}/effort`.
- Configuração no Skills Configurations: `AssistantEnabled`, `AssistantHoursSteps`, `AssistantHoursPerStep`,
  `AssistantAverageHours` (seed).

### Textos gerados (um botão, como Análises/Dados/Anexos)
- Todos os textos do card num lugar: análise, soluções, descrição do PR, root cause, resumo não técnico (PT/EN),
  orientação ao cliente, passo a passo da configuração, texto do chamado, resumo da situação, handover.
- Cada um com título, origem, quando foi gerado/editado, **onde está publicado** (Timeline, discussion, campo do DevOps) e
  se a versão publicada está desatualizada.
- **Editar** (salva no PRMake), **Gerar de novo com IA** (instruções da pessoa + contexto escolhido: card, discussion,
  análise, situação; usa a chave de IA da própria pessoa e nunca salva sem ela revisar), **Enviar/Atualizar no DevOps**
  (o mesmo comentário/campo é atualizado — resumo não técnico, resumo da situação, root cause; Timeline: edita a entrada).
- Back: `PUT ExecutionPlan/{id}/artifacts/{aid}/text`; publicar a situação com texto editado (`markdown`).

### Outras melhorias propostas
- Horas visíveis no cartão do card (Original · Completed · Remaining, média do time).
- O resultado de cada ação do assistente vai para a Timeline pelo caminho das Ações DevOps (quem fez, o quê).
