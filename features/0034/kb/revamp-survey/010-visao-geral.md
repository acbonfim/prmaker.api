<!-- gerado por mapear.py a partir de revamp-Survey@e383ac06 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de Survey (pesquisas/questionários) do estate Revamp. Expõe uma API REST (Solvace.Survey.API), um Worker e Lambdas AWS SQS para processamento de eventos de integração (Users, UDA, PhysicalLayout, Team). Utiliza arquitetura multi-banco (Corporate, Local e Global SqlServer) e integra com múltiplos BuildingBlocks globais para consultas cross-service, cache distribuído, armazenamento S3, exportação Excel/PDF e tradução multilíngue.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Survey.Application` | lambda | net8.0 |
| `Lambda.Survey.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.Survey.Schema.Team` | lambda | net8.0 |
| `Lambda.Survey.Schema.UDA` | lambda | net8.0 |
| `Lambda.Survey.Schema.User` | lambda | net8.0 |
| `Solvace.Survey.API` | api | net8.0 |
| `Solvace.Survey.Application` | application | net8.0 |
| `Solvace.Survey.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Survey.Domain` | domain | net8.0 |
| `Solvace.Survey.Infra.Data.Corporate.SqlServer` | infra | net8.0 |
| `Solvace.Survey.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Survey.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Survey.Worker` | worker | net8.0 |
| `Solvace.Survey.API.Tests` | test | net8.0 |
| `Solvace.Survey.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Survey.Application.Tests` | test | net8.0 |
| `Solvace.Survey.Domain.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Corporate (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, Enums, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetSiteByInstanceNameQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Pagination, Producer, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 406 C#, 0 SQL · commit `e383ac06` (master, 2026-09-25)
