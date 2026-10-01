## Depende de

**Fila (SQS)**
- `revamp-commentmanager` — envia para a fila COMMENT_MENTION_WORKER_<amb> (revamp-Comment/Solvace.Comment/src/Solvace.Comment.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:20)
- `revamp-commentmanager` — envia para a fila COMMENT_HEADER_WORKER_<amb> (revamp-Comment/Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:43)
- `revamp-post` — envia para a fila POST_INTERACTION_UPDATED_<amb> (revamp-Comment/Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:94)

**Banco compartilhado**
- `revamp-digitalobeya` — usa tabelas TB_DOB_* (ex.: TB_DOB_WIDGET) (revamp-Comment/Solvace.Comment/src/Solvace.Comment.Application/UseCases/SendNotification/RedirectConfigurationsServiceByConfigType.cs:25)

**Serviço externo**
- `ext:microsoft-graph` — Microsoft Graph / Teams: https://graph.microsoft.com/.default (TeamsConfig:Scope) (revamp-Comment/Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `ext:azure-ad` — Azure AD / Entra ID: https://login.microsoftonline.com/{0}/oauth2/v2.0/token (TeamsConfig:LoginEndpoint) (revamp-Comment/Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `ext:microsoft-graph` — Microsoft Graph / Teams: https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendactivitynotification (TeamsConfig:SendNotificationEndpoint) (revamp-Comment/Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `COMMENT_MENTION_WORKER_LOCAL` (Solvace.Comment/src/Solvace.Comment.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:20)
- `COMMENT_MENTION_WORKER` (Solvace.Comment/src/Solvace.Comment.Application/UseCases/SendNotification/SendNotificationCommandHandler.cs:22)
- `COMMENT_HEADER_WORKER_LOCAL` (Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:43)
- `COMMENT_HEADER_WORKER` (Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:45)
- `POST_INTERACTION_UPDATED_LOCAL` (Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:94)
- `POST_INTERACTION_UPDATED` (Solvace.Comment/src/Solvace.Comment.Application/Services/MessageBrokerService.cs:96)

## URLs de configuração
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.HotfixVersion.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.ReleaseVersion.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.ReleaseVersion.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.ReleaseVersion.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.ReleaseVersion.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Development.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Development.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Development.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Development.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Edge.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Edge.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Edge.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.Comment/src/Lambda.Comment.DeleteGroup/appsettings.Edge.json)
