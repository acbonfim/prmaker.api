## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila UNC_USER_EVENT_CREATED) (revamp-UnsafeCondition/Solvace.UnsafeCondition/src/Lambda.UNC.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-UnsafeCondition/Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.Application/Shared/UnsafeConditionNotificationHelper.cs:31)

**Banco compartilhado**
- `revamp-masterdata` — usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO_FOTO) (revamp-UnsafeCondition/Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.Infra.Data.Global.SqlServer/Queries/EquipmentPhotoQuery.cs:26)


## Usado por

- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_UNC_* (ex.: TB_UNC_UNSAFE_CONDITION)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_UNC_* (ex.: TB_UNC_UNSAFE_CONDITION)

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.Application/Shared/UnsafeConditionNotificationHelper.cs:31)
- `ASPNETCORE_ENVIRONMENT` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.API/Extensions/TopicSnsBuilderExtensions.cs:43)
- `UNC_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.API/Extensions/TopicSnsBuilderExtensions.cs:47)
- `UNC_TEAM_EVENT_CREATED` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.API/Extensions/TopicSnsBuilderExtensions.cs:48)
- `UNC_USER_EVENT_CREATED` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.API/Extensions/TopicSnsBuilderExtensions.cs:49)
- `UNC_EQUIPAMENT_EVENT_CREATED` (Solvace.UnsafeCondition/src/Solvace.UnsafeCondition.API/Extensions/TopicSnsBuilderExtensions.cs:50)
