<!-- gerado por mapear.py a partir de revamp-Quiz@972e8495 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de Quiz do estate Revamp. Provê criação de quizzes, fluxos de treinamento e conclusão (end-game). Expõe uma REST API (`Solvace.Quiz.API`), um worker de background (`Solvace.Quiz.EndGame.Worker`) e consumidores SQS baseados em Lambda para eventos de integração (Users, UDA, PhysicalLayout, Team). Utiliza Redis para cache, S3 para armazenamento de arquivos, SQS para mensageria assíncrona e o padrão Producer para publicação de eventos de integração.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Quiz.Application` | lambda | net8.0 |
| `Lambda.Quiz.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.Quiz.Schema.Team` | lambda | net8.0 |
| `Lambda.Quiz.Schema.UDA` | lambda | net8.0 |
| `Lambda.Quiz.Schema.User` | lambda | net8.0 |
| `Solvace.Quiz.API` | api | net8.0 |
| `Solvace.Quiz.Application` | application | net8.0 |
| `Solvace.Quiz.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Quiz.Application.Worker` | worker | net8.0 |
| `Solvace.Quiz.Domain` | domain | net8.0 |
| `Solvace.Quiz.EndGame.Worker` | worker | net8.0 |
| `Solvace.Quiz.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Quiz.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Quiz.Application.Tests` | test | net8.0 |
| `Solvace.Quiz.Application.Tests.Integration` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetSiteByInstanceNameQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.PhysicalLayout, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Producer, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 507 C#, 0 SQL · commit `972e8495` (master, 2026-09-25)
