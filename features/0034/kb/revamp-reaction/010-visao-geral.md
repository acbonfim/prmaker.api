<!-- gerado por mapear.py a partir de revamp-Reaction@6b3f8eaa (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de reações (emoji/like) em itens de conteúdo ao longo do estate Revamp. Expõe uma REST API protegida por autenticação e validações, utiliza mensageria assíncrona via SQS (Producer e Infra.Service.AwsSqs) e consome eventos de integração relacionados a usuários por meio de uma função Lambda dedicada (Lambda.Reaction.Application).

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Reaction.Application` | lambda | net8.0 |
| `Lambda.Reaction.Schema.User` | lambda | net8.0 |
| `Solvace.Reaction.API` | api | net8.0 |
| `Solvace.Reaction.Application` | application | net8.0 |
| `Solvace.Reaction.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Reaction.Domain` | domain | net8.0 |
| `Solvace.Reaction.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Reaction.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.Reaction.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess.Migrations, DataAccess.Repositories, EntityFrameworkCore, GetApplicationLocalQuery, Infra.Service.AwsLambda, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Users, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 127 C#, 0 SQL · commit `6b3f8eaa` (master, 2026-09-25)
