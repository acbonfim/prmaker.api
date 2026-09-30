<!-- gerado por mapear.py a partir de revamp-MasterData@5caa80ec (2026-09-29) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de dados mestre do estate Revamp, centralizando referências fundamentais como unidades, departamentos, áreas, equipes, equipamentos, layout físico, classificações, materiais, fornecedores, empreiteiros e falhas. Atua como produtor primário de eventos de integração para todos os demais módulos que consomem dados mestre, utilizando SNS/SQS para mensageria assíncrona, Redis para cache distribuído e S3 para armazenamento de arquivos. Possui capacidades de exportação de dados em formatos Excel e PDF.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.MasterData.API` | api | net8.0 |
| `Solvace.MasterData.Application` | application | net8.0 |
| `Solvace.MasterData.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.MasterData.Domain` | domain | net8.0 |
| `Solvace.MasterData.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.MasterData.Application.Tests` | test | net8.0 |
| `Solvace.MasterData.Infra.Data.Local.SqlServer.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, AspNetCore.Translate, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetLastSiteInfoByUserIdGlobalQuery, GetTimezoneHourDiffByUserIdGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsSns, Infra.Service.AwsSqs, IntegrationEvents, IntegrationEvents.Classification, IntegrationEvents.Contractor, IntegrationEvents.Equipament, IntegrationEvents.Failure, IntegrationEvents.Material, IntegrationEvents.MaterialType, IntegrationEvents.PhysicalLayout, IntegrationEvents.Supplier, IntegrationEvents.Team, IntegrationEvents.UnityDepartArea, Pagination

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 218 C#, 0 SQL · commit `5caa80ec` (master, 2026-09-29)
