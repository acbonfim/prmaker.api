<!-- gerado por mapear.py a partir de revamp-Users@090e3660 (2026-09-30) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo central de gerenciamento de usuários do estate Revamp, responsável pelo ciclo de vida de usuários, papéis, times, layout físico (UnityDepartArea, PhysicalLayout) e integração com identidade via AWS Cognito. Atua como módulo provedor: expõe dados de usuário para o restante do estate por meio de integration events (SQS/SNS) e global query BuildingBlocks. Não realiza chamadas HTTP de saída — outros módulos consomem os dados de Users via `GetUserInfoGlobalQuery`, `GetTimezoneHourDiffByUserIdGlobalQuery` e eventos de integração. Utiliza Lambda para processamento assíncrono de schemas de PhysicalLayout e UnityDepartArea.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Users.Application` | lambda | net8.0 |
| `Lambda.Users.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.Users.Schema.UDA` | lambda | net8.0 |
| `Solvace.Users.API` | api | net8.0 |
| `Solvace.Users.Application` | application | net8.0 |
| `Solvace.Users.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Users.Domain` | domain | net8.0 |
| `Solvace.Users.Infra` | infra | net8.0 |
| `Solvace.Users.Infra.Dapper` | infra | net8.0 |
| `Solvace.Users.Infra.Data.Corporate.SqlServer` | infra | net8.0 |
| `Solvace.Users.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Users.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Users.Integration.API` | api | net8.0 |
| `Solvace.Users.API.Tests` | test | net8.0 |
| `Solvace.Users.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Users.Application.Tests` | test | net8.0 |
| `Solvace.Users.Infra.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer), Corporate (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsCognito, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.DynamoDB, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.QLDB, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, Enums, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetSiteParameterValueGlobalQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsLambda, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Pagination, Producer, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 809 C#, 0 SQL · commit `090e3660` (master, 2026-09-30)
