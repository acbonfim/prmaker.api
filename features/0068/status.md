# Status — Feature 0068

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ implementada (build ok) | Claude | 36ce542 |
| E1 | ✅ implementada (build + publish trimmed osx-arm64 sem avisos) | Claude | ver PR |
| T1 | ✅ ok (2026-10-06) | Claude | — |
| T2 | ⏳ depois do deploy | — | — |

## T1 (local, fora do git em `.t0068/`)
- Relay local com `TokenSigningKey`/`RelayKey`, API real em `RealTime:Mode=Relay` contra Postgres 18 isolado (docker),
  auth falso, executor 1.0.13 publicado (trimmed) com HOME isolado. `e2e.py`: registro, `report` sem fila
  (`queueReady=false`), `GET ExecutionWorker/realtime` (URL/grupo/evento), `next?wait=25` → 426, `next?wait=0` → 204
  na hora, executor conectado sem nenhum `next` em 20 s, pedido novo → o relay acorda o executor (1 `next`),
  `queueReady=true` na máquina-alvo, claim, nova tentativa no futuro → `queueReady=false`. Tudo OK.
- Com API falsa: relay derrubado → volta a consultar a cada 10 s; relay de volta → reconecta (backoff 8/16/32 s…),
  consulta uma vez e fica quieto; evento para outro grupo não acorda.

## Notas
- O relay não muda (o hub já aceita `AddToGroup` de qualquer conexão com token da API). O evento não leva dado
  nenhum além do sinal — quem entrar no grupo de outro usuário só fica sabendo que "há pedido".
- O executor 1.0.8 (sem autoatualização) para de pegar pedidos com o 426: reiniciar o executor nessa máquina faz
  ele se atualizar no primeiro sinal de vida. Os 1.0.11/1.0.12 seguem funcionando (polling de 10 s) e se atualizam
  sozinhos para a 1.0.13.
- `realtime` nas capacidades do executor aparece a partir do segundo sinal de vida (o primeiro sai antes de conectar).
- T2: esperado `cime-pullrequest` com poucos minutos/dia cobrados; nos logs, `next` só depois de pedido criado.
