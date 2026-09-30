## Padrão de um módulo (vale para todos — base: claude-global `revamp/architecture.md`, 2026-04)
Projetos em `Solvace.<Módulo>/src/`: `.API` (controllers, `/health`) → `.Application.Abstractions` (contratos) → `.Application`
(handlers MediatR, validators, services) → `.Domain` (entidades, regras) ; `.Infra.Data.Global.SqlServer`, `.Infra.Data.Local.SqlServer`
(e `.Corporate` só em Users/Authentication) ; opcionais `.Integration.*`, `Lambda.*`/`.Worker`. Pastas de caso de uso:
`UseCases/<Entidade>/<Ação>/` (BOS, LPP) ou `Commands|Queries/<...>` (RCA, ActionPlan).
- Building blocks: pacotes NuGet `Solvace.BuildingBlocks.*` no **AWS CodeArtifact** (fonte fora destes repos).
- Logs: **Sentry** + `BuildingBlocks.AspNetCore.AwsLogging` (CloudWatch `revamp-api-*`). Não é Serilog.
- Autenticação delegada ao `revamp-Authentication`; os módulos validam o token. Resolução de tenant **duplicada nos handlers**
  (não há pipeline behavior).
- Dados: SQL Server em 3 camadas (Global/Corporate/Local); alvo futuro PostgreSQL. Cross-tier não faz JOIN — composição na aplicação.
- Deploy: AWS ECS (us-east-1) por **CodePipeline por branch**; CI no GitHub Actions (`pull.yml`: build, testes, SonarCloud bloqueante).
- Branches: `feature|bugfix|hotfix/*` → `development` → `release-candidate` → `release-version` → `master`; também `edge`,
  `hotfix-version`, `release-version2`. Commits `AB#<card>`.

## Onde está cada módulo
| Repositório próprio (`electradv/…`) | Última atividade local |
|---|---|
| revamp-ActionPlan (ver projeto `revamp-actionplan`) | 2026-06 |
| revamp-Administration · revamp-CIL · revamp-CustomField | 2026-05 |
| revamp-Authentication | 2026-04 |
| revamp-BOS | 2026-08 |
| revamp-Checklist · revamp-Hashtag | 2026-01 |
| revamp-Complaint · revamp-LPP · revamp-Multilingual · revamp-ScoreCard | 2026-02 |
| revamp-KnowledgeCenter (base de conhecimento; Aurora PostgreSQL `KnowledgeCenter`, schema `knowledge_center`) | 2026-09 |
| revamp-Post | 2026-06 |
| revamp-UnsafeCondition · revamp-Users | 2026-09 |

**Monorepo `electradv/revamp`** (`modules/Solvace.<X>`, clone local de 2025-10 — pode estar desatualizado): ActionPlan, Administration,
Alert, Assessment, Authentication, BOS, Bookmark, CIL, Centerline, Checklist, Comment, CommentManager, Communication, Complaint,
Copilot, CustomField, DefectTag, DigitalObeya, Documentation, Documents, Fishbone, Gamification, Hashtag, ImageResizer, Incident,
Kaizen, MasterData, Moc, Multilingual, NonConformity, Notification, Post, Praise, Project, Quiz, Reaction, Subtitle, Survey,
Training, Users, Views, WhyWhy, WorkPermit. Módulo com repo próprio: **vale o repo próprio**.

Localizar código: `revamp-repos.sh where <Módulo>` / `grep <padrão> <Módulo>` (skill analisar-bug).
