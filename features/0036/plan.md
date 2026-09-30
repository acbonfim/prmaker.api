# Plano — 0036

## Diagnóstico
Um clique em uma opção fazia, em série, antes de a tela mudar:
1. `POST .../questions/{id}/answer` — grava a resposta, **depois** publica no relay de tempo real (HTTP para o
   MonsterASP) e **depois** grava na Timeline (que publica de novo no relay);
2. só então a tela recarregava o plano inteiro (`GET`) para mostrar a resposta.
Enquanto isso o botão só ficava desabilitado (quase imperceptível), e um clique durante outra ação (`busy`) era ignorado.

## Fases
| Fase | O quê |
|---|---|
| F1 | Resposta otimista: a escolha aparece na hora ("resposta · enviando…"), as opções somem; a confirmação do servidor aplica a pergunta devolvida no plano sem esperar a recarga; erro devolve as opções. Independente do `busy` geral; várias perguntas podem ser respondidas em sequência. |
| B1 | `AnswerAsync`: tempo real e Timeline em paralelo (`Task.WhenAll`) — contextos distintos, sem o DbContext do plano. O envio ao relay continua aguardado dentro do request (CPU do Cloud Run só durante o request). |
| Q1 | Teste local com o POST atrasado 3 s e com erro simulado. |
