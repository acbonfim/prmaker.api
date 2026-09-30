<!-- gerado por mapear.py a partir de revamp-RCA@dc4a1a83 (2026-09-25) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo de Root Cause Analysis (RCA). Gerencia workflows de análise de causa raiz, incluindo estruturas de Fishbone, Why-Why e investigações correlatas. Integra-se com múltiplos domínios via consumidores AWS Lambda (Centerline, Checklist, Documentation, Equipment, Kaizen, LIL, LPP, PhysicalLayout, Team, UDA). Utiliza consultas KPI ScoreCard, campos customizados, formatação de timezone, mensageria SNS/SQS, armazenamento S3, exportação PDF/Excel e acesso a dados legados.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.RCA.Application` | lambda | net8.0 |
| `Lambda.RCA.Schema.Centerline` | lambda | net8.0 |
| `Lambda.RCA.Schema.Checklist` | lambda | net8.0 |
| `Lambda.RCA.Schema.Documentation` | lambda | net8.0 |
| `Lambda.RCA.Schema.Equipment` | lambda | net8.0 |
| `Lambda.RCA.Schema.Kaizen` | lambda | net8.0 |
| `Lambda.RCA.Schema.LIL` | lambda | net8.0 |
| `Lambda.RCA.Schema.LPP` | lambda | net8.0 |
| `Lambda.RCA.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.RCA.Schema.Team` | lambda | net8.0 |
| `Lambda.RCA.Schema.UDA` | lambda | net8.0 |
| `Lambda.RCA.Schema.User` | lambda | net8.0 |
| `Solvace.RCA.API` | api | net8.0 |
| `Solvace.RCA.Application` | application | net8.0 |
| `Solvace.RCA.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.RCA.Domain` | domain | net8.0 |
| `Solvace.RCA.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.RCA.Utils` | other | net8.0 |
| `Solvace.RCA.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.RCA.Application.Tests` | test | net8.0 |
| `Solvace.RCA.Infra.Data.Global.SqlServer.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, CustomFieldValue, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetLocationsQuery, GetSiteByInstanceNameQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Centerline, IntegrationEvents.Checklist, IntegrationEvents.Documentation, IntegrationEvents.Equipament, IntegrationEvents.Kaizen, IntegrationEvents.LIL, IntegrationEvents.LPP, IntegrationEvents.PhysicalLayout, IntegrationEvents.RCA, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, LegacyData, LegacyData.Abstractions, Pagination, Producer, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 695 C#, 0 SQL · commit `dc4a1a83` (master, 2026-09-25)
