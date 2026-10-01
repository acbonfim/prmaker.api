<!-- gerado por mapear.py a partir de revamp-Comment@03a5ed5f (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Modulo responsavel pelo gerenciamento de comentarios no estate Revamp. Suporta processamento assincrono de mensagens via SQS (Lambda consumer para exclusao de grupos), upload de arquivos via S3, cache distribuido com Redis e consultas globais de informacoes de usuario e timezone. Expoe uma REST API com infraestrutura compativel com SignalR, usando Redis como backplane para escala horizontal.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Comment.DeleteGroup` | lambda | net8.0 |
| `Solvace.Comment.API` | api | net8.0 |
| `Solvace.Comment.Application` | application | net8.0 |
| `Solvace.Comment.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Comment.CrossCutting` | other | net8.0 |
| `Solvace.Comment.Domain` | domain | net8.0 |
| `Solvace.Comment.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Comment.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Comment.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, Infra.Service.AwsSqs, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 236 C#, 0 SQL · commit `03a5ed5f` (master, 2026-09-25)
