<!-- gerado por mapear.py a partir de revamp-Communication@dd7136c4 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Module responsible for managing communications within the Solvace Revamp estate. It handles communication records and types, supports Excel and PDF exports, and relies on AWS Lambda for async processing of integration events (Users, UDA, PhysicalLayout, Team) via SQS. Exposes a REST API and does not make outbound HTTP calls to other Revamp services, using integration events for all cross-service data synchronization.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Communication.Application` | lambda | net8.0 |
| `Lambda.Communication.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.Communication.Schema.Team` | lambda | net8.0 |
| `Lambda.Communication.Schema.UDA` | lambda | net8.0 |
| `Lambda.Communication.Schema.User` | lambda | net8.0 |
| `Solvace.Communication.API` | api | net8.0 |
| `Solvace.Communication.Application` | application | net8.0 |
| `Solvace.Communication.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Communication.Domain` | domain | net8.0 |
| `Solvace.Communication.Infra.Data` | infra | net8.0 |
| `Solvace.Communication.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Communication.Application.Tests` | test | net8.0 |
| `Solvace.Communication.Domain.Tests` | test | net8.0 |

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetSiteByInstanceNameQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 280 C#, 0 SQL · commit `dd7136c4` (master, 2026-09-25)
