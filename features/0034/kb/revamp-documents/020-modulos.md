## Endpoints HTTP
- **DocumentController** (`[controller]`, Solvace.Documents/src/Solvace.Documents.API/Controllers/DocumentController.cs): `POST CreateNewEmptyFile` · `POST CreateNewEmptyFileFromUpload` · `POST CreateNewEmptyFileFromUploadId` · `GET ListTemplates` · `GET ListFiles` · `GET LoadFileFromId` · `POST Callback` · `PUT {documentUniqueId}/reference/{referenceId}`
- **WebEditorController** (`[controller]`, Solvace.Documents/src/Solvace.Documents.API/Controllers/WebEditorController.cs): `POST callback`

## Casos de uso
- **Commands**: CreateNewEmptyFile, CreateNewEmptyFileFromUpload, CreateNewEmptyFileFromUploadId, UpdateReferenceId, WebEditorCallBack, WebEditorCallBackRevamp
- **Queries**: GetDocumentVersion, ListDocumentsTemplates, ListDocumentsVersions
