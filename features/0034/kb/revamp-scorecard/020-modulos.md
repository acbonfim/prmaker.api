## Endpoints HTTP
- **KpiController** (`[controller]`, Solvace.Scorecard/src/Solvace.Scorecard.API/Controllers/KpiController.cs): `GET vision` · `GET vision/board`
- **VisionController** (`[controller]`, Solvace.Scorecard/src/Solvace.Scorecard.API/Controllers/VisionController.cs): `GET {visionUniqueId}` · `POST /` · `PUT /` · `DELETE {visionUniqueId}`

## Casos de uso
- **Queries**: KeyPerformanceByVisionId, KeyPerformanceByVisionIdBoard, KeyPerformanceIndicator, Vision
- **UseCases**: CreateVision, DeleteVision, UpdateVision
