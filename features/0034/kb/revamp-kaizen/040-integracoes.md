## Depende de

**Fila (SQS)**
- `revamp-commentmanager` — envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb> (revamp-Kaizen/Solvace.Kaizen/src/Solvace.Kaizen.Application/Commands/DeleteKaizenComments/DeleteKaizenCommentsCommandHandler.cs:26)


## Usado por

- `revamp-post` — Banco compartilhado: usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS)
- `revamp-rca` — Evento (SNS → fila): consome o tópico KAIZEN (fila RCA_KAIZEN_EVENT_CREATED)
- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS)
- `revamp-buildingblocks` — Banco compartilhado: usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS)

## Filas/tópicos citados no código
- `COMMENT_GROUP_DELETE_WORKER` (Solvace.Kaizen/src/Solvace.Kaizen.Application/Commands/DeleteKaizenComments/DeleteKaizenCommentsCommandHandler.cs:26)
