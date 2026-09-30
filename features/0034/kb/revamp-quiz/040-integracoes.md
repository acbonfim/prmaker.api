## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila QUIZ_USER_EVENT_CREATED) (revamp-Quiz/Solvace.Quiz/src/Lambda.Quiz.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Quiz/Solvace.Quiz/src/Solvace.Quiz.Application/UseCases/SendDistributionList/SendDistributionListCommandHandler.cs:132)

**Banco compartilhado**
- `revamp-training` — usa tabelas TB_TRN_* (ex.: TB_TRN_TRAINING) (revamp-Quiz/Solvace.Quiz/src/Solvace.Quiz.Infra.Data.Global.SqlServer/Helpers/Training/Queries/TrainingQueries.cs:28)
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_FUNCIONARIO_CARGO) (revamp-Quiz/Solvace.Quiz/src/Solvace.Quiz.Infra.Data.Global.SqlServer/Queries/Helpers/UserHelperQueries.cs:39)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `NOTIFICATION_WORKER` (Solvace.Quiz/src/Solvace.Quiz.Application/UseCases/SendDistributionList/SendDistributionListCommandHandler.cs:132)
