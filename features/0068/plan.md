# Plano — Feature 0068

Branch `feature/0068` em `prform.api-0068` (só backend + executor; o front não muda).

| Fase | O quê | Depende |
|---|---|---|
| B1 | `ExecutionQueue/next`: `wait` > 0 → 426; `NextAsync` sem espera (sai `Signals`/`WaitSignalAsync`); `Pulse` → evento `executionQueueReady` no grupo `execworkers:{dono}` pelo relay; `report` roda manutenção + regra automática e devolve `queueReady`; `GET ExecutionWorker/realtime` (URL do hub, token curto, grupo, evento) | — |
| E1 | Executor 1.0.13: cliente SignalR mínimo (negotiate + WebSocket, protocolo JSON) no relay; laço principal espera o sinal (relay, `queueReady`, job terminado) com reserva de 10 min conectado / 10 s sem relay; `realtime` nas capacidades | B1 |
| T1 | Teste local: relay local + `/publish` acordando o executor; build da API e do executor | B1, E1 |
| T2 | Depois do deploy: `billable_instance_time` da `cime-pullrequest` e as chamadas de `next` nos logs | merge |
