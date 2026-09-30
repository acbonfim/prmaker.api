## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila DFT_USER_EVENT_CREATED) (revamp-DefectTag/Solvace.DefectTag/src/DFT.User.EventCreated/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_TYPE_<amb> (revamp-DefectTag/Solvace.DefectTag/src/Solvace.DefectTag.Application.Abstractions/Queries/Entities/Type.cs:20)
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-DefectTag/Solvace.DefectTag/src/Solvace.DefectTag.Application/Shared/DefectTagNotificationHelper.cs:31)

**Banco compartilhado**
- `revamp-masterdata` — usa tabelas TB_MNT_* (ex.: TB_MNT_EQUIPAMENTO) (revamp-DefectTag/Solvace.DefectTag/src/Solvace.DefectTag.Infra.Data.Global.SqlServer/Migrations/20260702120000_sync_reference_data.cs:274)


## Usado por

- `revamp-commentmanager` — Banco compartilhado: usa tabelas TB_DFT_* (ex.: TB_DFT_DEFECT_TAG)
- `revamp-subtitle` — Banco compartilhado: usa tabelas TB_DFT_* (ex.: TB_DFT_DEFECT_TAG)

## Filas/tópicos citados no código
- `DFT_PHYSICAL_LAYOUT_EVENT_CREATED` (Solvace.DefectTag/src/Solvace.DefectTag.API/Extensions/TopicSnsBuilderExtensions.cs:12)
- `DFT_TEAM_EVENT_CREATED` (Solvace.DefectTag/src/Solvace.DefectTag.API/Extensions/TopicSnsBuilderExtensions.cs:13)
- `DFT_USER_EVENT_CREATED` (Solvace.DefectTag/src/Solvace.DefectTag.API/Extensions/TopicSnsBuilderExtensions.cs:14)
- `DFT_EQUIPAMENT_EVENT_CREATED` (Solvace.DefectTag/src/Solvace.DefectTag.API/Extensions/TopicSnsBuilderExtensions.cs:15)
- `ASPNETCORE_ENVIRONMENT` (Solvace.DefectTag/src/Solvace.DefectTag.API/Extensions/TopicSnsBuilderExtensions.cs:20)
- `NOTIFICATION_TYPE` (Solvace.DefectTag/src/Solvace.DefectTag.Application.Abstractions/Queries/Entities/Type.cs:20)
- `NOTIFICATION_WORKER` (Solvace.DefectTag/src/Solvace.DefectTag.Application/Shared/DefectTagNotificationHelper.cs:31)
