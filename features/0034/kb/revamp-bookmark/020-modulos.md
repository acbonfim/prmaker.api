## Endpoints HTTP
- **ListIdController** (`[controller]`, Solvace.Bookmark/src/Solvace.Bookmark.API/Controllers/ListIdController.cs): `GET /` · `GET ByIds`
- **ListUIdController** (`[controller]`, Solvace.Bookmark/src/Solvace.Bookmark.API/Controllers/ListUIdController.cs): `GET /` · `GET ByUIds`
- **RegistryIdController** (`[controller]`, Solvace.Bookmark/src/Solvace.Bookmark.API/Controllers/RegistryIdController.cs): `POST /` · `DELETE /`
- **RegistryUidController** (`[controller]`, Solvace.Bookmark/src/Solvace.Bookmark.API/Controllers/RegistryUidController.cs): `POST /` · `DELETE /`

## Casos de uso
- **Queries**: GetListRegistryBookmarkIdQuery, GetListRegistryBookmarkUIdQuery, GetListRegistryByIdsIdQuery, GetListRegistryByUIdsUIdQuery
- **UseCases**: CreateRegistryIdCommand, CreateRegistryUidCommand, DeleteRegistryIdCommand, DeleteRegistryUidCommand
