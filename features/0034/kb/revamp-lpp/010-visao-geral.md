<!-- gerado por mapear.py a partir de revamp-LPP@136b9564 (2026-09-25) -->

> "# Root Cause Analysis Module API

## Descrição (revamp-wiki)
Lesson Practice Plan (LPP) — módulo de gestão de planos de prática de aprendizado, com suporte a campos customizados, exportação Excel/PDF, mensageria SQS, cache Redis, eventos de integração (LPP, Users, PhysicalLayout, Team, UnityDepartArea) e processamento assíncrono via Lambda (consumidores de schema e serviço de cancelamento automático).

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.LPP.Application` | lambda | net8.0 |
| `Lambda.LPP.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.LPP.Schema.Team` | lambda | net8.0 |
| `Lambda.LPP.Schema.UDA` | lambda | net8.0 |
| `Lambda.LPP.Schema.User` | lambda | net8.0 |
| `Lambda.LPP.Service.AutomaticCancelation` | lambda | net8.0 |
| `Solvace.LPP.API` | api | net8.0 |
| `Solvace.LPP.Application` | application | net8.0 |
| `Solvace.LPP.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.LPP.Domain` | domain | net8.0 |
| `Solvace.LPP.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.LPP.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.LPP.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.LPP.Application.Tests` | test | net8.0 |
| `Solvace.LPP.Domain.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, CheckIfUserAdminQuery, CustomFieldValue, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, Enums, FormatUrlHelper, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.LPP, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, code-quality-review.yml, pr-checklist.yml, pull.yml

Arquivos: 827 C#, 1 SQL · commit `136b9564` (master, 2026-09-25)
