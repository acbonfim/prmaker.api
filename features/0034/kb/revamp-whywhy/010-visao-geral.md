<!-- gerado por mapear.py a partir de revamp-WhyWhy@00b3b1d4 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
WhyWhy implementa a análise dos 5 Porquês (5 Whys) para identificação de causa raiz via questionamento iterativo. Expõe uma REST API, processa eventos de integração de múltiplos domínios (RCA, Users, Teams, Equipment, UnityDepartArea) via handlers AWS Lambda, suporta exportação de arquivos (Excel, PDF, S3), internacionalização multilíngue e mensageria assíncrona via SNS/SQS.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.WhyWhy.Application` | lambda | net8.0 |
| `Lambda.WhyWhy.Schema.Fishbone` | lambda | net8.0 |
| `Lambda.WhyWhy.Schema.RCA` | lambda | net8.0 |
| `Lambda.WhyWhy.Schema.User` | lambda | net8.0 |
| `Solvace.RCA.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.WhyWhy.API` | api | net8.0 |
| `Solvace.WhyWhy.Application` | application | net8.0 |
| `Solvace.WhyWhy.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.WhyWhy.Domain` | domain | net8.0 |
| `Solvace.WhyWhy.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.WhyWhy.Application.Lambda.Tests` | test | net8.0 |
| `Solvace.WhyWhy.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, DataAccess, DataAccess.Migrations, DataAccess.Repositories, Domain.Entity, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetSiteByInstanceNameQuery, GetTimezoneInfoByUserIdGlobalQuery, GetUserInfoGlobalQuery, GetUsersByTeamByIdsQuery, Infra.Service.AwsLambda, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Equipament, IntegrationEvents.PhysicalLayout, IntegrationEvents.RCA, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, IntegrationEvents.Users, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 112 C#, 0 SQL · commit `00b3b1d4` (master, 2026-09-25)
