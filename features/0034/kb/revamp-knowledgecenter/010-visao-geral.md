<!-- gerado por mapear.py a partir de revamp-KnowledgeCenter@0f54499f (2026-09-25) -->

> Backend service for the **Knowledge Center / Base de Conhecimento** module — part of Solvace's "Revamp" initiative. A global library of articles authored by the Solvace CS team (Admin/Redator profiles) and consumed by every user of every client and site: search, categories/tags navigation, article reading, reactions (útil/não útil), categorized feedback, sharing, and an admin analytics dashboard.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.KnowledgeCenter.API` | api | net10.0 |
| `Solvace.KnowledgeCenter.Application` | application | net10.0 |
| `Solvace.KnowledgeCenter.Application.Abstractions` | abstractions | net10.0 |
| `Solvace.KnowledgeCenter.Domain` | domain | net10.0 |
| `Solvace.KnowledgeCenter.Infra.Data.Global.SqlServer` | infra | net10.0 |
| `Solvace.KnowledgeCenter.Infra.S3` | infra | net10.0 |
| `Solvace.KnowledgeCenter.Application.Tests` | test | net10.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess.Migrations, DataAccess.Repositories, GetGlobalParameterValueQuery, Pagination, Storage.AwsS3

**CI (GitHub Actions):** pull.yml

Arquivos: 15 C#, 1 SQL · commit `0f54499f` (master, 2026-09-25)
