## Endpoints HTTP
- **ApplicationController** (`[controller]`, Solvace.CommentManager/src/Solvace.CommentManager.API/Controllers/ApplicationController.cs): `GET /`
- **ManagerController** (`[controller]`, Solvace.CommentManager/src/Solvace.CommentManager.API/Controllers/ManagerController.cs): `GET /` · `GET TotalNoRead` · `PUT {commentHeaderId}/MarkAsRead` · `GET {commentHeaderId}/registryId/{registryId}/userHasPermission` · `GET {commentHeaderId}/registryUId/{registryUId}/userHasPermission` · `POST {commentHeaderId}/MarkFeedUnRead` · `POST {commentHeaderId}/PinFeed`
- **NotificationController** (`[controller]`, Solvace.CommentManager/src/Solvace.CommentManager.API/Controllers/NotificationController.cs): `POST Send`

## Casos de uso
- **Queries**: GetApplications, GetFiltered, GetTotalRegisterNoRead, Helpers, UserHasPermissionToSeeComment
- **UseCases**: CommentHeaderDeleteCommand, MarkAsRead, MarkFeedUnRead, PinFeed, SendNotification, SendNotificationFromWorker, SendTeamsNotification, UpsertCommentHeader, UpsertWithDeleteCommentHeader
