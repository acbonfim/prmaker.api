<!-- gerado por mapear.py a partir de revamp-Fishbone@a51cf0d7 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de diagramas de Fishbone (Ishikawa) para análise de causa raiz, suportando criação e gerenciamento de diagramas. A comunicação assíncrona entre módulos é realizada exclusivamente via eventos de integração (SQS), sem chamadas HTTP diretas a outros serviços Revamp. Possui uma camada Lambda dedicada para sincronização de schema de usuários via IntegrationEvents.Users.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Fishbone.Application` | lambda | net8.0 |
| `Lambda.Fishbone.Schema.User` | lambda | net8.0 |
| `Solvace.Fishbone.API` | api | net8.0 |
| `Solvace.Fishbone.Application` | application | net8.0 |
| `Solvace.Fishbone.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Fishbone.Domain` | domain | net8.0 |
| `Solvace.Fishbone.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Fishbone.Application.Lambda.Test` | test | net8.0 |
| `Solvace.Fishbone.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, Infra.Service.AwsLambda, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Users, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 75 C#, 0 SQL · commit `a51cf0d7` (master, 2026-09-25)
