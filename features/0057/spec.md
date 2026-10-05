# Feature 0057 — Custo do GCP: fila sem long-poll e tela sem loop de GET

Análise de 2026-10-05 (Monitoring do `cime-prod`): a `cime-pullrequest` voltou a ficar cobrada ~24 h/dia desde 02/10
(antes 0,03–1,2 h/dia) e as requisições passaram de ~12 mil/dia para ~83 mil/dia.

1. **Executor** (`prmake-agent` ≤ 1.0.10) chama `GET ExecutionQueue/next?wait=25` sem parar: sempre há uma requisição
   aberta, então a instância do Cloud Run nunca escala a zero (mesmo problema do WebSocket, feature 0013).
   → polling curto: `wait=0` e 10 s entre consultas com a fila vazia. Versão 1.0.11 (autoatualiza).
2. **Front** (`re-revision.component.ts`): `load()` roda dentro de um `effect` e lê `rev()`; ao responder, `rev.set()`
   re-executa o effect → loop infinito de `GET ReverseEngineering/revisions/{id}` (~2/s, 47 KB cada) com a tela aberta.
   → `untracked`. O andamento ao vivo não depende desse GET (`app-re-progress` recebe o `open` do tempo real).

Não mexe em regra de negócio nem na API.
