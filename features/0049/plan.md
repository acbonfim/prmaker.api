# Plano — Feature 0049

| Fase | O quê | Depende |
|---|---|---|
| B1 | Fila: `Phase`/`NewSession` no pedido (migração), sessão nova na correção (quem começou a sessão; fallback: sessões do plano de análise), prompt da correção, plano de correção com a sessão em execução | — |
| B2 | Fila: espera depois de comentário (`Delay`/`RunNow`, `gathering`), "Continuar" começa já; consumo final do executor não marca o plano como vivo | — |
| B3 | Host: `ExecutionQueueSettings` + seed `ExecutorCorrectionNewSession`/`ExecutorNoteDelaySeconds` | B1, B2 |
| S1 | Skill: `contexto-correcao`, resumo para a correção no checkpoint de `propor-solucoes`, `prmake_file` direto no executor | — |
| F1 | Front: `gathering` com "Começar agora", aviso de correção em sessão nova | B2 |
| T1 | API local + Postgres isolado: análise → respostas → correção em sessão nova → comentários agrupados; card antigo; chave desligada; script | todas |
