<!-- gerado por mapear.py a partir de revamp-DefectTag@1570d951 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de tags de defeitos no estate Revamp. Suporta campos customizados, KPI scorecards, termos multilíngues, dados com aware de timezone, upload de arquivos (S3), trilha de auditoria imutável (QLDB), mensageria assíncrona (SQS/SNS via Producer) e consumo de eventos de integração via AWS Lambda (eventos de Users, PhysicalLayout, Team e Equipment). > **Mudanças desde 2026-06-11 (branch `development`):** lote de features de Kanban — filtros (status/data/cor), código de cor customizado, color age, anexos, bookmark scoping, clear planning e guard de usuário planejado nullable. Sem mudança de versão de BBs. TargetFramework permanece `net8.0`.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `DFT.Equipment.EventCreated` | other | net8.0 |
| `DFT.PhysicalLayout.EventCreated` | other | net8.0 |
| `DFT.Team.EventCreated` | other | net8.0 |
| `DFT.User.EventCreated` | other | net8.0 |
| `Solvace.DFT.Application.Lambda` | lambda | net8.0 |
| `Solvace.DefectTag.API` | api | net8.0 |
| `Solvace.DefectTag.Application` | application | net8.0 |
| `Solvace.DefectTag.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.DefectTag.Domain` | domain | net8.0 |
| `Solvace.DefectTag.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.DefectTag.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.DFT.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.DefectTag.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.QLDB, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, CustomFieldValue, CustomFieldValue.Abstractions, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, Enums, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetSiteParameterValueGlobalQuery, GetSubortinatesInfoQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Equipament, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.Users, Masterdata.Distribution, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 713 C#, 0 SQL · commit `1570d951` (master, 2026-09-25)
