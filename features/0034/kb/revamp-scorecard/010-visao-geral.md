<!-- gerado por mapear.py a partir de revamp-ScoreCard@6a6b1180 (2026-09-25) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo de ScoreCard/KPI do estate Revamp, responsável pelo gerenciamento de scorecards e indicadores de desempenho. Utiliza funções Lambda para processamento de eventos de usuário e migrações, com acesso a dados via SQL Server, mensageria assíncrona por SQS, armazenamento de arquivos no S3, exportação para PDF e Excel, e BuildingBlocks de queries globais para dados de usuário e fuso horário.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Scorecard.Application` | lambda | net8.0 |
| `Lambda.Scorecard.Schema.User` | lambda | net8.0 |
| `Solvace.Scorecard.API` | api | net8.0 |
| `Solvace.Scorecard.Application` | application | net8.0 |
| `Solvace.Scorecard.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Scorecard.Domain` | domain | net8.0 |
| `Solvace.Scorecard.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Scorecard.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Scorecard.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, DataAccess, DataAccess.Migrations, DataAccess.Repositories, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Users

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 133 C#, 0 SQL · commit `6a6b1180` (master, 2026-09-25)
