## Endpoints HTTP
- **DiagramController** (`[controller]`, Solvace.Fishbone/src/Solvace.Fishbone.API/Controllers/DiagramController.cs): `GET /` · `POST /` · `POST /Node` · `PUT /` · `PUT /Node` · `DELETE /Node`

## Casos de uso
- **Queries**: GetById
- **UseCases**: CreateDiagram, CreateDiagramNode, DeleteDiagramNode, Notification, UpdateDiagram, UpdateDiagramNode

## Tempo real (SignalR)
- `FishboneHub` (Solvace.Fishbone/src/Solvace.Fishbone.Application/Hubs/FishboneHub.cs)
