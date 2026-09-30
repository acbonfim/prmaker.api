<!-- gerado por mapear.py a partir de revamp-CustomField@b4eddf6b (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Gerencia campos customizados no estate Revamp. Suporta definição, armazenamento e recuperação de valores de campos customizados para diversas entidades. Utiliza S3 para campos do tipo arquivo, queries globais de parâmetros para dados cross-service, EF Core para acesso a dados e utilitários de formatação de datas.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.CustomField.API` | api | net8.0 |
| `Solvace.CustomField.Application` | application | net8.0 |
| `Solvace.CustomField.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.CustomField.Domain` | domain | net8.0 |
| `Solvace.CustomField.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.CustomField.Application.Tests` | test | net8.0 |
| `Solvace.CustomField.Infra.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess.Migrations, DataAccess.Repositories, DatetimeFormatter, Domain.Entity, EntityFrameworkCore, FormatUrlHelper, GetGlobalParameterValueQuery

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 177 C#, 3 SQL · commit `b4eddf6b` (master, 2026-09-25)
