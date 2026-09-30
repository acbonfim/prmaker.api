<!-- gerado por mapear.py a partir de revamp-WorkPermit@d1cedcb3 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo de gerenciamento de Work Permits (Permissões de Trabalho). Responsável pela criação, ciclo de vida e upload de arquivos relacionados a permissões de trabalho. Utiliza SQL Server para persistência via EF Core e Dapper, e AWS S3 para armazenamento de arquivos.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.WorkPermit.API` | api | net8.0 |
| `Solvace.WorkPermit.Application` | application | net8.0 |
| `Solvace.WorkPermit.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.WorkPermit.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.WorkPermit.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, DataAccess, DataAccess.Repositories, FormatUrlHelper, NuGetPackages

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 11 C#, 0 SQL · commit `d1cedcb3` (master, 2026-09-25)
