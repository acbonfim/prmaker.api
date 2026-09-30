<!-- gerado por mapear.py a partir de revamp-Authentication@3772dbbd (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Serviço de autenticação do estate Revamp, responsável por autenticação de usuários via AWS Cognito, segurança JWT, gerenciamento de sessão, geração de newsletters em PDF, e acesso a múltiplos bancos de dados SQL Server (Global, Corporate e Local) com suporte a cache distribuído via Redis e armazenamento de arquivos no S3. Expõe duas superfícies de API: a principal (`Solvace.Authentication.API`) e uma dedicada a cenários de integração (`Solvace.Authentication.Integration.API`).

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Authentication.API` | api | net8.0 |
| `Solvace.Authentication.Application` | application | net8.0 |
| `Solvace.Authentication.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Authentication.Common.Application` | application | net8.0 |
| `Solvace.Authentication.Infra.Data.Corporate.SqlServer` | infra | net8.0 |
| `Solvace.Authentication.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Authentication.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Authentication.Infra.SqlServer` | infra | net8.0 |
| `Solvace.Authentication.Integration.API` | api | net8.0 |
| `Solvace.Authentication.Integration.Application` | integration | net8.0 |
| `Solvace.Authentication.API.Tests` | test | net8.0 |
| `Solvace.Authentication.Application.Tests` | test | net8.0 |
| `Solvace.Authentication.Infra.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer), Corporate (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsCognito, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, FormatUrlHelper, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetSiteParameterValueGlobalQuery, GetUserInfoGlobalQuery, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 373 C#, 0 SQL · commit `3772dbbd` (master, 2026-09-25)
