## Endpoints HTTP
- **ApplicationFeedController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/ApplicationFeedController.cs): `GET /`
- **ComplaintController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/ComplaintController.cs): `GET /` · `GET {complaintId}` · `POST /` · `DELETE {complaintId}` · `PUT {complaintId}/delete/post`
- **ComplaintTypeController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/ComplaintTypeController.cs): `GET /`
- **ListController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/ListController.cs): `GET /` · `GET ById`
- **MediaController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/MediaController.cs): `PUT /`
- **NotificationController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/NotificationController.cs): `POST Send` · `POST Broadcast`
- **PostController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/PostController.cs): `POST /` · `PUT /` · `DELETE /` · `GET {postId}` · `GET {postId}/isDeleted` · `GET deleted`
- **ShareController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/ShareController.cs): `PUT /` · `GET {postId}`
- **TranslateController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/TranslateController.cs): `GET /`
- **UserController** (`[controller]`, Solvace.Post/src/Solvace.Post.API/Controllers/UserController.cs): `PATCH block` · `GET management` · `GET eligibility`

## Casos de uso
- **Queries**: Builder, GetComplaintById, GetComplaintFitered, GetComplaintTypes, GetDeletedPostFiltered, GetPostIsDeleted, GetPostMediaUrlsSigned, GetPosts, GetTranslation, GetUserEligibility, GetUserManagement, GetUsersThatShared, InfoById
- **UseCases**: BasePost, BlockUser, CreateComplaint, CreatePost, DeleteComplaint, DeletePost, DeletePostWithComplaint, FromQueuePost, NotifyInteractionPost, SendNotificationComplaint, SendNotificationInteraction, SendNotificationPost, SendPostSqsMessageGeneric, SharePost, UpdatePost, UploadPostMediaUrls

## Tempo real (SignalR)
- `PostHub` (Solvace.Post/src/Solvace.Post.Application/Hubs/PostHub.cs)
