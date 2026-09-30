## Endpoints HTTP
- **ChecklistVersionsController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/ChecklistVersionsController.cs): `POST /` · `GET {id}` · `GET last`
- **RCAsController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/RCAsController.cs): `GET {id}` · `GET internal/{rootCauseAnalysisId}` · `GET /` · `GET card` · `GET pdf` · `GET excel` · `DELETE {id}` · `PATCH {id}/finalize` · `GET {id}/finalize` · `PATCH {id}/cancel` · `PATCH {id}/reopen` · `GET drilldown`
- **ReportController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/ReportController.cs): `GET GroupByYear` · `GET GroupByMonth` · `GET GroupByDay` · `GET GroupByStatus` · `GET GroupByStatusAndAge` · `GET GroupByType` · `GET GroupByArea` · `GET GroupByOriginator` · `GET GroupByTeam`
- **ScoreCardsController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/ScoreCardsController.cs): `GET rca/opened` · `GET rca/closed` · `GET rca/opened/items` · `GET rca/closed/items` · `GET rca-legacy/opened` · `GET rca-legacy/closed` · `GET rca-legacy/opened/items` · `GET rca-legacy/closed/items`
- **StepVersionsController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/StepVersionsController.cs): `POST /` · `GET {id}` · `GET last` · `POST initialize` · `GET DefaultSteps`
- **TemplateController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/TemplateController.cs): `POST /` · `PUT {templateUniqueId}` · `GET names` · `GET {templateUniqueId}` · `DELETE {templateUniqueId}`
- **TypesController** (`[controller]`, Solvace.RCA/src/Solvace.RCA.API/Controllers/TypesController.cs): `POST /` · `GET {id}` · `PUT {id}` · `DELETE {id}` · `GET /` · `GET pdf` · `GET excel`

## Casos de uso
- **Commands**: ChecklistVersions, RCAs, StepVersions, Template, Types
- **Queries**: Base, Builder, ChecklistVersions, Kpi, RCAs, StepVersions, Template, Types
