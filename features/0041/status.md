# Status — Feature 0041

| Fase | Descrição | Status |
|---|---|---|
| B1 | Serviços compartilhados REST/MCP | ✅ concluída |
| B2 | Ferramentas MCP novas | ✅ concluída |
| B3 | Correção herdando a sessão | ✅ concluída |
| B4 | Limite da conta na fila | ✅ concluída |
| B5 | Medição MCP × script | ✅ concluída |
| A1 | Executor 1.0.2 | ✅ concluída |
| S1 | Skills MCP primeiro | ✅ concluída |
| F1 | Front | ✅ concluída |
| Q1 | Teste local | ✅ concluída |
| D1 | PRs e merge | 🚀 |

## Log
- 2026-10-01: spec, plano e branches `feature/0041` (back e front).
- 2026-10-01: B1–B5, A1, S1, F1 implementadas; Q1 executado.

## Q1 — teste local (Postgres 18 isolado, auth falso, executor 1.0.2 com `claude` falso, Chrome headless)
- MCP com 23 ferramentas. Ciclo inteiro pelo MCP num plano criado como a skill cria: `prmake_advance` (com registro),
  `prmake_checkpoint`, `prmake_block`/`prmake_unblock`, `prmake_ask` + `prmake_answer` (por número), `prmake_link`
  (chamado que trava a etapa), `prmake_plan` (checkpoint/atividade/links), `prmake_correction` → plano de correção
  **com a sessão herdada** (`sess-analise-1`), `prmake_step` no plano de correção (fase padrão = correção),
  `prmake_config` (Skills Configurations), `prmake_devops_config` (ações + classificações), `prmake_devops classify`
  com preset inválido devolve a mensagem com as opções. **Ajuste**: `prmake_card` sem integração do Azure dava erro
  genérico — agora erros de integração/DevOps vão com a mensagem.
- `prmake-plan.sh use <card> correction` sem estado local achou no PRMake o plano criado pelo MCP.
- Limite da conta: `claude` falso devolvendo "You've hit your session limit · resets 7:50pm (America/Bahia)" às 21:01 →
  pedido de volta à fila até 02/10 19:51 **com 0 tentativas gastas**, máquina `throttledUntil`, o outro pedido da fila
  com motivo `throttled` sem ser pego, executor parado até o reset.
- Medição: transcripts sintéticos (6 chamadas MCP × 6 pelo script) enviados pelo `send_usage` real → sessões com
  `mcpCalls`/`scriptCalls` → relatório "Com MCP 127 mil × Sem MCP 187 mil tokens de entrada por plano"; tela com o
  comparativo e o limite da conta. **Ajuste**: aviso de executor desatualizado só quando a versão publicada é mais nova.
