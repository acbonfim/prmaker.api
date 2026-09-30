## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila COMMUNICATION_USER_EVENT_CREATED) (revamp-Communication/Solvace.Communication/src/Lambda.Communication.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Communication/Solvace.Communication/src/Solvace.Communication.Application/UseCases/Communication/SendCommunication/SendCommunicationCommandHandler.cs:78)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.Communication/src/Solvace.Communication.Application/UseCases/Communication/SendCommunication/SendCommunicationCommandHandler.cs:78)
