## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila ALERT_USER_EVENT_CREATED) (revamp-Alert/Solvace.Alert/src/Lambda.Alert.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Alert/Solvace.Alert/src/Solvace.Alert.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:28)
- `revamp-commentmanager` — envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb> (revamp-Alert/Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToCommentService.cs:13)

**Banco compartilhado**
- `revamp-masterdata` — usa tabelas TB_MST_* (ex.: TB_MST_FALHAS) (revamp-Alert/Solvace.Alert/src/Solvace.Alert.Infra.Data.Global.SqlServer/Queries/AlertQueries.cs:43)
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-Alert/Solvace.Alert/src/Solvace.Alert.Infra.Data.Global.SqlServer/Queries/Helpers/User/Queries/UserHelperQueries.cs:39)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:28)
- `COMMENT_GROUP_DELETE_WORKER_LOCAL` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToCommentService.cs:13)
- `COMMENT_GROUP_DELETE_WORKER` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToCommentService.cs:15)
- `ACP_DELETE_WORKER_LOCAL` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToActionPlanService.cs:26)
- `ASPNETCORE_ENVIRONMENT` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToActionPlanService.cs:28)
- `ACP_DELETE_WORKER` (Solvace.Alert/src/Solvace.Alert.Application/UseCases/DeleteAlert/Service/SendAlertDeletedToActionPlanService.cs:29)
- `ALERT_CLASSIFICATION_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:12)
- `ALERT_FAILURE_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:13)
- `ALERT_MATERIAL_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:14)
- `ALERT_MATERIALTYPE_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:15)
- `ALERT_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:16)
- `ALERT_SUPPLIER_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:17)
- `ALERT_TEAM_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:18)
- `ALERT_UDA_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:19)
- `ALERT_USER_EVENT_CREATED` (Solvace.Alert/src/Solvace.Alert.API/Extensions/TopicSnsBuilderExtensions.cs:20)
