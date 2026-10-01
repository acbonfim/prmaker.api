## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila BOS_USER_EVENT_CREATED) (revamp-BOS/Solvace.BOS/src/Lambda.BOS.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-BOS/Solvace.BOS/src/Solvace.BOS.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:37)

**Banco compartilhado**
- `revamp-masterdata` — usa tabelas TB_MST_* (ex.: TB_MST_SHIFT) (revamp-BOS/Solvace.BOS/src/Solvace.BOS.Infra.Data.Global.SqlServer/Queries/FeedbackQueries.cs:44)
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-BOS/Solvace.BOS/src/Solvace.BOS.Infra.Data.Global.SqlServer/Queries/UserHelperQueries.cs:39)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `BOS_USER_EVENT_CREATED_WORKER` (Solvace.BOS/src/Solvace.BOS.Queue.Worker/ProcessUser.cs:24)
- `ASPNETCORE_ENVIRONMENT` (Solvace.BOS/src/Solvace.BOS.Queue.Worker/Subscription/SubscriptionQueue.cs:37)
- `DB_SOLV_DEV_GLOBAL` (Solvace.BOS/src/Solvace.BOS.API/Program.cs:79)
- `BOS_USER_EVENT_CREATED` (Solvace.BOS/src/Solvace.BOS.API/Extensions/TopicSnsBuilderExtensions.cs:47)
- `BOS_TEAM_EVENT_CREATED` (Solvace.BOS/src/Solvace.BOS.API/Extensions/TopicSnsBuilderExtensions.cs:48)
- `BOS_UDA_EVENT_CREATED` (Solvace.BOS/src/Solvace.BOS.API/Extensions/TopicSnsBuilderExtensions.cs:49)
- `BOS_CONTRACTOR_EVENT_CREATED` (Solvace.BOS/src/Solvace.BOS.API/Extensions/TopicSnsBuilderExtensions.cs:50)
- `NOTIFICATION_WORKER` (Solvace.BOS/src/Solvace.BOS.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:37)

## ⚠️ Atenção
- Lambda `lambda_bos_user_create_event` declarada em revamp-bos, revamp-fishbone — um deploy sobrescreve o outro
