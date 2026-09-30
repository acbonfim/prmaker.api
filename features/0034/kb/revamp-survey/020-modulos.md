## Endpoints HTTP
- **AnswerController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/AnswerController.cs): `GET employees` · `GET texts` · `GET multiple-texts`
- **AttachmentController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/AttachmentController.cs): `GET {referenceKey}/Question/{questionId}`
- **QuestionTemplateController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/QuestionTemplateController.cs): `GET /` · `GET informations` · `POST /` · `PUT {questionTypeId}` · `DELETE {questionTypeId}`
- **SurveyController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/SurveyController.cs): `GET /` · `GET {surveyId}` · `GET {surveyId}/DistributionList` · `GET download/pdf` · `GET download/excel` · `POST /` · `PUT {surveyId}` · `DELETE {surveyId}` · `GET {surveyId}/user/eligibility` · `PATCH {surveyId}/send` · `PATCH {surveyId}/resend` · `PATCH {surveyId}/distributionList` · `GET {surveyId}/questions` · `PATCH {surveyId}/reschedule` … (+7)
- **TopicController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/TopicController.cs): `PUT {surveyId}`
- **TypeController** (`[controller]`, Solvace.Survey/src/Solvace.Survey.API/Controllers/TypeController.cs): `GET /` · `GET names` · `POST /` · `DELETE typeId` · `PUT {typeId}` · `GET download/excel` · `GET download/pdf`

## Casos de uso
- **Queries**: Builder, GetAllTypes, GetAttachmentByReferenceKey, GetCountDistributionListGroupByLevel, GetDistributionListBySurveyId, GetEmployeeListAnswerPaginated, GetExcelSurvey, GetExcelType, GetMultipleTextFieldAnswerPaginated, GetPdfSurvey, GetPdfType, GetQuestionTemplateFiltered, GetQuestionTemplateInfo, GetSurveyAnswersQuestionByTopic, GetSurveyById, GetSurveyFiltered, GetSurveyQuestions, GetSurveyTopics, GetSurveyUserEligibility, GetTextFieldAnswerPaginated, GetTypeNames, GetUsersRecipientByArea, GetUsersRecipientByTeam, Validations
- **UseCases**: QuestionTemplate, Survey, Topic, Types
