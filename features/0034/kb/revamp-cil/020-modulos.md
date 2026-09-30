## Endpoints HTTP
- **AnalyticsController** (`[controller]`, Solvace.CIL/src/Solvace.CIL.API/Controllers/AnalyticsController.cs): `GET inspections/details` · `GET inspections/details/type` · `GET subordinates/inspections/details` · `GET subordinates/inspections/details/type`
- **CilController** (`[controller]`, Solvace.CIL/src/Solvace.CIL.API/Controllers/CILController.cs): `GET /simpleSearch`
- **CountersController** (`[controller]`, Solvace.CIL/src/Solvace.CIL.API/Controllers/CountersController.cs): `GET inspections` · `GET inspections/approvals` · `GET inspections/subordinates`
- **LegacyController** (`[controller]`, Solvace.CIL/src/Solvace.CIL.API/Controllers/LegacyController.cs): `POST {id}/events/upsert` · `POST {id}/events/delete`

## Casos de uso
- **Commands**: Events
- **Queries**: GetCilCounters, GetCilStatusAnalytics, GetCilStatusAnalyticsByTypeDetail, GetStatusSubordinatesAnalytics, GetStatusSubordinatesAnalyticsTypeDetail, GetTasksApprovalCounter, SimpleSearch, SubordinatesCounters
