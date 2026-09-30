<!-- gerado por mapear.py a partir de revamp-Alert@ffaae36c (2026-09-25) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Modulo responsavel pela criacao, gerenciamento e notificacao de alertas no estate Revamp. Possui arquitetura dual: expoe uma REST API (Solvace.Alert.API) e executa como consumidores AWS Lambda que processam eventos de integracao via SQS para sincronizacao de dados de classificacao, falha, fornecedor, usuarios, layout fisico, equipe, material e tipo de material. A comunicacao entre modulos e feita exclusivamente via eventos SQS e BuildingBlock global queries, sem chamadas HTTP de saida para outros servicos Revamp.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Alert.Application` | lambda | net8.0 |
| `Lambda.Alert.Core` | lambda | net8.0 |
| `Lambda.Alert.Schema.Classification` | lambda | net8.0 |
| `Lambda.Alert.Schema.Failure` | lambda | net8.0 |
| `Lambda.Alert.Schema.Material` | lambda | net8.0 |
| `Lambda.Alert.Schema.MaterialType` | lambda | net8.0 |
| `Lambda.Alert.Schema.PhysicalLayout` | lambda | net8.0 |
| `Lambda.Alert.Schema.Supplier` | lambda | net8.0 |
| `Lambda.Alert.Schema.Team` | lambda | net8.0 |
| `Lambda.Alert.Schema.UDA` | lambda | net8.0 |
| `Lambda.Alert.Schema.User` | lambda | net8.0 |
| `Solvace.Alert.API` | api | net8.0 |
| `Solvace.Alert.Application` | application | net8.0 |
| `Solvace.Alert.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Alert.Domain` | domain | net8.0 |
| `Solvace.Alert.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Alert.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Alert.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Alert.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetKpiScoreCardQuery, GetLanguageTermsGlobalQuery, GetSiteByInstanceNameQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Classification, IntegrationEvents.Failure, IntegrationEvents.Material, IntegrationEvents.MaterialType, IntegrationEvents.PhysicalLayout, IntegrationEvents.Supplier, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, NuGetPackages, Pagination, Producer, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 407 C#, 0 SQL · commit `ffaae36c` (master, 2026-09-25)
