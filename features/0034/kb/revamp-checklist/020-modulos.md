## Endpoints HTTP
- **AnalyticsController** (`[controller]`, Solvace.Checklist/src/Solvace.Checklist.API/Controllers/AnalyticsController.cs): `GET inspections/details` · `GET inspections/details/type` · `GET subordinates/inspections/details` · `GET subordinates/inspections/details/type`
- **ChecklistController** (`[controller]`, Solvace.Checklist/src/Solvace.Checklist.API/Controllers/ChecklistController.cs): `GET /simpleSearch`
- **CountersController** (`[controller]`, Solvace.Checklist/src/Solvace.Checklist.API/Controllers/CountersController.cs): `GET inspections` · `GET inspections/approvals` · `GET inspections/subordinates`
- **LegacyController** (`[controller]`, Solvace.Checklist/src/Solvace.Checklist.API/Controllers/LegacyController.cs): `POST {id}/events/upsert` · `POST {id}/events/delete`

## Casos de uso
- **Commands**: Events
- **Queries**: ChecklistStatusAnalyticsBySubordinates, ChecklistStatusAnalyticsByUserId, ChecklistStatusAnalyticsByUserIdTypeDetail, ChecklistStatusCountersByUserId, GetInspectionApprovalsCounter, SimpleSearch, SubordinatesCounters
