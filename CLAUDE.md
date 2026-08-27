# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CIME (Client Integration and Management Ecosystem) — a .NET 8.0 modular monolith API for pull request management, integrating with GitHub, Azure DevOps, and AI providers. Written in C# with nullable reference types and implicit usings enabled.

## Build & Run Commands

```bash
# Build entire solution
dotnet build Solvace.Master.sln

# Run the main API
dotnet run --project CIME/modules/Solvace.PullRequests/src/solvace.prform.api

# Restore dependencies
dotnet restore Solvace.Master.sln

# Add EF migration (from repo root)
dotnet ef migrations add <MigrationName> --context DefaultContext \
  --project CIME/modules/Solvace.PullRequests/src/solvace.prform.infra \
  --startup-project CIME/modules/Solvace.PullRequests/src/solvace.prform.api

# Apply migrations
dotnet ef database update --context DefaultContext \
  --startup-project CIME/modules/Solvace.PullRequests/src/solvace.prform.api

# Vacation module migrations use VacationContext instead of DefaultContext

# AWS CodeArtifact login (for private NuGet packages)
aws codeartifact login --tool dotnet --repository revamp --domain solvace --domain-owner 367983645102 --region us-east-1
```

No test projects exist in this repository currently.

## Architecture

### Module Structure

All modules live under `CIME/modules/`. Each follows clean architecture layers: **domain → application → infra → api**.

| Module | Responsibility |
|---|---|
| **Solvace.PullRequests** | Core module — the API host, PR registration, forms, plugins |
| **Solvace.GitHub** | GitHub API integration for PR operations |
| **Solvace.Azure** | Azure DevOps integration |
| **Solvace.AI** | AI provider abstraction (Gemini, OpenAI, Claude) |
| **Solvace.Vacations** | Vacation request and balance management |
| **Cime.BuildingBlocks** | Shared cross-cutting concerns (auth, CORS, caching, Swagger, exception handling, extensions) |

### Entry Point & DI

`Program.cs` in `solvace.prform.api` is the host. Each module registers itself via an `AddXxxModule()` extension method (e.g., `AddGitHubModule`, `AddAIModule`). Building blocks register via similar extensions (`AddSecurityAuth`, `AddCorsPolice`, `AddCacheService`, `AddSwaggerConfig`).

### Databases

- **DefaultContext** (MySQL via Pomelo) — PR data, forms, plugins, vacations. Auto-migrates on startup.
- **AuthenticationContext** (SQL Server) — User authentication. Auto-migrates on startup.

### Authentication

Custom `X-API-Key` header scheme with role-based authorization. Roles: `admin`, `support`, `user`, `gestor`. Implemented in `Cime.BuildingBlocks.Security`.

### API Conventions

- Route pattern: `/api/v{version:apiVersion}/[controller]` (current version: 1.0)
- Controllers: PullRequest, Form, GitHub, Azure, AI, PluginConfiguration, Vacations
- Async/await throughout, repository pattern for data access

### Domain Patterns

Entities use DDD-style private setters with validation methods. Base interfaces: `IEntity<T>`, `IAuditableEntity`, `IDescribable`. Domain validation throws `DomainException`.

### Deployment

Docker multi-stage builds → Google Artifact Registry → Google Cloud Run. CI/CD via Bitbucket Pipelines with QA and production (master) branches.
