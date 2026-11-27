# 0105 — Status

| Fase | Status | Notas |
|---|---|---|
| B1 | ✅ | 12 testes do cálculo (dias úteis, feriados, faixas, parado, origem, regras digitadas na tela) |
| B2 | ✅ | board, card aberto, `Azure/card/{id}/full` |
| B3 | ✅ | seção "SLA dos cards" logo depois de Azure DevOps |
| F1 | ✅ | |
| F2 | ✅ | |
| Q1 | ✅ | harness `.t0105` (Postgres `cime-pg-0099c`, DevOps falso 8199, API 5099, front 4399): 90001 grave 1 d.u., 90002 ok 22 d.u., 90004 atenção 10 d.u., 90006 Cumprido, 90007 Vencido há 2 d.u.; US sem selo; PATCH `SlaGraveMaxDays=10` → 90004 grave na hora. Suíte: 696 testes ok |

## Log
- 2026-11-27: feature completa, PRs abertos (back + front).

## Pendências
- Conferir com cards reais (Found by/Creation Date preenchidos em todos os bugs do HubSpot?).
- Pausar o prazo em "Waiting for information" não foi pedido — hoje conta direto.
