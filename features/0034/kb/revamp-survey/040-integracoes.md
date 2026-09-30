## Depende de

**Evento (SNS → fila)**
- `revamp-users` — consome o tópico USER (fila SURVEY_USER_EVENT_CREATED) (revamp-Survey/Solvace.Survey/src/Lambda.Survey.Schema.User/aws-lambda-tools-defaults.json)

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Survey/Solvace.Survey/src/Solvace.Survey.Application/UseCases/Survey/SendSurvey/SendSurveyCommandHandler.cs:100)


## Usado por

- `revamp-administration` — Banco compartilhado: usa tabelas TB_SURVEY_* (ex.: TB_SURVEY_USER_RESPONSE)

## Filas/tópicos citados no código
- `BUCKET_S3` (Solvace.Survey/src/Solvace.Survey.Application/UseCases/Topic/UpsertTopic/UpsertTopicCommandHandler.cs:28)
- `NOTIFICATION_WORKER` (Solvace.Survey/src/Solvace.Survey.Application/UseCases/Survey/SendSurvey/SendSurveyCommandHandler.cs:100)
