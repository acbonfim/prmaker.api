# Feature 0068 — Executor acordado pelo relay: fim do polling da fila no Cloud Run

Análise de 2026-10-06 (Monitoring + logs de requests do `cime-prod`): a `cime-pullrequest` segue cobrada ~24 h/dia
(79–88 mil s/dia desde 02/10, ~US$ 60/mês) mesmo depois da 0057.

1. **Executor antigo em long-poll.** Um `prmake-agent` 1.0.8 numa outra máquina segue chamando
   `GET ExecutionQueue/next?wait=25` sem parar (sozinho mantém a instância ligada). Ele não se autoatualiza: até a
   1.0.11 o laço do sinal de vida morre no primeiro timeout (corrigido na 1.0.12). O servidor ainda aceita `wait` > 0.
   Zerar o `wait` no servidor não basta: depois de um 204 o 1.0.8 pergunta de novo na hora (laço apertado).
   → o servidor recusa `wait` > 0 com **426** ("atualize o executor"): o 1.0.8 cai no backoff (até 60 s entre
   tentativas) e o long-poll deixa de existir no servidor.
2. **Polling curto ainda custa.** Cada 1.0.12 ligado pergunta a cada 10 s (~0,6 s por consulta, ~4.700 s/dia por
   máquina); com 2–3 máquinas ligadas o dia todo passa da cota grátis.
   → o executor fica conectado ao **relay de tempo real** (MonsterASP, fora do Cloud Run — 0013) e só consulta a fila
   quando a API publica `executionQueueReady` no grupo do dono, quando o sinal de vida (60 s, já existente) diz que
   há pedido pronto (`queueReady`) ou quando um job termina. Sem relay (desenvolvimento ou relay fora), volta ao
   polling de 10 s.

O sinal de vida passa a rodar a manutenção da fila (trava vencida, fila expirada, motivos de espera) e a regra
automática — antes rodavam no `next`. Pedidos que ficam prontos com o tempo (nova tentativa, comentários reunidos,
orçamento do dia, limite da conta) começam em até 1 min, pelo `queueReady`.

Não muda regra de negócio nem a tela.
