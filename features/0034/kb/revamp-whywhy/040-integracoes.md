## Depende de

**Evento (SNS → fila)**
- `revamp-rca` — consome o tópico RCA (fila WHYWHY_RCA_EVENT_CREATED) (revamp-WhyWhy/Solvace.WhyWhy/src/Lambda.WhyWhy.Schema.RCA/aws-lambda-tools-defaults.json)
- `revamp-users` — consome o tópico USER (fila WHYWHY_USER_EVENT_CREATED) (revamp-WhyWhy/Solvace.WhyWhy/src/Lambda.WhyWhy.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-post` — envia para a fila POST_WORKER_<amb> (revamp-WhyWhy/Solvace.WhyWhy/src/Solvace.WhyWhy.Application/Service/SendRootCauseAsPostService.cs:78)


## Usado por

- `revamp-fishbone` — Fila (SQS): envia para a fila WHYWHY_FISHBONE_EVENT_CREATED_<amb>

## Filas/tópicos citados no código
- `POST_WORKER_LOCAL` (Solvace.WhyWhy/src/Solvace.WhyWhy.Application/Service/SendRootCauseAsPostService.cs:78)
- `ASPNETCORE_ENVIRONMENT` (Solvace.WhyWhy/src/Solvace.WhyWhy.Application/Service/SendRootCauseAsPostService.cs:80)
- `POST_WORKER` (Solvace.WhyWhy/src/Solvace.WhyWhy.Application/Service/SendRootCauseAsPostService.cs:81)
- `DB_SOLV_DEV_GLOBAL` (Solvace.WhyWhy/src/Solvace.WhyWhy.API/Program.cs:107)
- `WHYWHY_USER_EVENT_CREATED` (Solvace.WhyWhy/src/Solvace.WhyWhy.API/Extensions/TopicSnsBuilderExtensions.cs:48)
- `WHYWHY_TEAM_EVENT_CREATED` (Solvace.WhyWhy/src/Solvace.WhyWhy.API/Extensions/TopicSnsBuilderExtensions.cs:49)
- `WHYWHY_UDA_EVENT_CREATED` (Solvace.WhyWhy/src/Solvace.WhyWhy.API/Extensions/TopicSnsBuilderExtensions.cs:50)
- `WHYWHY_EQUIPAMENT_EVENT_CREATED` (Solvace.WhyWhy/src/Solvace.WhyWhy.API/Extensions/TopicSnsBuilderExtensions.cs:51)
