## Endpoints HTTP
- **CenterlineController** (`[controller]`, Solvace.Centerline/src/Solvace.Centerline.API/Controllers/CenterlineController.cs): `GET /simpleSearch`
- **CounterController** (`[controller]`, Solvace.Centerline/src/Solvace.Centerline.API/Controllers/CounterController.cs): `GET inspection/approvals`
- **LegacyController** (`[controller]`, Solvace.Centerline/src/Solvace.Centerline.API/Controllers/LegacyController.cs): `POST {id}/events/upsert` · `POST {id}/events/delete`

## Casos de uso
- **Commands**: Events
- **Queries**: GetInspectionApprovalsCounter, SimpleSearch
