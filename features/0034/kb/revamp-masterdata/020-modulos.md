## Endpoints HTTP
- **ClassificationController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/ClassificationController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **ContractorController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/ContractorController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **DistributionController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/DistributionController.cs): `GET List` · `GET ById` · `POST /` · `PUT /` · `DELETE /` · `GET Download/Excel` · `GET Download/Pdf`
- **EquipmentsController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/EquipmentsController.cs): `GET location/{udaId}` · `GET locations` · `POST publish/event/upsert`
- **FailureController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/FailureController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **MaterialController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/MaterialController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **MaterialTypeController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/MaterialTypeController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **RequestDownloadController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/RequestDownloadController.cs): `GET List`
- **SupplierController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/SupplierController.cs): `GET List` · `POST publish/event/upsert` · `POST publish/event/delete`
- **TeamController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/TeamController.cs): `POST publish/event/upsert`
- **UdaController** (`[controller]`, Solvace.MasterData/src/Solvace.MasterData.API/Controllers/UDAController.cs): `GET department` · `GET area` · `GET area/site/{siteId}` · `GET location` · `POST publish/event/upsert` · `POST publish/event/delete`

## Casos de uso
- **Queries**: Builder, GetClassificationList, GetContractors, GetDistributionById, GetDistributionList, GetDistributionListExport, GetEquipmentByListLocation, GetEquipmentInformations, GetFailureList, GetMaterialList, GetMaterialTypeList, GetModuleDownloadFiles, GetSupplierList, GetUdaInformations, GetUdaInformationsBySite, Helpers
- **UseCases**: CreateDistributionList, DeleteDistributionList, PublishEventCreated, UpdateDistributionList
