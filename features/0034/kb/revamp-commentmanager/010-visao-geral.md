<!-- gerado por mapear.py a partir de revamp-CommentManager@47612cb6 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Gerencia comentários em todo o estate Revamp, incluindo processamento de menções (via worker dedicado), notificações para Microsoft Teams (Lambda), operações de delete (Lambda) e capacidades em tempo real via SignalR. Utiliza SQS para processamento assíncrono, ElasticSearch para consultas de posts, cache Redis e múltiplas global queries para dados de usuário, timezone e linguagem. Possui duas camadas de Application distintas: a principal (`Solvace.CommentManager.Application`) e uma específica para processamento de menções (`Solvace.CommentManager.Application.MentionWorker`). > **Mudanças desde 2026-06-11 (branch `edge`):** fix da strategy de UNC revamp, remoção da dependência `Lambda.CommentManager.MicrosoftTeams`, e registro dos serviços de UnsafeConditionalRevamp. Sem mudança de versão de BBs. TargetFramework permanece `net8.0`.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.CommentManager.Delete` | lambda | net8.0 |
| `Lambda.CommentManager.MicrosoftTeams` | lambda | net8.0 |
| `Solvace.CommentManager.API` | api | net8.0 |
| `Solvace.CommentManager.Application` | application | net8.0 |
| `Solvace.CommentManager.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.CommentManager.Application.MentionWorker` | worker | net8.0 |
| `Solvace.CommentManager.CrossCutting` | other | net8.0 |
| `Solvace.CommentManager.Domain` | domain | net8.0 |
| `Solvace.CommentManager.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.CommentManager.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.CommentManager.Worker` | worker | net8.0 |
| `Solvace.CommentManagerMention.Worker` | worker | net8.0 |
| `Solvace.CommentManager.Application.Tests` | test | net8.0 |
| `Solvace.CommentManager.Domain.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, ElasticSearch.GetPostQuery, EntityFrameworkCore, GetApplicationLocalQuery, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, GetUsersByUdaQuery, Infra.Service.AwsSqs, Infra.Service.ElasticDataAccess, Modules, Pagination, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 390 C#, 0 SQL · commit `47612cb6` (master, 2026-09-25)
