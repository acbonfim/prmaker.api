# Feature 0061 — Recomeçar um card do zero (admin)

Para refazer a análise de um card (ex.: o 75294, depois da melhoria da engenharia reversa na 0060) sem mexer no banco à mão.

`POST api/v1/ExecutionPlan/card/{card}/reset?dryRun=true|false` — só `admin`; `dryRun` é o padrão (só conta).
Apaga: planos (etapas, logs, anexos e o conteúdo, perguntas, links, notas), pedidos da fila, Timeline do card, a linha da
tabela de PR (só se não houver PR no GitHub ligado) e o registro do card na engenharia reversa (módulos ligados e itens
consultados). Mantém: lacunas/sugestões/armadilhas da Base Solvace e tudo no DevOps/GitHub.
Recusa (409) com pedido na fila/rodando — cancele antes, senão a sessão recriaria o plano.

Testado localmente (Postgres isolado, API real): ensaio, 403 sem admin, apaga só o card pedido (outro card intacto),
PR sem GitHub apagado, PR com GitHub mantido, 409 com pedido na fila.
