## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila REACTION_USER_EVENT_CREATED) (revamp-Reaction/Solvace.Reaction/src/Lambda.Reaction.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-post` — envia para a fila POST_INTERACTION_UPDATED_<amb> (revamp-Reaction/Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:24)
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Reaction/Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:60)

**Banco compartilhado**
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-Reaction/Solvace.Reaction/src/Solvace.Reaction.Infra.Data.Global.SqlServer/Queries/UserHelperQuerie.cs:45)

**Serviço externo**
- `ext:s3` — AWS S3: https://solvacelabs-webcomponents.s3.amazonaws.com/ (S3BucketUrl) (revamp-Reaction/Solvace.Reaction/src/Solvace.Reaction.API/appsettings.json)
- `ext:s3` — AWS S3: https://solvacelabs-webcomponents.s3.amazonaws.com/icons/ (código) (revamp-Reaction/Solvace.Reaction/src/Solvace.Reaction.Infra.Data.Global.SqlServer/Migrations/20241209121109_UpdateSeed.cs:17)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `ASPNETCORE_ENVIRONMENT` (Solvace.Reaction/src/Solvace.Reaction.API/Extensions/TopicSnsBuilderExtensions.cs:40)
- `REACTION_USER_EVENT_CREATED` (Solvace.Reaction/src/Solvace.Reaction.API/Extensions/TopicSnsBuilderExtensions.cs:44)
- `POST_INTERACTION_UPDATED_LOCAL` (Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:24)
- `POST_INTERACTION_UPDATED` (Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:26)
- `NOTIFICATION_WORKER_LOCAL` (Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:60)
- `NOTIFICATION_WORKER` (Solvace.Reaction/src/Solvace.Reaction.Application/Services/MessageBrokerService.cs:62)

## URLs de configuração
- `S3BucketUrl` = https://solvacelabs-webcomponents.s3.amazonaws.com/ (Solvace.Reaction/src/Solvace.Reaction.API/appsettings.json)
- `S3BucketUrl` = https://solvacelabs-webcomponents.s3.amazonaws.com/ (Solvace.Reaction/src/Solvace.Reaction.API/appsettings.Edge.json)
