# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CIME (Client Integration and Management Ecosystem) — a .NET 8.0 modular monolith API for pull request management, integrating with GitHub, Azure DevOps, and AI providers. Written in C# with nullable reference types and implicit usings enabled.

## Build & Run Commands

```bash
# Local database (PostgreSQL 18, same engine as production) — Development points here, never to production
docker compose up -d

# Build everything (API host, modules, Cime.Auth, realtime relay)
dotnet build Solvace.Master.sln

# Run the main API / the auth API (Development uses the local Postgres)
dotnet run --project CIME/modules/Solvace.PullRequests/src/solvace.prform.api
dotnet run --project CIME/modules/Cime.Auth/src/cime.auth.api

# Add an EF migration (one DbContext per module/schema; Development connection = local Postgres)
dotnet ef migrations add <Name> --context DefaultContext \
  --project CIME/modules/Solvace.PullRequests/src/solvace.prform.infra \
  --startup-project CIME/modules/Solvace.PullRequests/src/solvace.prform.api
#   VacationContext → --project CIME/modules/Solvace.Vacations/src/solvace.vacations.infra
#   TimelineContext → --project CIME/modules/Solvace.Timeline/src/solvace.timeline.infra
#   Auth            → --project/--startup-project CIME/modules/Cime.Auth/src/cime.auth.api

# AWS CodeArtifact login (for private NuGet packages)
aws codeartifact login --tool dotnet --repository revamp --domain solvace --domain-owner 367983645102 --region us-east-1
```

Migrations are applied automatically on startup outside Development (advisory lock, fatal on failure). No test projects exist in this repository currently.

## Architecture

### Module Structure

All modules live under `CIME/modules/`. Each follows clean architecture layers: **domain → application → infra → api**.

| Module | Responsibility |
|---|---|
| **Solvace.PullRequests** | Core module — the API host, PR registration, forms, plugins, handovers |
| **Solvace.GitHub** | GitHub API integration for PR operations |
| **Solvace.Azure** | Azure DevOps integration |
| **Solvace.AI** | AI provider abstraction (Gemini, OpenAI, Claude) |
| **Solvace.Vacations** | Vacation request and balance management |
| **Solvace.Timeline** | Card timeline entries (incl. Teams import) |
| **Cime.Auth** | Separate auth API (ASP.NET Identity): users, roles, services, JWT and x-api-keys |
| **Cime.RealTime** | Realtime relay (SignalR hub) hosted outside Cloud Run |
| **Cime.BuildingBlocks** | Shared cross-cutting concerns (auth, CORS, caching, Swagger, exception handling, realtime, persistence) |

`tools/Cime.DataMigrator` is the one-off data migrator used to move data into PostgreSQL (features 0014/0015).

### Entry Point & DI

`Program.cs` in `solvace.prform.api` is the host. Each module registers itself via an `AddXxxModule()` extension method (e.g., `AddGitHubModule`, `AddAIModule`). Building blocks register via similar extensions (`AddSecurityAuth`, `AddCorsPolice`, `AddCacheService`, `AddSwaggerConfig`, `AddRealTimeService`).

### Database

- **PostgreSQL 18** (Npgsql), a single database with **one schema per module**: `auth` (Cime.Auth), `prform` (`DefaultContext`), `vacations` (`VacationContext`), `timeline` (`TimelineContext`); each context has its own `__EFMigrationsHistory` in its schema. The host reads auth users via the read-only `AuthenticationContext` (schema `auth`).
- Connection strings: `ConnectionStrings:PrformDatabase` (host) and `ConnectionStrings:AuthDatabase` (Cime.Auth). In production they come from Secret Manager; `appsettings.json` holds no real secrets.
- **DateTime**: `DateTime` maps to `timestamp without time zone` stored with `Kind=Unspecified` (`PostgresConventions.UseUnspecifiedDateTimes`, same behavior the old MySQL had — JSON stays without `Z`); `DateTimeOffset` maps to `timestamptz`. In Cime.Auth, dates are written with `DateTime.Now` (Npgsql rejects `Kind=Utc` in `timestamp` columns).
- **Text comparisons are case-sensitive** in PostgreSQL: when a query must ignore case, use `ToLower()` on both sides. Queries whose order matters need an explicit `OrderBy`.

### Authentication

Custom `X-API-Key` header scheme (JWT issued by Cime.Auth, validated with `Auth:Secret`) with role-based authorization. Roles: `admin`, `support`, `user`, `gestor`. Implemented in `Cime.BuildingBlocks.Security`. Cime.Auth itself uses Bearer JWT; its signing keys are `Auth:Secret` / `Auth:SecretRefresh` (configuration only — it refuses to start without them).

### Realtime

SignalR hub runs in the relay (`Cime.RealTime.Relay`, MonsterASP) in production (`RealTime:Mode=Relay`): the API publishes via HTTP and the browser connects with a short-lived token from `GET api/v1/RealTime/connection`. Development can use the in-process hub (`Mode=InProcess`, token-only).

### API Conventions

- Route pattern: `/api/v{version:apiVersion}/[controller]` (current version: 1.0)
- Controllers: PullRequest, Form, GitHub, Azure, AI, PluginConfiguration, Vacations, Timeline, Handover, UserIntegration, Teams, Image, RealTime
- Async/await throughout, repository pattern for data access

### Domain Patterns

Entities use DDD-style private setters with validation methods. Base interfaces: `IEntity<T>`, `IAuditableEntity`, `IDescribable`. Domain validation throws `DomainException`.

### Deployment

Docker images → Google Artifact Registry → **Google Cloud Run** (`us-central1`), via GitHub Actions on PR→master (`.github/workflows/deploy.yml`). Infrastructure (Cloud Run, Secret Manager, IAM) in Terraform under `deploy/terraform` (local state); DNS in `deploy/dns`. The realtime relay deploys to MonsterASP via Web Deploy (`deploy-realtime.yml`). Details and runbooks: `deploy/README.md`.

### Local development cautions

Running Cime.Auth locally with real SMTP settings sends real e-mails (registration, password reset) — keep `Email:Password` empty or point `Email:Host` to an invalid host. Integrations (Azure DevOps, GitHub, Teams, AI) use real credentials only if you configure them (user-secrets).
