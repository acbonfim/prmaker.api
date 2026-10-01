## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila PRAISE_USER_EVENT_CREATED) (revamp-Praise/Solvace.Praise/src/Lambda.Praise.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Praise/Solvace.Praise/src/Solvace.Praise.Application/UseCases/Praises/SendPraise/SendPraiseCommandHandler.cs:41)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.Praise/src/Solvace.Praise.Application/UseCases/Praises/SendPraise/SendPraiseCommandHandler.cs:41)
