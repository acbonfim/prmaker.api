## Endpoints HTTP
- **AnalyticsController** (`Analytics`, Solvace.LPP/src/Solvace.LPP.API/Controllers/AnalyticsController.cs): `GET LppGroupByDay` · `GET LppGroupByMonth` · `GET LppGroupByYear` · `GET LppGroupByPerson` · `GET LppGroupByTeam` · `GET LppGroupByArea` · `GET LppGroupByCategory` · `GET ByStatus` · `GET ByQualification` · `GET PendingApprovals`
- **ApplicationFeedController** (`[controller]`, Solvace.LPP/src/Solvace.LPP.API/Controllers/ApplicationFeedController.cs): `GET /`
- **ApprovalLevelController** (`ApprovalLevels`, Solvace.LPP/src/Solvace.LPP.API/Controllers/ApprovalLevelController.cs): `DELETE {approvalLevelId}` · `GET {approvalLevelId}` · `GET Dropdown` · `GET download/excel` · `GET download/pdf`
- **CategoryController** (`Categories`, Solvace.LPP/src/Solvace.LPP.API/Controllers/CategoryController.cs): `GET {categoryId}` · `GET download/excel` · `GET download/pdf` · `GET Dropdown`
- **CounterController** (`[controller]`, Solvace.LPP/src/Solvace.LPP.API/Controllers/CounterController.cs): `GET lpp/approvals`
- **DocumentsController** (`Documents`, Solvace.LPP/src/Solvace.LPP.API/Controllers/DocumentsController.cs): `GET {documentUniqueId}` · `POST /` · `PUT {documentUniqueId}` · `GET /` · `GET cards` · `GET drilldown` · `GET tree` · `GET tree/download/excel` · `GET download/excel` · `GET tree/download/pdf` · `GET download/pdf` · `GET {documentUniqueId}/confidentiality` · `PATCH {documentUniqueId}/confidentiality` · `GET {documentNumber}/{siteId}/revisions` … (+23)
- **LPPController** (`[controller]`, Solvace.LPP/src/Solvace.LPP.API/Controllers/LPPController.cs): `GET /simpleSearch`
- **LegacyController** (`[controller]`, Solvace.LPP/src/Solvace.LPP.API/Controllers/LegacyController.cs): `POST {id}/events/upsert` · `POST {id}/events/delete`
- **ModelController** (`Models`, Solvace.LPP/src/Solvace.LPP.API/Controllers/ModelController.cs): `POST /` · `PUT {modelUniqueId}` · `GET {modelUniqueId}` · `DELETE {modelUniqueId}` · `GET download/excel` · `GET download/pdf` · `GET Dropdown`
- **ParameterController** (`Parameters`, Solvace.LPP/src/Solvace.LPP.API/Controllers/ParameterController.cs): `PUT /` · `GET Site/{siteId}` · `GET qrcode`
- **ScoreCardsController** (`[controller]`, Solvace.LPP/src/Solvace.LPP.API/Controllers/ScoreCardsController.cs): `GET documents/created` · `GET documents/published` · `GET documents/published/items` · `GET documents/created/items`
- **StatusController** (`Status`, Solvace.LPP/src/Solvace.LPP.API/Controllers/StatusController.cs): `GET Dropdown`
- **TypesController** (`Types`, Solvace.LPP/src/Solvace.LPP.API/Controllers/TypesController.cs): `GET Dropdown`
- **UserController** (`Users`, Solvace.LPP/src/Solvace.LPP.API/Controllers/UserController.cs): `GET Site/{siteId}`

## Casos de uso
- **Queries**: Analytics, ApprovalLevel, Builder, Category, Documents, Helpers, Kpis, Model, Parameter, SimpleSearch, Status, Types, Users
- **UseCases**: ApprovalLevel, Category, Documents, Models, Parameter
