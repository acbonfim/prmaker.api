<!-- gerado por mapear.py a partir de revamp-Subtitle@6af6c53a (2026-09-25) -->

> * [WRITE CURRENT SERVICE DOCUMENTATION HERE]

## Descrição (revamp-wiki)
Módulo responsável por gerar, gerenciar e traduzir legendas para conteúdos de mídia, utilizando os serviços AWS Transcribe e AWS Translate por meio de BuildingBlocks dedicados. Inclui uma função Lambda (Lambda.Subtitle.EventTranscribe) para processamento assíncrono de eventos de transcrição disparados via SQS.

## Projetos
| Projeto | Tipo | Framework |
|---|---|---|
| `Lambda.Subtitle.EventTranscribe` | lambda | net8.0 |
| `Solvace.Subtitle.API` | api | net8.0 |
| `Solvace.Subtitle.Application` | application | net8.0 |
| `Solvace.Subtitle.Application.Abstractions` | abstractions | net8.0 |
| `Solvace.Subtitle.Domain` | domain | net8.0 |
| `Solvace.Subtitle.Infra.Data.Global.SqlServer` | infra | net8.0 |
| `Solvace.Subtitles.Commons` | other | net8.0 |
| `Solvace.Subtitle.Application.Tests` | test | net8.0 |

**Camadas de banco:** Global (SqlServer)

**Building blocks:** AspNetCore.AwsLogging, AspNetCore.AwsS3, AspNetCore.AwsTranscribe, AspNetCore.AwsTranslate, AspNetCore.Cors, AspNetCore.Security, AspNetCore.Swagger, Cache.Redis, DataAccess, DataAccess.Migrations, DataAccess.Repositories, EntityFrameworkCore, GetApplicationLocalQuery, GetGlobalParameterValueQuery, GetLanguageTermsGlobalQuery, GetUserInfoGlobalQuery, Infra.Service.AwsSqs, Producer

**CI (GitHub Actions):** apply_pr_template.yml, pr-checklist.yml, pull.yml

Arquivos: 90 C#, 0 SQL · commit `6af6c53a` (master, 2026-09-25)
