## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila POST_USER_EVENT_CREATED) (revamp-Post/Solvace.Post/src/Lambda.Post.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Post/Solvace.Post/src/Solvace.Post.Application/UseCases/SendNotificationComplaint/SendNotificationComplaintCommandHandler.cs:18)
- `revamp-hashtag` — envia para a fila HASHTAG_WORKER_<amb> (revamp-Post/Solvace.Post/src/Solvace.Post.Application/UseCases/SendPostSqsMessageGeneric/SendPostSqsMessageGenericCommandHandler.cs:141)

**Banco compartilhado**
- `revamp-kaizen` — usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS) (revamp-Post/Solvace.Post/src/Solvace.Post.Infra.Data.Global.SqlServer/Helpers/RegisterAsPost/RegisterAsPostQueries.cs:242)
- `revamp-lpp` — usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT) (revamp-Post/Solvace.Post/src/Solvace.Post.Infra.Data.Global.SqlServer/Helpers/RegisterAsPost/RegisterAsPostQueries.cs:134)
- `revamp-rca` — usa tabelas TB_SA3_* (ex.: TB_SA3_A3) (revamp-Post/Solvace.Post/src/Solvace.Post.Infra.Data.Global.SqlServer/Helpers/RegisterAsPost/RegisterAsPostQueries.cs:54)
- `revamp-masterdata` — usa tabelas TB_MST_* (ex.: TB_MST_CATEGORIA_CUSTO) (revamp-Post/Solvace.Post/src/Solvace.Post.Infra.Data.Global.SqlServer/Helpers/RegisterAsPost/RegisterAsPostQueries.cs:241)
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-Post/Solvace.Post/src/Solvace.Post.Infra.Data.Global.SqlServer/Helpers/Bookmark/Queries/UserUidQueries.cs:52)


## Usado por

- `revamp-lpp` — Fila (SQS): envia para a fila POST_WORKER_<amb>
- `revamp-rca` — Fila (SQS): envia para a fila POST_WORKER_<amb>
- `revamp-reaction` — Fila (SQS): envia para a fila POST_INTERACTION_UPDATED_<amb>
- `revamp-whywhy` — Fila (SQS): envia para a fila POST_WORKER_<amb>
- `revamp-comment` — Fila (SQS): envia para a fila POST_INTERACTION_UPDATED_<amb>
- `revamp-whiteboard` — Fila (SQS): envia para a fila POST_WORKER_<amb>

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.Post/src/Solvace.Post.Application/UseCases/SendNotificationComplaint/SendNotificationComplaintCommandHandler.cs:18)
- `HASHTAG_WORKER` (Solvace.Post/src/Solvace.Post.Application/UseCases/SendPostSqsMessageGeneric/SendPostSqsMessageGenericCommandHandler.cs:141)
- `POST_INTERACTION_UPDATED_LOCAL` (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/Worker.cs:19)
- `POST_INTERACTION_UPDATED` (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/Worker.cs:21)
- `POST_WORKER_LOCAL` (Solvace.Post/src/Solvace.Post.Queue.Worker/Worker.cs:22)
- `POST_WORKER` (Solvace.Post/src/Solvace.Post.Queue.Worker/Worker.cs:24)
- `DB_SOLV_DEV_GLOBAL` (Solvace.Post/src/Solvace.Post.API/Program.cs:77)
- `ASPNETCORE_ENVIRONMENT` (Solvace.Post/src/Solvace.Post.API/Extensions/TopicSnsBuilderExtensions.cs:9)
- `POST_USER_EVENT_CREATED` (Solvace.Post/src/Solvace.Post.API/Extensions/TopicSnsBuilderExtensions.cs:13)

## URLs de configuração
- `UrlNotification` = https://api-post-rc.solvacelabs.com/Notification/Send (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/appsettings.HotfixVersion.json)
- `UrlNotification` = https://api-post-rc.solvacelabs.com/Notification/Send (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/appsettings.ReleaseVersion.json)
- `UrlNotification` = https://api-post-prod.solvacelabs.com/Notification/Send (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/appsettings.json)
- `UrlNotification` = https://api-post-dev.solvacelabs.com/Notification/Send (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/appsettings.Development.json)
- `UrlNotification` = https://api-post-prod.solvacelabs.com/Notification/Send (Solvace.Post/src/Solvace.Post.Queue.Worker.Interactions/appsettings.Edge.json)
- `UrlNotification` = https://api-post-rc.solvacelabs.com/Notification/Broadcast (Solvace.Post/src/Solvace.Post.Queue.Worker/appsettings.HotfixVersion.json)
- `UrlNotification` = https://api-post-rc.solvacelabs.com/Notification/Broadcast (Solvace.Post/src/Solvace.Post.Queue.Worker/appsettings.ReleaseVersion.json)
- `UrlNotification` = https://api-post-prod.solvacelabs.com/Notification/Broadcast (Solvace.Post/src/Solvace.Post.Queue.Worker/appsettings.json)
- `UrlNotification` = https://api-post-dev.solvacelabs.com/Notification/Broadcast (Solvace.Post/src/Solvace.Post.Queue.Worker/appsettings.Development.json)
- `UrlNotification` = https://api-post-prod.solvacelabs.com/Notification/Broadcast (Solvace.Post/src/Solvace.Post.Queue.Worker/appsettings.Edge.json)
