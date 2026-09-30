## Endpoints HTTP
- **CommunicationController** (`[controller]`, Solvace.Communication/src/Solvace.Communication.API/Controllers/CommunicationController.cs): `GET /` · `GET received` · `GET {communicationId}` · `GET {communicationId}/user/eligibility` · `GET {communicationId}/preview` · `GET {communicationId}/distributionList` · `GET {communicationId}/distributionList/groupByLevel` · `POST /` · `PATCH {communicationId}/send` · `PUT {communicationId}` · `PATCH {communicationId}/read` · `PATCH {communicationId}/attachment` · `PATCH {communicationId}/distributionList` · `DELETE {communicationId}` … (+7)
- **TypeController** (`[controller]`, Solvace.Communication/src/Solvace.Communication.API/Controllers/TypeController.cs): `GET /` · `GET names` · `POST /` · `DELETE typeId` · `PUT {typeId}` · `GET download/excel` · `GET download/pdf`

## Casos de uso
- **Queries**: Builder, GetAllTypes, GetCommunicationById, GetCommunicationPreview, GetCommunicationUserEligibility, GetCommunicationsFiltered, GetCommunicationsLoggedUserFiltered, GetCountDistributionListGroupByLevel, GetDistributionListByCommunicationId, GetExcelCommunication, GetExcelType, GetHighlightedCommunicationIds, GetPdfCommunication, GetPdfType, GetTypeNames, GetUnreadCounterByUserId, GetUsersRecipientByArea, GetUsersRecipientByTeam
- **UseCases**: Communication, Types
