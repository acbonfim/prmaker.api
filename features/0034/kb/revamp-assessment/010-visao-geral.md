<!-- gerado por mapear.py a partir de revamp-Assessment@d573d594 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de assessments no estate Revamp. Utiliza SQL Server para acesso a dados via Dapper e BuildingBlocks.DataAccess, AWS S3 para armazenamento de arquivos, segurança JWT, Swagger, CORS, logging AWS, pipeline de FluentValidation e um helper de formatação de URLs na camada de abstrações. É um módulo auto-contido que não realiza chamadas HTTP externas a outros serviços do estate.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Assessment.API` | api | net8.0 |
| `Solvace.Assessment.Application` | application | net8.0 |
| `Solvace.Assessment.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Assessment.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Assessment.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, DataAccess, DataAccess.Repositories, FormatUrlHelper

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 11 C#, 0 SQL · commit `d573d594` (master, 2026-09-25)
