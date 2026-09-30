<!-- gerado por mapear.py a partir de revamp-Views@c402a126 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável pelo gerenciamento de Views no estate Revamp. Utiliza DynamoDB para acesso a dados em conjunto com o BuildingBlock padrão de DataAccess, protegido via AspNetCore.Security e exposto por uma API documentada com Swagger. A camada de Application.Abstractions não carrega dependências NuGet próprias.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Views.API` | api | net8.0 |
| `Solvace.Views.Application` | application | net8.0 |
| `Solvace.Views.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Views.Application.Tests` | test | net8.0 |

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.Cors, AspNetCore.DynamoDB, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 20 C#, 0 SQL · commit `c402a126` (master, 2026-09-25)
