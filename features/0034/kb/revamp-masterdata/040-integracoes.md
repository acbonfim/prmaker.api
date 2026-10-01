## Depende de

**Fila (SQS)**
- `revamp-users` — envia para a fila USERS_UDA_EVENT_CREATED_<amb> (revamp-MasterData/Solvace.MasterData/src/Solvace.MasterData.API/Extensions/TopicSnsBuilderExtensions.cs:53)
- `revamp-users` — envia para a fila USERS_PHYSICAL_LAYOUT_EVENT_CREATED_<amb> (revamp-MasterData/Solvace.MasterData/src/Solvace.MasterData.API/Extensions/TopicSnsBuilderExtensions.cs:54)


## Usado por

- `revamp-actionplan` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_FORNECEDOR)
- `revamp-alert` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_FALHAS)
- `revamp-bos` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_SHIFT)
- `revamp-defecttag` — Banco compartilhado: usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO)
- `revamp-post` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_CATEGORIA_CUSTO)
- `revamp-rca` — Banco compartilhado: usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO)
- `revamp-unsafecondition` — Banco compartilhado: usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO_FOTO)
- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_FALHAS)
- `revamp-buildingblocks` — Banco compartilhado: usa tabelas TB_MST_* (ex.: TB_MST_FINISHED_PRODUCT)

## Filas/tópicos citados no código
- `DB_SOLV_DEV_GLOBAL` (Solvace.MasterData/src/Solvace.MasterData.API/Program.cs:89)
- `ASPNETCORE_ENVIRONMENT` (Solvace.MasterData/src/Solvace.MasterData.API/Extensions/TopicSnsBuilderExtensions.cs:49)
- `USERS_UDA_EVENT_CREATED` (Solvace.MasterData/src/Solvace.MasterData.API/Extensions/TopicSnsBuilderExtensions.cs:53)
- `USERS_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.MasterData/src/Solvace.MasterData.API/Extensions/TopicSnsBuilderExtensions.cs:54)
