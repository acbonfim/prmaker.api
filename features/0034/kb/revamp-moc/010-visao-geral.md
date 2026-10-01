<!-- gerado por mapear.py a partir de revamp-Moc@78b50f64 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Module of Change (MOC) — gerencia e controla mudanças em processos, equipamentos ou sistemas. Oferece upload de arquivos via S3, acesso a dados SQL Server e infraestrutura padrão de segurança, validação e logging para APIs.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Moc.API` | api | net8.0 |
| `Solvace.Moc.Application` | application | net8.0 |
| `Solvace.Moc.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Moc.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Moc.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, DataAccess, DataAccess.Repositories, FormatUrlHelper, NuGetPackages

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 11 C#, 0 SQL · commit `78b50f64` (master, 2026-09-25)
