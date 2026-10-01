<!-- gerado por mapear.py a partir de revamp-Administration@b1ef29a0 (2026-09-26) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo de administração do estate Revamp, responsável pelo gerenciamento de dados corporativos, globais e locais via SQL Server, além de Aurora PostgreSQL. Utiliza cache distribuído com Redis, armazenamento de arquivos com S3, e depende extensivamente de BuildingBlocks de consulta global para informações de usuário, idioma, timezone, parâmetros de site e parâmetros globais.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Administration.API` | api | net8.0 |
| `Solvace.Administration.Application` | application | net8.0 |
| `Solvace.Administration.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Administration.Domain` | domain | net8.0 |
| `Solvace.Administration.Infra.Data.AuroraPostgreSQL` | infra | net8.0 |
| `Solvace.Administration.Infra.Data.Corporate.SqlServer` | infra | net8.0 |
| `Solvace.Administration.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Administration.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Administration.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Corporate (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, FormatUrlHelper, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetSiteParameterValueGlobalQuery, GetTimezoneHourDiffByUserIdGlobalQuery, Pagination

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 363 C#, 0 SQL · commit `b1ef29a0` (master, 2026-09-26)
