# 0103 — Agendamento de itens de trabalho e sub-tarefas

Pedido do usuário (2026-11-25):

> na aplicacao temos situacoes onde precisamos aguardar algum horario para executar. Por ex:
>
> Abrimos um pr e fazemos merge, mas nossa esteira em alguns ambientes leva umas 2h para executar, outras pipelines sao
> acionadas manualmente. Entao nao podemos tentar validar no ambiente antes disso.
>
> Outros casos seria o caso do card 78794, no #1080 vou precisar aguardar pra testar depois das 04:30 do meu timezone
> (brasilia) pra testar novamente e seguir com o tratamento.
>
> Com isso, eu gostaria que fosse possivel agendar pra uma tarefa iniciar em uma data/horario especifico.

## O que precisa

1. Um item de trabalho segue o fluxo normal, mas uma **sub-tarefa agendada** não executa automaticamente — só no horário
   definido. As sub-tarefas seguintes podem executar, **só as que não dependem direta ou indiretamente** da agendada.
2. Criar um **item de trabalho agendado** para iniciar numa data/hora específica.
3. O **especialista e todas as skills** conhecem o agendamento: podem criar itens separados já agendados quando sabem que
   será preciso esperar (ex.: PR mesclado → item para validar no ambiente das branches abertas — development, qa —
   daqui a ~2h30), ou deixar itens **em espera sem data** (opcionais: a pessoa decide se executa e se define data).
4. **Alterar** a data/hora de itens/sub-tarefas ainda não executados, **cancelar o agendamento** ou **executar agora**.
5. Itens e sub-tarefas agendados mostram um **relógio de agendamento na cor lilás**.
6. **Agendar** uma sub-tarefa (ou item) que ainda não executou e foi criada sem agendamento.

Horários: a pessoa pensa no fuso dela (Brasília); o servidor guarda em UTC.
