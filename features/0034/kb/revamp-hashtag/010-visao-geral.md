<!-- gerado por mapear.py a partir de revamp-Hashtag@240c781f (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de gerenciamento de hashtags do estate Revamp, responsável pela criação, consulta e associação de hashtags. Utiliza SQS para processamento assíncrono de mensagens e Redis para cache distribuído, além de queries globais para acesso cross-service a dados de usuários e parâmetros.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Hashtag.API` | api | net8.0 |
| `Solvace.Hashtag.Application` | application | net8.0 |
| `Solvace.Hashtag.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Hashtag.Domain` | domain | net8.0 |
| `Solvace.Hashtag.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Hashtag.Queue.Worker` | worker | net8.0 |
| `Solvace.Hashtag.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, GetGlobalParameterValueQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsSqs, NuGetPackages

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 258 C#, 0 SQL · commit `240c781f` (master, 2026-09-25)
