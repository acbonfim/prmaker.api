## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila RCA_USER_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.User/aws-lambda-tools-defaults.json)
- `revamp-centerline` — consome o tópico CENTERLINE (fila RCA_CENTERLINE_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.Centerline/aws-lambda-tools-defaults.json)
- `revamp-kaizen` — consome o tópico KAIZEN (fila RCA_KAIZEN_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.Kaizen/aws-lambda-tools-defaults.json)
- `revamp-checklist` — consome o tópico CHECKLIST (fila RCA_CHECKLIST_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.Checklist/aws-lambda-tools-defaults.json)
- `revamp-documentation` — consome o tópico DOCUMENTATION (fila RCA_DOCUMENTATION_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.Documentation/aws-lambda-tools-defaults.json)
- `revamp-lpp` — consome o tópico LPP (fila RCA_LPP_EVENT_CREATED) (revamp-RCA/Solvace.RCA/src/Lambda.RCA.Schema.LPP/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-post` — envia para a fila POST_WORKER_<amb> (revamp-RCA/Solvace.RCA/src/Solvace.RCA.Application.Abstractions/Constants.cs:16)
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-RCA/Solvace.RCA/src/Solvace.RCA.Application/Shared/RcaNotificationHelper.cs:28)

**Banco compartilhado**
- `revamp-actionplan` — usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN) (revamp-RCA/Solvace.RCA/src/Solvace.RCA.Application.Abstractions/Constants.cs:41)
- `revamp-masterdata` — usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO) (revamp-RCA/Solvace.RCA/src/Solvace.RCA.Infra.Data.Global.SqlServer/Commands/EquipamentCommand.cs:47)


## Usado por

- `revamp-post` — Banco compartilhado: usa tabelas TB_SA3_* (ex.: TB_SA3_A3)
- `revamp-whywhy` — Evento (SNS → fila): consome o tópico RCA (fila WHYWHY_RCA_EVENT_CREATED)
- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_SA3_* (ex.: TB_SA3_A3)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_SA3_* (ex.: TB_SA3_A3)
- `revamp-moduleintegration` — Banco compartilhado: usa tabelas TB_SA3_* (ex.: TB_SA3_A3)

## Filas/tópicos citados no código
- `POST_WORKER_LOCAL` (Solvace.RCA/src/Solvace.RCA.Application.Abstractions/Constants.cs:16)
- `ASPNETCORE_ENVIRONMENT` (Solvace.RCA/src/Solvace.RCA.Application.Abstractions/Constants.cs:18)
- `POST_WORKER` (Solvace.RCA/src/Solvace.RCA.Application.Abstractions/Constants.cs:19)
- `DB_SOLV_DEV_GLOBAL` (Solvace.RCA/src/Solvace.RCA.API/Program.cs:164)
- `RCA_USER_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:57)
- `RCA_UDA_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:58)
- `RCA_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:59)
- `RCA_TEAM_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:60)
- `RCA_EQUIPAMENT_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:61)
- `RCA_CENTERLINE_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:62)
- `RCA_CHECKLIST_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:63)
- `RCA_DOCUMENTATION_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:64)
- `RCA_KAIZEN_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:65)
- `RCA_LIL_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:66)
- `RCA_LPP_EVENT_CREATED` (Solvace.RCA/src/Solvace.RCA.API/Extensions/TopicSnsBuilderExtensions.cs:67)
- `NOTIFICATION_WORKER` (Solvace.RCA/src/Solvace.RCA.Application/Shared/RcaNotificationHelper.cs:28)
