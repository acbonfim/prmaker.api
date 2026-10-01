<!-- gerado por mapear.py a partir de revamp-BOS@2b37179e (2026-09-26) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Business Object System (BOS) é o hub central de dados mestres e configuração do estate Revamp. Gerencia layout físico, empreiteiros, equipes, usuários, UDAs, KPI scorecards e analytics via datalake/Athena. Não realiza chamadas HTTP diretas a outros módulos — consome dados cross-service exclusivamente via pacotes de query BuildingBlocks e eventos de integração assíncronos via SQS/SNS. Expõe duas superfícies de API (REST principal e Integration API) e workers Lambda dedicados ao processamento de eventos de atualização de schema por domínio.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.BOS.Application` | lambda | net8.0 |
| `Lambda.BOS.Schema.Contractor` | lambda | net8.0 |
| `Lambda.BOS.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.BOS.Schema.Team` | lambda | net8.0 |
| `Lambda.BOS.Schema.UDA` | lambda | net8.0 |
| `Lambda.BOS.Schema.User` | lambda | net8.0 |
| `Solvace.BOS.API` | api | net8.0 |
| `Solvace.BOS.Application` | application | net8.0 |
| `Solvace.BOS.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.BOS.Application.Lambda` | lambda | net8.0 |
| `Solvace.BOS.Datalake.Application` | application | net8.0 |
| `Solvace.BOS.Domain` | domain | net8.0 |
| `Solvace.BOS.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.BOS.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.BOS.Integration.API` | api | net8.0 |
| `Solvace.BOS.Integration.Application` | integration | net8.0 |
| `Solvace.BOS.Queue.Worker` | worker | net8.0 |
| `Solvace.BOS.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.BOS.Application.Tests` | test | net8.0 |
| `Solvace.BOS.Infra.Data.Global.SqlServer.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsAthena, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, CustomFieldValue, CustomFieldValue.Abstractions, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetLocationsQuery, GetSiteByInstanceNameQuery, GetSiteParameterValueGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Contractor, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Pagination, Producer, ThirdPartyComponents.Flexmonster

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 785 C#, 0 SQL · commit `2b37179e` (master, 2026-09-26)
