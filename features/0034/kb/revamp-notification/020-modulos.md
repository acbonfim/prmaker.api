## Endpoints HTTP
- **BroadcastController** (`[controller]`, Solvace.Notification/src/Solvace.Notification.API/Controllers/BroadcastController.cs): `PATCH Toast` · `PATCH Notification`
- **Configuration** (`[controller]`, Solvace.Notification/src/Solvace.Notification.API/Controllers/ConfigurationController.cs): `GET ByRule` · `GET ByApplication` · `GET ByRuleAndReference` · `PUT Default` · `PUT ByUserId` · `PUT ByReferenceId`
- **NotificationController** (`[controller]`, Solvace.Notification/src/Solvace.Notification.API/Controllers/NotificationController.cs): `GET /` · `PUT ReadNotificationsByUserId`

## Casos de uso
- **Queries**: GetConfigurationsByApplication, GetConfigurationsByRule, GetConfigurationsByRuleAndReference, GetNotificationEnvironmentContext, GetNotificationsByUserId, GetWorkerNotificationEnvironmentContext
- **UseCases**: BroadcastNotificationByNotificationId, BroadcastToast, SendNotificationByRuleId, SetAllNotificationReadByUserId, SetConfigurationActive, SetConfigurationActiveByReferenceId, SetConfigurationActiveByUserId

## Tempo real (SignalR)
- `ToastHub` (Solvace.Notification/src/Solvace.Notification.Application/Hubs/ToastHub.cs)
- `NotificationHub` (Solvace.Notification/src/Solvace.Notification.Application/Hubs/NotificationHub.cs)
