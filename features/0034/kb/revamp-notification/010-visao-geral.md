<!-- gerado por mapear.py a partir de revamp-Notification@20248d6a (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Serviço de notificações responsável por entregar notificações via e-mail (AWS Secrets Manager + Infra.Mail), push (Firebase/FirebaseAdmin), tempo real via SignalR (com Redis como backplane) e filas SQS (workers dedicados). Suporta múltiplos idiomas via consulta de termos globais, resolução de dados de usuário por meio de queries globais e acesso a arquivos no S3. Não realiza chamadas HTTP de saída para outros módulos Revamp — toda comunicação externa é feita por meio de canais assíncronos e BuildingBlocks de infraestrutura.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Notification.API` | api | net8.0 |
| `Solvace.Notification.Application` | application | net8.0 |
| `Solvace.Notification.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Notification.Application.Common` | application | net8.0 |
| `Solvace.Notification.Application.Worker` | worker | net8.0 |
| `Solvace.Notification.Domain` | domain | net8.0 |
| `Solvace.Notification.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Notification.Infra.Mail` | infra | net8.0 |
| `Solvace.Notification.Infra.PushNotification` | infra | net8.0 |
| `Solvace.Notification.Queue.Worker` | worker | net8.0 |
| `Solvace.Notification.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Abstractions, DataAccess.Migrations, DataAccess.Repositories, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsSqs, Modules, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 360 C#, 0 SQL · commit `20248d6a` (master, 2026-09-25)
