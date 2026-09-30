<!-- gerado por mapear.py a partir de revamp-Datalake@fa472f6b (2026-09-29) -->

> Serviço .NET 8 que materializa diariamente as views do schema `[datastage]` das bases locais das plantas em **Parquet no S3** e as serve ao widget de Pivot (Perspective, agrega no browser) como **Arrow IPC**, com filtro de **sites permitidos**, **colunas restritas** (nível coluna) e **registros confidenciais** (nível linha, regra do legado).

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Datalake.API` | api | net8.0 |
| `Solvace.Datalake.Application` | application | net8.0 |
| `Solvace.Datalake.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Datalake.Domain` | domain | net8.0 |
| `Solvace.Datalake.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Datalake.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Datalake.Infra.Source.Fixture` | infra | net8.0 |
| `Solvace.Datalake.Infra.Storage` | infra | net8.0 |
| `Solvace.Datalake.Queue.Worker` | worker | net8.0 |
| `Solvace.Datalake.Api.Tests` | test | net8.0 |
| `Solvace.Datalake.Application.Tests` | test | net8.0 |
| `Solvace.Datalake.Tests.Integration` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**CI (GitHub Actions):** deploy-prod.yml, deploy-qa.yml, deploy-rt.yml, validate-prod.yml

Arquivos: 82 C#, 2 SQL · commit `fa472f6b` (master, 2026-09-29)
