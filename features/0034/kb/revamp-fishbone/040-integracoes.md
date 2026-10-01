## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila FISHBONE_USER_EVENT_CREATED) (revamp-Fishbone/Solvace.Fishbone/src/Lambda.Fishbone.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-whywhy` — envia para a fila WHYWHY_FISHBONE_EVENT_CREATED_<amb> (revamp-Fishbone/Solvace.Fishbone/src/Solvace.Fishbone.Application/UseCases/Notification/DiagramNotificationCommandHandler.cs:53)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `WHYWHY_FISHBONE_EVENT_CREATED_LOCAL` (Solvace.Fishbone/src/Solvace.Fishbone.Application/UseCases/Notification/DiagramNotificationCommandHandler.cs:53)
- `ASPNETCORE_ENVIRONMENT` (Solvace.Fishbone/src/Solvace.Fishbone.Application/UseCases/Notification/DiagramNotificationCommandHandler.cs:56)
- `WHYWHY_FISHBONE_EVENT_CREATED` (Solvace.Fishbone/src/Solvace.Fishbone.Application/UseCases/Notification/DiagramNotificationCommandHandler.cs:59)

## ⚠️ Atenção
- Lambda `lambda_bos_user_create_event` declarada em revamp-bos, revamp-fishbone — um deploy sobrescreve o outro
