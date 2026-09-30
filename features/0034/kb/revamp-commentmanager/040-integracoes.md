## Depende de

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.MentionWorker/UseCases/SendNotificationFromWorker/SendNotificationFromWorkerHandler.cs:40)

**Banco compartilhado**
- `revamp-digitalobeya` — usa tabelas TB_DOB_* (ex.: TB_DOB_WIDGET) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:45)
- `revamp-assessment` — usa tabelas TB_AST_* (ex.: TB_AST_INSPECTION_ITEM) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:143)
- `revamp-nonconformity` — usa tabelas TB_NCF_* (ex.: TB_NCF_NONCONFORMITY) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:33)
- `revamp-rca` — usa tabelas TB_SA3_* (ex.: TB_SA3_A3) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:131)
- `revamp-workpermit` — usa tabelas TB_WRK_* (ex.: TB_WRK_WORK_PERMIT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:14)
- `revamp-centerline` — usa tabelas TB_CLN_* (ex.: TB_CLN_INSPECTION) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:175)
- `revamp-checklist` — usa tabelas TB_CHK_* (ex.: TB_CHK_INSPECTION) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:158)
- `revamp-training` — usa tabelas TB_TRN_* (ex.: TB_TRN_STUDENT_TRAINING) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:293)
- `revamp-actionplan` — usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:13)
- `revamp-moc` — usa tabelas TB_MOC_* (ex.: TB_MOC_MOC) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsServiceByConfigType.cs:189)
- `revamp-cil` — usa tabelas TB_LIL_* (ex.: TB_LIL_NINSPECTION) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:240)
- `revamp-kaizen` — usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:63)
- `revamp-complaint` — usa tabelas TB_CMP_* (ex.: TB_CMP_COMPLAINT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:159)
- `revamp-lpp` — usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:121)
- `revamp-documentation` — usa tabelas TB_GED_* (ex.: TB_GED_DOCUMENT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:141)
- `revamp-defecttag` — usa tabelas TB_DFT_* (ex.: TB_DFT_DEFECT_TAG) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:23)
- `revamp-unsafecondition` — usa tabelas TB_UNC_* (ex.: TB_UNC_UNSAFE_CONDITION) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:53)
- `revamp-project` — usa tabelas TB_PJT_* (ex.: TB_PJT_PROJECT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:150)
- `revamp-incident` — usa tabelas TB_ICD_* (ex.: TB_ICD_INCIDENT) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Application.Abstractions/Services/RedirectConfigurationsService.cs:168)
- `revamp-masterdata` — usa tabelas TB_MST_* (ex.: TB_MST_FALHAS) (revamp-CommentManager/Solvace.CommentManager/src/Solvace.CommentManager.Infra.Data.Local.SqlServer/Queries/MasterdataQuery.cs:20)

**Serviço externo**
- `ext:microsoft-graph` — Microsoft Graph / Teams: https://graph.microsoft.com/.default (TeamsConfig:Scope) (revamp-CommentManager/Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `ext:azure-ad` — Azure AD / Entra ID: https://login.microsoftonline.com/{0}/oauth2/v2.0/token (TeamsConfig:LoginEndpoint) (revamp-CommentManager/Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `ext:microsoft-graph` — Microsoft Graph / Teams: https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendactivitynotification (TeamsConfig:SendNotificationEndpoint) (revamp-CommentManager/Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)


## Usado por

- `revamp-actionplan` — Fila (SQS): envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb>
- `revamp-alert` — Fila (SQS): envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb>
- `revamp-comment` — Fila (SQS): envia para a fila COMMENT_MENTION_WORKER_<amb>
- `revamp-comment` — Fila (SQS): envia para a fila COMMENT_HEADER_WORKER_<amb>
- `revamp-kaizen` — Fila (SQS): envia para a fila COMMENT_GROUP_DELETE_WORKER_<amb>

## Filas/tópicos citados no código
- `COMMENT_MENTION_WORKER_LOCAL` (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/WorkerMention.cs:26)
- `COMMENT_MENTION_WORKER` (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/WorkerMention.cs:28)
- `COMMENT_HEADER_WORKER_LOCAL` (Solvace.CommentManager/src/Solvace.CommentManager.Worker/Worker.cs:26)
- `COMMENT_HEADER_WORKER` (Solvace.CommentManager/src/Solvace.CommentManager.Worker/Worker.cs:28)
- `COMMENTMANAGER_MICROSOFTTEAMS` (Solvace.CommentManager/src/Solvace.CommentManager.Application/UseCases/UpsertCommentHeader/UpsertCommentHeaderCommandBase.cs:31)
- `BUCKET_S3` (Solvace.CommentManager/src/Solvace.CommentManager.Application/UseCases/UpsertCommentHeader/UpsertCommentHeaderCommandBase.cs:32)
- `ASPNETCORE_ENVIRONMENT` (Solvace.CommentManager/src/Solvace.CommentManager.Application/UseCases/UpsertCommentHeader/UpsertCommentHeaderCommandBase.cs:313)
- `NOTIFICATION_WORKER_LOCAL` (Solvace.CommentManager/src/Solvace.CommentManager.Application.MentionWorker/UseCases/SendNotificationFromWorker/SendNotificationFromWorkerHandler.cs:40)
- `NOTIFICATION_WORKER` (Solvace.CommentManager/src/Solvace.CommentManager.Application.MentionWorker/UseCases/SendNotificationFromWorker/SendNotificationFromWorkerHandler.cs:42)

## URLs de configuração
- `UrlNotification` = https://api-commentmanager-rc.solvacelabs.com/Notification/Send (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/appsettings.HotfixVersion.json)
- `UrlNotification` = https://api-commentmanager-rc.solvacelabs.com/Notification/Send (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/appsettings.ReleaseVersion.json)
- `UrlNotification` = https://api-commentmanager-prod.solvacelabs.com/Notification/Send (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/appsettings.json)
- `UrlNotification` = https://api-commentmanager-dev.solvacelabs.com/Notification/Send (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/appsettings.Development.json)
- `UrlNotification` = https://api-commentmanager-edge.solvacelabs.com/Notification/Send (Solvace.CommentManager/src/Solvace.CommentManagerMention.Worker/appsettings.Edge.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.HotfixVersion.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.ReleaseVersion.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.ReleaseVersion.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.ReleaseVersion.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.ReleaseVersion.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Development.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Development.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Development.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Development.json)
- `TeamsConfig:Scope` = https://graph.microsoft.com/.default (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Edge.json)
- `TeamsConfig:AppEntityUrl` = https://teams.microsoft.com/l/entity/{appId}/{tabId} (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Edge.json)
- `TeamsConfig:LoginEndpoint` = https://login.microsoftonline.com/{0}/oauth2/v2.0/token (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Edge.json)
- `TeamsConfig:SendNotificationEndpoint` = https://graph.microsoft.com/v1.0/users/{0}/teamwork/sendActivityNotification (Solvace.CommentManager/src/Lambda.CommentManager.MicrosoftTeams/appsettings.Edge.json)
