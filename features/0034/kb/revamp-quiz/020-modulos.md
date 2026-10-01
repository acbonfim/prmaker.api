## Endpoints HTTP
- **ApplicationFeedController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/ApplicationFeedController.cs): `GET /`
- **QuizController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/QuizController.cs): `GET ById` · `GET Training/ById` · `GET Custom/ById` · `POST Training` · `POST Custom` · `PUT Training` · `PUT Custom` · `PUT Commit` · `GET Custom/Question/ById` · `PUT Custom/Question` · `DELETE Custom/Question/ById` · `GET CardList` · `GET Download/Excel` · `GET Download/Pdf` … (+3)
- **QuizUserSessionController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/QuizUserSessionController.cs): `POST Training` · `POST Custom` · `GET Training/Question` · `GET Custom/Question` · `PATCH Training/Answer` · `PATCH Custom/Answer`
- **ReportController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/ReportController.cs): `GET Ranking` · `GET Participation` · `GET ParticipationAnalytic`
- **TrainingController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/TrainingController.cs): `GET /Training/Questions`
- **TypeController** (`[controller]`, Solvace.Quiz/src/Solvace.Quiz.API/Controllers/TypeController.cs): `GET Paging` · `GET ById` · `GET Download/Excel` · `GET Download/Pdf`

## Casos de uso
- **Queries**: CardListQuiz, GetDistributionListByQuizId, GetExcelType, GetParticipationReport, GetParticipationReportAnalytic, GetPdfType, GetQuizListExportExcel, GetQuizListExportPdf, GetRanking, Helpers, ListQuiz, ListRetry, ListStatus, ListTraining, ListTrainingQuestions, ListType, ListTypePaging, QuizCustomById, QuizCustomQuestionById, QuizTrainingById, TimePlayMinutes, TypeById
- **UseCases**: CommitQuiz, CreateCustomQuiz, CreateCustomQuizUserSession, CreateTrainingQuiz, CreateTrainingQuizUserSession, CreateType, DeleteCustomQuestionById, DeleteQuiz, DeleteType, ExpireQuizzes, GetCustomQuestion, GetTrainingQuestion, SendDistributionList, Shared, UpdateCustomQuiz, UpdateDistributionList, UpdateQuestionCustom, UpdateTrainingQuiz, UpdateType, UpsertCustomQuizUserSessionAnswer, UpsertTrainingQuizUserSessionAnswer
