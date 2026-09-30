## Depende de

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Users/Solvace.Users/src/Solvace.Users.Application/Commands/CreateNotification/CreateNotificationCommandHandler.cs:55)


## Usado por

- `revamp-actionplan` — Evento (SNS → fila): consome o tópico USER (fila ACP_USER_EVENT_CREATED)
- `revamp-alert` — Evento (SNS → fila): consome o tópico USER (fila ALERT_USER_EVENT_CREATED)
- `revamp-alert` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-bos` — Evento (SNS → fila): consome o tópico USER (fila BOS_USER_EVENT_CREATED)
- `revamp-bos` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-communication` — Evento (SNS → fila): consome o tópico USER (fila COMMUNICATION_USER_EVENT_CREATED)
- `revamp-defecttag` — Evento (SNS → fila): consome o tópico USER (fila DFT_USER_EVENT_CREATED)
- `revamp-fishbone` — Evento (SNS → fila): consome o tópico USER (fila FISHBONE_USER_EVENT_CREATED)
- `revamp-lpp` — Evento (SNS → fila): consome o tópico USER (fila LPP_USER_EVENT_CREATED)
- `revamp-lpp` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-post` — Evento (SNS → fila): consome o tópico USER (fila POST_USER_EVENT_CREATED)
- `revamp-post` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-praise` — Evento (SNS → fila): consome o tópico USER (fila PRAISE_USER_EVENT_CREATED)
- `revamp-quiz` — Evento (SNS → fila): consome o tópico USER (fila QUIZ_USER_EVENT_CREATED)
- `revamp-quiz` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-rca` — Evento (SNS → fila): consome o tópico USER (fila RCA_USER_EVENT_CREATED)
- `revamp-reaction` — Evento (SNS → fila): consome o tópico USER (fila REACTION_USER_EVENT_CREATED)
- `revamp-reaction` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-scorecard` — Evento (SNS → fila): consome o tópico USER (fila SCC_USER_EVENT_CREATED)
- `revamp-survey` — Evento (SNS → fila): consome o tópico USER (fila SURVEY_USER_EVENT_CREATED)
- `revamp-unsafecondition` — Evento (SNS → fila): consome o tópico USER (fila UNC_USER_EVENT_CREATED)
- `revamp-whywhy` — Evento (SNS → fila): consome o tópico USER (fila WHYWHY_USER_EVENT_CREATED)
- `revamp-masterdata` — Fila (SQS): envia para a fila USERS_UDA_EVENT_CREATED_<amb>
- `revamp-masterdata` — Fila (SQS): envia para a fila USERS_PHYSICAL_LAYOUT_EVENT_CREATED_<amb>
- `revamp-authentication` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_CARGO)
- `revamp-buildingblocks` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO)
- `revamp-training` — Banco compartilhado: usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_FUNCAO)

## Filas/tópicos citados no código
- `UPDATED_DATE` (Solvace.Users/src/Solvace.Users.Application.Abstractions/Queries/Entities/UserGuest.cs:23)
- `DB_SOLV_DEV_GLOBAL` (Solvace.Users/src/Solvace.Users.API/Program.cs:62)
- `ASPNETCORE_ENVIRONMENT` (Solvace.Users/src/Solvace.Users.Application/Extensions/TopicSnsBuilderExtensions.cs:30)
- `USERS_UDA_EVENT_CREATED` (Solvace.Users/src/Solvace.Users.Application/Extensions/TopicSnsBuilderExtensions.cs:36)
- `NOTIFICATION_WORKER` (Solvace.Users/src/Solvace.Users.Application/Commands/CreateNotification/CreateNotificationCommandHandler.cs:55)
