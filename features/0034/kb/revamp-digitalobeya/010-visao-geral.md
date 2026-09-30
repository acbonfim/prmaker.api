<!-- gerado por mapear.py a partir de revamp-DigitalObeya@d098afe0 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de Digital Obeya que fornece salas estilo dashboard com widgets configuráveis. Suporta comunicação em tempo real via SignalR com backplane Redis (StackExchangeRedis), integração com AWS IoT, exportação de arquivos em PDF e Excel, cache distribuído com Redis, e integração com **~19 módulos** do estate via BuildingBlocks de ModuleIntegration. Possui dois projetos AWS Lambda: um para cálculos periódicos agendados (Calc.Execute) e outro para limpeza de dados de keep-alive (KeepAlive.Clean). > **Estado (2026-06-29):** branch `feature/obeya-v1` — TargetFramework `net10.0`, todos os BBs em versão **3.0.0** (atualizado). Em 2026-06 (US-51206) o DigitalObeya passou a ser o hub central de integração: além de ActionPlan/BOS/CIL, foram adicionados ~16 novos BBs de ModuleIntegration (Administration, Alerts, Assessment, Centerline, Checklist, Complaints, DefectTag, Documentation, Incidents, Kaizen, MOC, NonConformity, OPL, Project, RCA, UnsafeCondition, WorkPermit). A **Wave 4 e 5 do upgrade net10 dos BBs foram concluídas e publicadas** (PRs #207–208, 2026-06-29) — todos os 47 BBs no net10. Ver `BuildingBlocks`.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.DigitalObeya.Application` | lambda | net8.0 |
| `Lambda.DigitalObeya.Calc.Execute` | lambda | net8.0 |
| `Lambda.DigitalObeya.KeepAlive.Clean` | lambda | net8.0 |
| `Solvace.DigitalObeya.API` | api | net8.0 |
| `Solvace.DigitalObeya.Application` | application | net8.0 |
| `Solvace.DigitalObeya.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.DigitalObeya.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.DigitalObeya.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.DigitalObeya.API.Tests` | test | net8.0 |
| `Solvace.DigitalObeya.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsIoT, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Repositories, DatetimeFormatter, Enums, EnvironmentVariableHelper, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsLambda, Pagination, SqlServer.GetLastInstanceByUserId

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 90 C#, 0 SQL · commit `d098afe0` (master, 2026-09-25)
