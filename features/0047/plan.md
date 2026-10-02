# Plano — Feature 0047

| Fase | O quê |
|---|---|
| B1 | Domínio: `ExecutionModelUsage` (modelo, respostas, entrada nova, cache lido/escrito, saída) em `ExecutionSession.Models`/`BaseModels` (JSON da sessão); `RecordUsage` com modelos; linha de base por modelo; `NetModels()`; migração do snapshot |
| B2 | Resposta do plano (`usage.models`) e relatório (`models` por linha, média por plano); request de consumo aceita `models` |
| B3 | Fila: fase do pedido (análise × correção) e modelo da configuração na resposta do `claim` (`phase`, `model`); seed `ExecutorAnalysisModel=opus`, `ExecutorCorrectionModel=sonnet` |
| E1 | Executor 1.0.6: `--model` do claim; modelo da execução pelo evento `init`; tokens por modelo do transcript no consumo final |
| S1 | Skill: `model: opus` no cabeçalho da `analisar-bug`; `usage` manda os tokens por modelo |
| F1 | Front: `TokenUsage.models` — estimativa = soma por modelo; painel com linha por modelo; plano e relatório passam os modelos |
| T1 | Teste local: claim por fase/modelo; consumo com dois modelos (plano, linha de base da correção, relatório); tela |
