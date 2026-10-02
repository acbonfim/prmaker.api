# Plano — Feature 0044

| Fase | O quê |
|---|---|
| B1 | `ExecutionRequest`: `SessionCostUsd` (acumulado informado), `CostUsd` = diferença; partes da entrada e modelo; migração que corrige os pedidos já gravados |
| B2 | Fim do pedido: consumo final da sessão (do executor) gravado no plano atual da sessão |
| B3 | Linha de base da sessão no plano de correção (análise → correção na mesma sessão); resumo e relatório pelo líquido |
| B4 | Relatório: entrada nova, cache lido, cache escrito, total e modelo |
| B5 | Preços com cache lido/escrito + `GET AiUsage/prices` |
| A1 | Executor 1.0.3: partes da entrada, modelo, sessão e consumo final lido do transcript; texto sem o custo acumulado |
| F1 | `<app-token-usage>` (chip + popover) e `TokenPricingService` |
| F2 | Plano, Meus executores (pedidos e relatório), Meu consumo de IA (tabelas e gráfico) e aviso pós-ação em entrada/saída/total |
| T1 | Teste local: executor real 1.0.3 + Claude falso com custo acumulado, análise → correção na mesma sessão; backfill; telas no Chrome headless |
