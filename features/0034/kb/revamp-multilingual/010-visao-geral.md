<!-- gerado por mapear.py a partir de revamp-Multilingual@aa520f8b (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Servico responsavel pela gestao de traducoes e termos multilinguais no estate Revamp. Possui API REST, WorkerService e Lambda workers para sincronizacao de termos entre ambientes (SyncDevToProd, SyncProdToDev), worker para importacao via Excel e um worker Lambda para traducao de gap terms (TranslateGapTerms). Usa Aurora PostgreSQL como banco principal e SQL Server global para dados compartilhados do estate.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Solvace.Multilingual.API` | api | net8.0 |
| `Solvace.Multilingual.Application` | application | net8.0 |
| `Solvace.Multilingual.Application.Abstraction` | application | net8.0 |
| `Solvace.Multilingual.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Multilingual.Domain` | domain | net8.0 |
| `Solvace.Multilingual.Infra.Data.AuroraPostgreSQL` | infra | net8.0 |
| `Solvace.Multilingual.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Multilingual.Infra.Data.Local.SqlServer` | infra | net8.0 |
| `Solvace.Multilingual.TranslateGapTerms` | other | net8.0 |
| `Solvace.Multilingual.WorkerService` | worker | net8.0 |
| `Solvace.Multilingual.Application.Tests` | test | net8.0 |

**Camadas de banco:** Local (SqlServer), Global (SqlServer)

**Building blocks:** AspNetCore.ApplicationValidations, AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsSecretsManager, AspNetCore.Cors, AspNetCore.Excel, AspNetCore.HttpContextAccessor, AspNetCore.Pdf, AspNetCore.Security, AspNetCore.SpeechToText, AspNetCore.Swagger, AspNetCore.Translate, Cache.Abstractions, Cache.Redis, DataAccess, DataAccess.Repositories, EntityFrameworkCore, GetLanguageTermsGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsSqs, Symmetric

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 229 C#, 0 SQL · commit `aa520f8b` (master, 2026-09-25)
