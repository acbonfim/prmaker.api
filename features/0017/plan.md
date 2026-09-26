# Feature 0017 — Upgrade para o .NET 10

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0017` (worktree `../prform.api-0017`), a partir de `master`. Só backend.

## 1. Levantamento (2026-09-26)
- **SDK local**: 10.0.401 já instalado (runtime 10.0.12). 30 projetos em `net8.0`.
- **Pacotes → versão para .NET 10**:

  | Pacote | Hoje | Alvo |
  |---|---|---|
  | EF Core, Design, Relational, Tools | 8.0.2 / 8.0.21 | 10.0.12 |
  | Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.2 | 10.0.3 |
  | AspNetCore.Authentication.JwtBearer, Identity.EntityFrameworkCore, Mvc.NewtonsoftJson | 8.0.2 | 10.0.12 |
  | Microsoft.Extensions.* (Caching, Http, Options, Configuration.Abstractions) | 8.0.x | 10.0.12 |
  | IdentityModel (fixado no `Directory.Build.props`) | 7.1.2 | 8.23.0 (exigido pelo JwtBearer 10) |
  | Swashbuckle.AspNetCore | 6.4.0 | 10.2.3 (Microsoft.OpenApi 2.x) |
  | Microsoft.AspNetCore.Mvc.Versioning (+ApiExplorer) | 5.1.0 (**descontinuado**) | Asp.Versioning.Mvc (+ApiExplorer) 10.2.x |
  | Microsoft.AspNetCore.OpenApi | 8.0.2 (7 projetos, **sem uso**) | removido |
  | Microsoft.AspNetCore.Identity | 2.2.0 (auth, pacote antigo) | removido (tipos vêm do shared framework) |
  | AutoMapper.Extensions 12, MailKit, Octokit, Cloudinary, Anthropic, Newtonsoft | — | mantidos (compatíveis) |
- **Código afetado**: `Cime.BuildingBlocks.Swagger/ConfigSwagger.cs` (versionamento + Swagger), `Program.cs` da auth (Swagger), 13 controllers com `[ApiVersion]` (namespace muda para `Asp.Versioning`).
- **Infra**: Dockerfiles (`sdk:8.0`/`aspnet:8.0` → `10.0`), `deploy-realtime.yml` (`setup-dotnet` 8 → 10). O MonsterASP suporta .NET 10 (x86).
- **Restore local**: o NuGet global do usuário tem o CodeArtifact (401 sem login); os pacotes estão todos no nuget.org → restore local com `--configfile` só com o nuget.org (o CI já usa só o nuget.org).

## 2. Riscos e cuidados
- **EF Core 9+**: `Migrate()` lança erro se o modelo divergir do snapshot (`PendingModelChangesWarning`). Checar `has-pending-model-changes` nos 4 contextos com o dotnet-ef 10.
- **Tokens**: IdentityModel 7 → 8. As api-keys e os JWT atuais (HS512) e os tokens do tempo real (HS256) precisam continuar válidos nos dois sentidos (a API e o relay são publicados em momentos diferentes).
- **Swashbuckle 10 / OpenApi 2**: API de segurança do Swagger mudou (referências por documento).
- **Asp.Versioning**: a constraint `{version:apiVersion}` e o `ReportApiVersions` precisam continuar funcionando; `UseApiVersioning()` deixa de existir.
- **Contrato**: comparar respostas .NET 8 × .NET 10 sobre o mesmo banco (System.Text.Json/Newtonsoft).

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | B1 (TFM, SDK, pacotes, build) |
| 2 | B2 (Asp.Versioning + Swashbuckle 10), B3 (EF 10: modelo × snapshot) |
| 3 | B4 (Docker, pipeline, relay) |
| 4 | T1 (ensaio local: contrato 8 × 10, tokens cruzados, relay, Swagger) |
| 5 | Q1 (deploy) |

- **B1** — `net10.0` em todos os projetos; `global.json` (SDK 10); pacotes da tabela; fixação do IdentityModel em 8.x; remoção dos pacotes sem uso. Build sem erros.
- **B2** — `Asp.Versioning.Mvc` + `ApiExplorer` (rotas `/api/v1` iguais; `using Asp.Versioning` nos controllers); Swashbuckle 10 com a nova API do OpenApi 2 (esquema `x-api-key`), nas duas APIs.
- **B3** — `dotnet ef migrations has-pending-model-changes` nos 4 contextos (dotnet-ef 10 local, sem instalar global); migrações sobem num Postgres limpo.
- **B4** — Dockerfiles 10.0; `deploy-realtime.yml` com .NET 10; `CLAUDE.md`/`deploy/README.md`.
- **T1**:
  1. Banco local com cópia dos dados atuais (`pg_dump` do `db70152` para o Docker).
  2. Mesma bateria de leituras na API .NET 8 (`master`) e na .NET 10: JSON idêntico.
  3. Tokens cruzados: api-key/JWT gerados pela auth .NET 8 aceitos pela API .NET 10 e vice-versa; token do tempo real da API .NET 10 aceito pelo relay .NET 8 e vice-versa.
  4. Swagger das duas APIs abre; `/api/v1/...` e o header de versões.
- **Q1** — PR → deploy das APIs; o relay sobe pelo próprio workflow. Conferir logs e fazer os testes no app. Rollback: revisões anteriores do Cloud Run e o deploy anterior do relay.
