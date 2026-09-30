## Endpoints HTTP
- **RoomController** (`[controller]`, Solvace.DigitalObeya/src/Solvace.DigitalObeya.API/Controllers/RoomController.cs): `GET /` · `GET download/excel` · `GET download/pdf`
- **SolvaceKaiController** (`[controller]`, Solvace.DigitalObeya/src/Solvace.DigitalObeya.API/Controllers/SolvaceKaiController.cs): `GET WidgetZoomList/{roomId}`
- **StatusController** (`[controller]`, Solvace.DigitalObeya/src/Solvace.DigitalObeya.API/Controllers/StatusController.cs): `GET names`
- **TypeController** (`[controller]`, Solvace.DigitalObeya/src/Solvace.DigitalObeya.API/Controllers/TypeController.cs): `GET names`
- **WidgetController** (`[controller]`, Solvace.DigitalObeya/src/Solvace.DigitalObeya.API/Controllers/WidgetController.cs): `POST notify-update`

## Casos de uso
- **Commands**: NotifyWidgetUpdate
- **Queries**: Builder, GetExcelRoom, GetPdfRoom, GetRoomDetails, GetStatusNames, GetTypeNames, GetWidgetZoomList
- **UseCases**: WidgetKeepAlive

## Tempo real (SignalR)
- `WidgetCalcHub` (Solvace.DigitalObeya/src/Solvace.DigitalObeya.Application/Hubs/WidgetCalcHub.cs)
- `DobSurveyHub` (Solvace.DigitalObeya/src/Solvace.DigitalObeya.Application/Hubs/DobSurveyHub.cs)
- `WidgetTableHub` (Solvace.DigitalObeya/src/Solvace.DigitalObeya.Application/Hubs/WidgetTableHub.cs)
- `NotifyWidgetsHub` (Solvace.DigitalObeya/src/Solvace.DigitalObeya.Application/Hubs/NotifyWidgetsHub.cs)
