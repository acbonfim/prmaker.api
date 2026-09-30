## Endpoints HTTP
- **AlertController** (`[controller]`, Solvace.Alert/src/Solvace.Alert.API/Controllers/AlertController.cs): `GET Card` · `GET Drilldown` · `GET ByComment` · `GET ById` · `GET ByUniqueId` · `GET Download/Excel` · `GET Download/Pdf`
- **AlertReportController** (`[controller]`, Solvace.Alert/src/Solvace.Alert.API/Controllers/AlertReportController.cs): `GET GroupByDepartment` · `GET GroupByArea` · `GET GroupByOriginator` · `GET GroupByTeam` · `GET GroupByMonth` · `GET GroupByYear` · `GET GroupByDay` · `GET GroupByLocation` · `GET GroupByFailure` · `GET GroupBySupplier` · `GET GroupByWordCloud` · `GET GroupByClassification` · `GET ActionPlansAlertIds`
- **ApplicationFeedController** (`[controller]`, Solvace.Alert/src/Solvace.Alert.API/Controllers/ApplicationFeedController.cs): `GET /`
- **AttachmentController** (`[controller]`, Solvace.Alert/src/Solvace.Alert.API/Controllers/AttachmentController.cs): `PUT /` · `DELETE /`
- **ScoreCardsController** (`[controller]`, Solvace.Alert/src/Solvace.Alert.API/Controllers/ScoreCardsController.cs): `GET alert/reported` · `GET alert/reported/items`

## Casos de uso
- **Queries**: ActionPlansAlertIds, AlertById, AlertByUniqueId, AlertGroupByArea, AlertGroupByClassification, AlertGroupByDay, AlertGroupByDeparment, AlertGroupByFailure, AlertGroupByLocation, AlertGroupByMonth, AlertGroupByOriginator, AlertGroupBySupplier, AlertGroupByTeam, AlertGroupByWordCloud, AlertGroupByYear, Builders, GetAlertListExportExcel, GetAlertListExportPdf, GetKpiAlertReported, GetKpiAlertReportedItem, Helpers, ListAlert, ListAlertByComment, ListAlertCard, ListAlertDrilldown
- **UseCases**: CreateAlert, DeleteAlert, RemoveAlertMedia, SendNotification, UpdateAlert, UploadAlertMedias
