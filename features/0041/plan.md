# Plano — 0041

| Fase | O quê |
|---|---|
| B1 | Serviços compartilhados REST/MCP: `SkillsConfigService` (sai do SkillsController) e `DevOpsActionRunner` (ação + Timeline, sai do AzureController) |
| B2 | Ferramentas MCP novas + `prmake_plan` com checkpoint/atividade/links + `prmake_devops` via runner (Timeline) e `classify` |
| B3 | Correção herdando a sessão do plano de análise (`ExecutionPlan.InheritSessions`) |
| B4 | Limite da conta: `FinishExecutionRequestRequest.RetryAt`, `ExecutionRequest.Throttle`, `ExecutionWorker.ThrottledUntil`, motivo `throttled` (migração `AddWorkerThrottle`) |
| B5 | Medição: `ExecutionSession.McpCalls/ScriptCalls`, `RecordExecutionUsageRequest` com os dois, `GET ExecutionPlan/usage-report` |
| A1 | Executor 1.0.2: detecção do limite + `retryAt` + não pegar pedidos até o reset |
| S1 | Skills: SKILL.md e plano-execucao MCP primeiro; `send_usage` conta MCP × script (e respeita `CLAUDE_CONFIG_DIR`); `use correction` busca no servidor |
| F1 | Front: motivo `throttled` na faixa e em Meus executores; comparativo de consumo |
| Q1 | Teste local (MCP com cliente JSON-RPC, executor com `claude` falso no limite, relatório) |
| D1 | PRs back e front, merge juntos |

Decisões:
- "Com MCP" = plano cujas sessões têm `McpCalls >= ScriptCalls` e `McpCalls > 0` (só dá para medir com custo enviado).
- Reset sem horário reconhecível → 30 min. Reset a mais de 24 h → 24 h.
- `prmake_plan_status` continua existindo, mas a SKILL.md conclui o plano pelo `status` do script (é ele que envia o
  custo da sessão).
