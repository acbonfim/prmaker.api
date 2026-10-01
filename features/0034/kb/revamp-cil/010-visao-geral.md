<!-- gerado por mapear.py a partir de revamp-CIL@703ca2b0 (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
CIL (Cleaning, Inspection & Lubrication) é o módulo responsável por gerenciar rotinas CIL, layouts físicos, equipamentos, empreiteiros, fornecedores, equipes e usuários. Expõe uma REST API e consome filas AWS SQS via AWS Lambda para processar eventos de integração de master-data. A sincronização de dados entre domínios é feita exclusivamente via mensageria assíncrona (SQS/SNS), e o módulo também publica eventos de integração LIL para outros consumidores do estate. > **Mudanças desde 2026-06-11 (branch `67242-67596-dev-01`):** relatórios de compliance e duração por período/dia/mês/ano (AB#60331-60335), Lambda de geração agendada de inspeções (AB#61102), Version History com lista paginada de versões (AB#60329), histórico de tempo de resposta de questões, feature flag de action plan para CIL. **Migrou para o BB `TimezoneQueries` 1.0.0** (AB#60327), substituindo GetTimezoneInfoByUserIdGlobalQuery. Usa `DataAccess` 2.4.0 e `DataAccess.Dapper`. TargetFramework permanece `net8.0`.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.CIL.API` | api | net8.0 |
| `Solvace.CIL.Application` | application | net8.0 |
| `Solvace.CIL.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.CIL.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.CIL.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.CIL.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer), Local (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.Cors, AspNetCore.HttpContextAccessor, AspNetCore.Security, AspNetCore.Swagger, DataAccess.Repositories, DatetimeFormatter, FormatUrlHelper, GetGlobalParameterValueQuery, GetSubortinatesInfoQuery, GetTimezoneInfoByUserIdGlobalQuery, IntegrationEvents.LIL

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 78 C#, 0 SQL · commit `703ca2b0` (master, 2026-09-25)
