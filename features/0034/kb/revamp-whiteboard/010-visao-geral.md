<!-- gerado por mapear.py a partir de revamp-WhiteBoard@c6dcbeae (2026-09-25) -->

## Descrição (revamp-wiki)
Módulo WhiteBoard responsável por fornecer funcionalidades de quadro branco colaborativo, com suporte a armazenamento de arquivos via S3, acesso a dados com migrations e repositórios, validações de aplicação e integração com parâmetros globais e configurações locais de aplicação via BuildingBlocks compartilhados.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace - Backup.WhiteBoard.API` | api | net8.0 |
| `Solvace.WhiteBoard.API` | api | net8.0 |
| `Solvace.WhiteBoard.Application` | application | net8.0 |
| `Solvace.WhiteBoard.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.WhiteBoard.Domain` | domain | net8.0 |
| `Solvace.WhiteBoard.Infra.Data` | infra | net8.0 |
| `Solvace.WhiteBoard.Application.Test` | test | net8.0 |

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, EntityFrameworkCore, GetApplicationLocalQuery, GetGlobalParameterValueQuery, Pagination

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 55 C#, 0 SQL · commit `c6dcbeae` (master, 2026-09-25)
