## Endpoints HTTP
- **CommentAnswerController** (`Comment/Answer`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentAnswerController.cs): `GET {commentParentId}` · `POST {commentParentId}`
- **CommentByReferenceIdController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentByReferenceIdController.cs): `GET /` · `GET external` · `GET counter` · `GET external/counter` · `POST /`
- **CommentByReferenceIdsController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentByReferenceIdsController.cs): `POST counter` · `POST external/counter`
- **CommentByReferenceUIdController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentByReferenceUIdController.cs): `GET /` · `GET external` · `GET counter` · `GET external/counter` · `POST /`
- **CommentByReferenceUIdsController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentByReferenceUIdsController.cs): `POST counter` · `POST external/counter`
- **CommentController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/CommentController.cs): `DELETE {commentId}` · `DELETE /` · `GET {commentId}` · `GET ByIds` · `PATCH {commentId}/attachment` · `GET mentionsuggestion`
- **TranslateController** (`[controller]`, Solvace.Comment/src/Solvace.Comment.API/Controllers/TranslateController.cs): `GET /`

## Casos de uso
- **Queries**: GetCommentCounters, GetComments, GetMentionSuggestion, GetTranslation
- **UseCases**: CreateComment, DeleteComment, DeleteCommentGroup, DeleteListComents, InsertAttachments, SendNotification
