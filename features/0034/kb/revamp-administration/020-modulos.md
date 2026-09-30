## Endpoints HTTP
- **AccessLogController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/AccessLogController.cs): `GET LogCode` · `GET AccessLog`
- **AttachmentController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/AttachmentController.cs): `POST temporary/{description}` · `GET ExtensionsAllowed/{description}`
- **BannerController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/BannerController.cs): `GET ById` · `GET Active` · `PUT UpdateStatus` · `GET Types` · `GET Environments` · `GET DismissModal`
- **ClientFuncionalityController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/ClientFuncionalityController.cs): `GET /` · `PUT /`
- **FloatingMenuController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/FloatingMenuController.cs): `GET workspace`
- **MenuGroupController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/MenuGroupController.cs): `GET /` · `GET module/{applicationId}` · `GET module/{applicationId}/available` · `GET module/{applicationId}/accessible`
- **ModulesController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/ModulesController.cs): `GET names`
- **ParameterController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/ParameterController.cs): `GET site/value/{paramKey}` · `GET currency` · `GET currencies` · `GET categories` · `GET countries` · `GET timezones` · `GET site` · `PUT site` · `GET global/value/{paramKey}` · `GET global/values` · `GET site/value/cached/{paramKey}` · `GET site/values/cached` · `GET global/value/cached/{paramKey}` · `GET global/values/cached`
- **SatisfactionSurveyController** (`[controller]`, Solvace.Administration/src/Solvace.Administration.API/Controllers/SatisfactionSurveyController.cs): `GET /` · `PUT {surveyId}`

## Casos de uso
- **Commands**: AddSatisfactionSurveyAnswer, UpdateSiteParameters
- **Queries**: Builder, CheckAccessibleModule, FloatingMenuByGroupId, GetAccessLog, GetAccessLogCode, GetAvailabilityFromModule, GetBannerActiveBanner, GetBannerById, GetCurrency, GetFileExtensions, GetModuleNames, GetModules, GetParameterValueByKey, GetParameterValueByKeyCached, GetParameterValueByKeys, GetParameterValueByKeysCached, GetParameterValueBySiteKeys, GetSatisfactionSurvey, GetSiteParameters, ListBanner, ListBannerEnvironments, ListBannerTypes, ListCategories, ListClientFuncionality, ListCountries, ListCurrencies, ListTimezones, MenuCategory, MenuGroup, Parameter, ParameterCached
- **UseCases**: CreateBanner, CreateTemporaryFile, DismissModal, UpdateBanner, UpdateBannerStatus, UpdateClientFuncionality, UpdateModuleAdmins

## Tempo real (SignalR)
- `BannerHub` (Solvace.Administration/src/Solvace.Administration.Application/Hubs/BannerHub.cs)
