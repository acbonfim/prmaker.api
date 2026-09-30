<!-- gerado por mapear.py a partir de revamp-Post@2a261152 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de feed social do estate Revamp, responsável pela criação de posts, interações (reações, comentários), reclamações, bloqueio de usuários e notificações em tempo real via SignalR. Utiliza Elasticsearch para busca de conteúdo, Redis para cache distribuído, SQS para mensageria assíncrona e funções Lambda para processamento em background. A comunicação cross-service é feita exclusivamente via BuildingBlocks de queries globais, sem chamadas HTTP diretas a outros módulos.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Post.Application` | lambda | net8.0 |
| `Lambda.Post.RegisterAsPost` | lambda | net8.0 |
| `Lambda.Post.Schema.User` | lambda | net8.0 |
| `Solvace.Post.API` | api | net8.0 |
| `Solvace.Post.Application` | application | net8.0 |
| `Solvace.Post.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Post.Domain` | domain | net8.0 |
| `Solvace.Post.Infra.Data.Elastic` | infra | net8.0 |
| `Solvace.Post.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Post.Infra.Service` | infra | net8.0 |
| `Solvace.Post.Queue.Worker` | worker | net8.0 |
| `Solvace.Post.Queue.Worker.Interactions` | worker | net8.0 |
| `Solvace.Post.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Post.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, Infra.Service.AwsLambda, Infra.Service.AwsSqs, Infra.Service.ElasticDataAccess, IntegrationEvents, IntegrationEvents.Users, Pagination, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 263 C#, 0 SQL · commit `2a261152` (master, 2026-09-25)
