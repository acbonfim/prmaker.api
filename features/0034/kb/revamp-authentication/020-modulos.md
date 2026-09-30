## Endpoints HTTP
- **AccessLogController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/AccessLogController.cs): `POST /`
- **CookieController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/CookieController.cs): `GET cloudfront`
- **EnvironmentController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/EnvironmentController.cs): `POST info` · `POST username` · `GET sso-reconciliation-flag`
- **LanguagesController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/LanguagesController.cs): `GET /` · `GET authenticated` · `GET terms/{languageId}` · `GET terms/authenticated` · `GET terms/v2/{languageId}` · `GET terms/authenticated/v2`
- **LoginController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/LoginController.cs): `POST sub` · `POST service-account`
- **LoginController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.Integration.API/Controllers/LoginController.cs): `POST validate`
- **McpOAuthController** (`oauth`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/McpOAuthController.cs): `GET /.well-known/oauth-authorization-server` · `POST register` · `GET authorize` · `GET callback` · `POST complete` · `POST token` · `POST revoke`
- **NewsletterController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/NewsletterController.cs): `GET /` · `GET CurrentEnvironment` · `GET download` · `GET download/{id}` · `PUT {newsletterVersionId}` · `GET CurrentEnvironmentList` · `GET CurrentEnvironmentUnreadCount` · `GET ById`
- **ParameterController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/ParameterController.cs): `GET {paramKey}`
- **RolesController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/RolesController.cs): `GET {applicationId}` · `GET User/{applicationId}`
- **SiteController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/SiteController.cs): `GET /` · `GET ByEnvironment/opened` · `GET ByEnvironment/authenticated`
- **TokenController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/TokenController.cs): `GET refresh`
- **UserPreferenceController** (`[controller]`, Solvace.Authentication/src/Solvace.Authentication.API/Controllers/UserPreferenceController.cs): `PUT change-language` · `PUT change-site`

## Casos de uso
- **Queries**: EnvironmentInfo, EnvironmentInfoByUserName, GetAvailabilityFromModule, GetCloudFrontSignedCookies, GetListVersionUnreadCounterQuery, GetNewsletter, GetNewsletterById, GetNewsletterFile, GetNewsletterFileFromId, GetNewsletterFromEnvironmentUserList, GetNewsletterUser, GetParameterValueByKey, GetRoles, GetSitesFromEnvironment, GetUserRoles, LanguageList, LanguageTerms, LanguageTermsV2, McpServiceAccount, NewsletterTasks, SitesByUserId, SsoReconciliationFlag
- **UseCases**: ChangeLanguageUserPreference, ChangeLastSiteIdUser, FactoryAccessToken, GetOrTranslateNewsletter, LoginExternal, LoginInternal, LoginServiceAccount, Mcp, ReadNewsletter, RefreshAccessToken, TranslateContent, TranslateNewsletter, UserAccessLog

## Tempo real (SignalR)
- `ChangeSiteHub` (Solvace.Authentication/src/Solvace.Authentication.Application/Hubs/ChangeSite.cs)
