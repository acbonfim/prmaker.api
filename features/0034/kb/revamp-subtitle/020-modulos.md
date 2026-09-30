## Endpoints HTTP
- **SubtitleByReferenceIdController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/SubtitleByReferenceIdController.cs): `GET /` · `DELETE /`
- **SubtitleByReferenceUIdController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/SubtitleByReferenceUIdController.cs): `GET /` · `DELETE /`
- **SubtitlesController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/SubtitlesController.cs): `GET /` · `DELETE /`
- **TranscriptionByReferenceIdController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/TranscriptionByReferenceIdController.cs): `POST /` · `GET status`
- **TranscriptionByReferenceUIdController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/TranscriptionByReferenceUIdController.cs): `POST /` · `GET status`
- **TranscriptionsController** (`[controller]`, Solvace.Subtitle/src/Solvace.Subtitle.API/Controllers/TranscriptionsController.cs): `POST /` · `GET status`

## Casos de uso
- **Commands**: CreateSubtitle, DeleteSubtitle, MessageBroker, TranscriptionOrder
- **Queries**: GetSubtitle
