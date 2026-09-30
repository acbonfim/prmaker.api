## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila LPP_USER_EVENT_CREATED) (revamp-LPP/Solvace.LPP/src/Lambda.LPP.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-post` — envia para a fila POST_WORKER_<amb> (revamp-LPP/Solvace.LPP/src/Solvace.LPP.Application.Abstractions/Constants.cs:19)
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-LPP/Solvace.LPP/src/Solvace.LPP.Application/UseCases/Documents/SendApproval/SendApprovalCommandHandler.cs:17)

**Banco compartilhado**
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-LPP/Solvace.LPP/src/Solvace.LPP.Infra.Data.Global.SqlServer/Queries/UserHelperQueries.cs:39)


## Usado por

- `revamp-post` — Banco compartilhado: usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT)
- `revamp-rca` — Evento (SNS → fila): consome o tópico LPP (fila RCA_LPP_EVENT_CREATED)
- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT)

## Filas/tópicos citados no código
- `POST_WORKER_LOCAL` (Solvace.LPP/src/Solvace.LPP.Application.Abstractions/Constants.cs:19)
- `ASPNETCORE_ENVIRONMENT` (Solvace.LPP/src/Solvace.LPP.Application.Abstractions/Constants.cs:21)
- `POST_WORKER` (Solvace.LPP/src/Solvace.LPP.Application.Abstractions/Constants.cs:22)
- `DB_SOLV_DEV_GLOBAL` (Solvace.LPP/src/Solvace.LPP.API/Program.cs:84)
- `LPP_USER_EVENT_CREATED` (Solvace.LPP/src/Solvace.LPP.API/Extensions/TopicSnsBuilderExtensions.cs:44)
- `LPP_TEAM_EVENT_CREATED` (Solvace.LPP/src/Solvace.LPP.API/Extensions/TopicSnsBuilderExtensions.cs:45)
- `LPP_UDA_EVENT_CREATED` (Solvace.LPP/src/Solvace.LPP.API/Extensions/TopicSnsBuilderExtensions.cs:46)
- `LPP_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.LPP/src/Solvace.LPP.API/Extensions/TopicSnsBuilderExtensions.cs:47)
- `NOTIFICATION_WORKER` (Solvace.LPP/src/Solvace.LPP.Application/UseCases/Documents/SendApproval/SendApprovalCommandHandler.cs:17)
