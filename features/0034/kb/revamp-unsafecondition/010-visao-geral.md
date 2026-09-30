<!-- gerado por mapear.py a partir de revamp-UnsafeCondition@e922493e (2026-09-26) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de condições inseguras (UNC) no estate Solvace Revamp. Suporta criação, acompanhamento e resolução de condições inseguras com campos customizados, anexos de arquivos, termos multilíngues, KPIs de scorecard, eventos de integração para layout físico, times, usuários e equipamentos via consumidores Lambda, mensageria SQS, cache Redis, exportações PDF/Excel e distribuição de master data. > **Mudanças desde 2026-06-11 (branch `fix/oatly-merge-release-version2`):** merge fix/oatly em release-version2 (resolução de conflitos em Create/UpdateUnsafeConditionCommandHandler), remoção do offset de timezone de campos de data truncados e timestamps de audit log, e bump de `CustomFieldValue` 1.0.9.3 → 1.0.9.5 (fix de TypeLoadException no startup + registry read fallback). TargetFramework permanece `net8.0`.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.UNC.Application` | lambda | net8.0 |
| `Lambda.UNC.Schema.Equipment` | lambda | net8.0 |
| `Lambda.UNC.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.UNC.Schema.Team` | lambda | net8.0 |
| `Lambda.UNC.Schema.User` | lambda | net8.0 |
| `Solvace.UnsafeCondition.API` | api | net8.0 |
| `Solvace.UnsafeCondition.Application` | application | net8.0 |
| `Solvace.UnsafeCondition.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.UnsafeCondition.Domain` | domain | net8.0 |
| `Solvace.UnsafeCondition.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.UNC.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.UnsafeCondition.Application.Tests` | test | net8.0 |
| `Solvace.UnsafeCondition.Tests.Integration` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, CustomFieldValue, CustomFieldValue.Abstractions, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, Enums, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetSiteParameterValueGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Equipament, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.Users, Masterdata.Distribution, Modules, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 463 C#, 7 SQL · commit `e922493e` (master, 2026-09-26)
