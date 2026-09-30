<!-- gerado por mapear.py a partir de revamp-ModuleIntegration@e535319a (2026-09-25) -->

> * Esse projeto se consiste em várias "Solutions" dentro da pasta "Modules".

## Descrição (revamp-wiki)
Módulo responsável pelas integrações cross-module no estate Revamp. Orquestra sincronização de dados e fluxos orientados a eventos entre módulos por meio de mensageria SQS, suportando múltiplos backends de banco de dados (AuroraPostgreSQL, Global/Local SqlServer). Expõe uma API e múltiplos workers especializados (INC, RCA, ACP, Application) para processamento de eventos de integração provenientes de diferentes módulos fonte.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.ModuleIntegration.ACP.Worker` | worker | net8.0 |
| `Solvace.ModuleIntegration.API` | api | net8.0 |
| `Solvace.ModuleIntegration.Application` | integration | net8.0 |
| `Solvace.ModuleIntegration.Application.Abstractions` | integration | net8.0 |
| `Solvace.ModuleIntegration.Application.Worker` | worker | net8.0 |
| `Solvace.ModuleIntegration.Domain` | integration | net8.0 |
| `Solvace.ModuleIntegration.INC.Worker` | worker | net8.0 |
| `Solvace.ModuleIntegration.Infra.Data.AuroraPostgreSQL` | integration | net8.0 |
| `Solvace.ModuleIntegration.Infra.Data.Global.SqlServer` | integration | net8.0 |
| `Solvace.ModuleIntegration.Infra.Data.Local.SqlServer` | integration | net8.0 |
| `Solvace.ModuleIntegration.RCA.Worker` | worker | net8.0 |
| `Solvace.ModuleIntegration.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, GetLanguageTermsGlobalQuery, GetTimezoneInfoByUserIdGlobalQuery, Infra.Service.AwsSqs

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 164 C#, 0 SQL · commit `e535319a` (master, 2026-09-25)
