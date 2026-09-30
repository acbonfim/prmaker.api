## Endpoints HTTP
- **ChatController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/ChatController.cs): `POST /` · `GET /` · `DELETE /`
- **FaqController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/FaqController.cs): `GET /`
- **FeedbackController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/FeedbackController.cs): `PUT new` · `GET categories`
- **MeetingNotesController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/MeetingNotesController.cs): `GET /` · `GET {meetingNotesId}` · `GET DatesByBoardId/{boardId}` · `DELETE {meetingNotesId}` · `PUT {meetingNotesId}`
- **RecordListController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/RecordListController.cs): `GET /`
- **RoutineController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/RoutineController.cs): `POST /` · `PUT {id}` · `DELETE {id}` · `GET /` · `GET History` · `GET Hydrated/{id}` · `GET {id}/Executions` · `PATCH {id}/read` · `PATCH {id}/unread` · `PATCH {id}/pin` · `DELETE {id}/Executions`
- **SolvaceAgentController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/SolvaceAgentController.cs): `GET /`
- **TemplateCreatorController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/TemplateCreatorController.cs): `GET /` · `GET {templateCreatorId}` · `POST /` · `PUT cancel/{templateCreatorId}`
- **UserAccessControlController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/UserAccessControlController.cs): `GET /`
- **UserAgentController** (`[controller]`, Solvace.Copilot/src/Solvace.Copilot.API/Controllers/UserAgentController.cs): `GET /` · `GET Available` · `GET {id}` · `POST /` · `PUT {id}` · `DELETE {id}`

## Casos de uso
- **Queries**: Timezone
