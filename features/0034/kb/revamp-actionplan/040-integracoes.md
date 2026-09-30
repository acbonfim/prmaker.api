## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila ACP_USER_EVENT_CREATED) (revamp-ActionPlan/Solvace.ActionPlan/src/Lambda.ACP.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-ActionPlan/Solvace.ActionPlan/src/Solvace.ActionPlan.Application/Commands/ActionPlan/ActionPlanNotification/ActionPlanNotificationFactory.cs:8)
- `revamp-commentmanager` — envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb> (revamp-ActionPlan/Solvace.ActionPlan/src/Solvace.ActionPlan.Application/Commands/DeleteActionPlanComments/DeleteActionPlanCommentsCommandHandler.cs:29)

**Banco compartilhado**
- `revamp-masterdata` — usa tabelas TB_MST_* (ex.: TB_MST_FORNECEDOR) (revamp-ActionPlan/Solvace.ActionPlan/scripts/Sync_ReplicaTables.sql:133)
- `revamp-digitalobeya` — usa tabelas TB_DOB_* (ex.: TB_DOB_WIDGET) (revamp-ActionPlan/Solvace.ActionPlan/src/Solvace.ActionPlan.Infra.Data.Local.SqlServer/Queries/GetObeyaBoardsLocalQuery.cs:21)


## Usado por

- `revamp-rca` — Banco compartilhado: usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN)
- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN)
- `revamp-buildingblocks` — Banco compartilhado: usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN)
- `revamp-moduleintegration` — Banco compartilhado: usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN)

## Filas/tópicos citados no código
- `DB_SOLV_DEV_GLOBAL` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Program.cs:84)
- `ASPNETCORE_ENVIRONMENT` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:14)
- `ACP_USER_EVENT_CREATED` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:18)
- `ACP_TEAM_EVENT_CREATED` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:19)
- `ACP_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:20)
- `ACP_SUPPLIER_EVENT_CREATED` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:21)
- `ACP_APPLICATION_EVENT_CREATED` (Solvace.ActionPlan/src/Solvace.ActionPlan.API/Extensions/TopicSnsBuilderExtensions.cs:22)
- `NOTIFICATION_WORKER` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application/Commands/ActionPlan/ActionPlanNotification/ActionPlanNotificationFactory.cs:8)
- `COMMENT_GROUP_DELETE_WORKER_LOCAL` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application/Commands/DeleteActionPlanComments/DeleteActionPlanCommentsCommandHandler.cs:29)
- `COMMENT_GROUP_DELETE_WORKER` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application/Commands/DeleteActionPlanComments/DeleteActionPlanCommentsCommandHandler.cs:31)
- `OUTLOOK_EVENT_ID` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application.Abstractions/Queries/Entities/ActionPlanFull.cs:177)
- `CREATED_USER_ID` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application.Abstractions/Queries/Entities/ActionPlanGroup.cs:23)
- `UPDATED_USER_ID` (Solvace.ActionPlan/src/Solvace.ActionPlan.Application.Abstractions/Queries/Entities/ActionPlanGroup.cs:26)
