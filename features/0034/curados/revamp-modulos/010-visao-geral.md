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

## Onde está cada módulo (53 repositórios `electradv/revamp-*`, clonados em 2026-09-30 no `kb-mirror`)
Cada linha tem projeto próprio nesta base (`000-projeto.md` com dependências e evidências). `⇄` = depende de / usado por.
O **monorepo `electradv/revamp`** (clone de 2025-10) está obsoleto: todo módulo tem repo próprio — vale o repo próprio.

| Projeto | O que é | Tipo | Último commit | ⇄ |
|---|---|---|---|---|
| `revamp-actionplan` | Plano de ação (revamp .NET 8, hexagonal + MediatR). | revamp | 2026-09-25 | 5/5 |
| `revamp-administration` | Módulo de administração do estate Revamp, responsável pelo gerenciamento de dados corporativos, globais e locais via SQL Server, além de Aurora… | revamp | 2026-09-26 | 1/0 |
| `revamp-alert` | Modulo responsavel pela criacao, gerenciamento e notificacao de alertas no estate Revamp. | revamp | 2026-09-25 | 4/0 |
| `revamp-assessment` | Módulo responsável pelo gerenciamento de assessments no estate Revamp. | revamp | 2026-09-25 | 0/3 |
| `revamp-audittrail` | Repositório sem código de aplicação (só README/template ou documentação). | infra | 2026-08-24 | 0/0 |
| `revamp-authentication` | Serviço de autenticação do estate Revamp, responsável por autenticação de usuários via AWS Cognito, segurança JWT, gerenciamento de sessão, geração… | revamp | 2026-09-25 | 2/1 |
| `revamp-bos` | Business Object System (BOS) é o hub central de dados mestres e configuração do estate Revamp. | revamp | 2026-09-26 | 3/0 |
| `revamp-bookmark` | 6 projetos (net8.0), 4 controllers/8 rotas, 8 casos de uso, 0 Lambdas; tabelas TB_WCM_USER, TB_WCM_SITE, RegistryBookmarkId, RegistryBookmarkUid,… | revamp | 2026-09-25 | 0/0 |
| `revamp-buildingblocks` | Repositório de bibliotecas reutilizáveis (BuildingBlocks) do estate Revamp. | infra | 2026-09-28 | 16/0 |
| `revamp-cil` | CIL (Cleaning, Inspection & Lubrication) é o módulo responsável por gerenciar rotinas CIL, layouts físicos, equipamentos, empreiteiros, fornecedores,… | revamp | 2026-09-25 | 0/3 |
| `revamp-centerline` | 6 projetos (net8.0), 3 controllers/4 rotas, 3 casos de uso, 0 Lambdas; tabelas TB_SITE, TB_CLN_CHECKLIST, TB_WCM_SITE, TB_CLN_INSPECTION. | revamp | 2026-09-25 | 0/4 |
| `revamp-checklist` | 6 projetos (net8.0), 4 controllers/10 rotas, 8 casos de uso, 0 Lambdas; tabelas TB_CHK_CHECKLIST_OWNER, TB_CHK_CHECKLIST_AREA_OWNER,… | revamp | 2026-09-25 | 0/4 |
| `revamp-comment` | Modulo responsavel pelo gerenciamento de comentarios no estate Revamp. | revamp | 2026-09-25 | 5/0 |
| `revamp-commentmanager` | Gerencia comentários em todo o estate Revamp, incluindo processamento de menções (via worker dedicado), notificações para Microsoft Teams (Lambda),… | revamp | 2026-09-25 | 23/4 |
| `revamp-communication` | Module responsible for managing communications within the Solvace Revamp estate. | revamp | 2026-09-25 | 2/0 |
| `revamp-complaint` | 7 projetos (net8.0), 2 controllers/3 rotas, 4 casos de uso, 0 Lambdas; tabelas TB_CMP_COMPLAINT, TB_WCM_USER_ROLE, TB_CMP_STATUS. | revamp | 2026-09-25 | 0/3 |
| `revamp-copilot` | 12 projetos (net8.0), 10 controllers/35 rotas, 1 casos de uso, 3 Lambdas; tabelas Routine, RoutineExecution, RoutineInstructionExecution,… | revamp | 2026-09-02 | 2/0 |
| `revamp-customfield` | Gerencia campos customizados no estate Revamp. | revamp | 2026-09-25 | 0/0 |
| `revamp-datalake` | 12 projetos (net8.0), 4 controllers/6 rotas, 0 casos de uso, 0 Lambdas; tabelas TB_DTL_ENVIRONMENT, TB_DTL_SNAPSHOT_RUN, TB_DTL_MODULE,… | infra | 2026-09-29 | 0/0 |
| `revamp-defecttag` | Módulo responsável pelo gerenciamento de tags de defeitos no estate Revamp. | revamp | 2026-09-25 | 3/2 |
| `revamp-digitalobeya` | Módulo de Digital Obeya que fornece salas estilo dashboard com widgets configuráveis. | revamp | 2026-09-25 | 0/7 |
| `revamp-documentation` | 6 projetos (net8.0), 4 controllers/5 rotas, 2 casos de uso, 0 Lambdas; tabelas TB_SITES, TB_GED_DOCUMENT, TB_GED_DOCUMENT_APPROVAL, TB_GED_APPROVER,… | revamp | 2026-09-25 | 0/4 |
| `revamp-documents` | 9 projetos (net8.0), 2 controllers/9 rotas, 9 casos de uso, 0 Lambdas; tabelas TB_WCM_USER, TB_WCM_SITE, TB_SYS_APPLICATION,… | revamp | 2026-09-25 | 0/0 |
| `revamp-fishbone` | Módulo de diagramas de Fishbone (Ishikawa) para análise de causa raiz, suportando criação e gerenciamento de diagramas. | revamp | 2026-09-25 | 2/0 |
| `revamp-gamification` | 6 projetos (net8.0), 1 controllers/1 rotas, 1 casos de uso, 0 Lambdas. | revamp | 2026-09-25 | 0/0 |
| `revamp-hashtag` | Módulo de gerenciamento de hashtags do estate Revamp, responsável pela criação, consulta e associação de hashtags. | revamp | 2026-09-25 | 1/1 |
| `revamp-incident` | 6 projetos (net8.0), 2 controllers/3 rotas, 3 casos de uso, 0 Lambdas; tabelas TB_ICD_INCIDENT, TB_WCM_USER_ROLE, TB_ICD_STATUS. | revamp | 2026-09-25 | 0/4 |
| `revamp-kaizen` | 6 projetos (net8.0), 6 controllers/15 rotas, 16 casos de uso, 0 Lambdas; tabelas TB_MLH_MELHORIAS, TB_MLH_MELHORIAS_CATEGORIA_CUSTO, TB_MLH_STATUS,… | revamp | 2026-09-25 | 1/5 |
| `revamp-knowledgecenter` | 7 projetos (net10.0), 0 controllers/0 rotas, 0 casos de uso, 0 Lambdas; tabelas TB_WCM_SITE. | revamp | 2026-09-25 | 0/0 |
| `revamp-lpp` | Lesson Practice Plan (LPP) — módulo de gestão de planos de prática de aprendizado, com suporte a campos customizados, exportação Excel/PDF,… | revamp | 2026-09-25 | 3/4 |
| `revamp-masterdata` | Módulo responsável pelo gerenciamento de dados mestre do estate Revamp, centralizando referências fundamentais como unidades, departamentos, áreas,… | revamp | 2026-09-29 | 1/9 |
| `revamp-moc` | Module of Change (MOC) — gerencia e controla mudanças em processos, equipamentos ou sistemas. | revamp | 2026-09-25 | 0/2 |
| `revamp-moduleintegration` | Módulo responsável pelas integrações cross-module no estate Revamp. | infra | 2026-09-25 | 4/0 |
| `revamp-multilingual` | Servico responsavel pela gestao de traducoes e termos multilinguais no estate Revamp. | revamp | 2026-09-25 | 0/0 |
| `revamp-nonconformity` | 6 projetos (net8.0), 2 controllers/3 rotas, 3 casos de uso, 0 Lambdas; tabelas TB_NCF_NONCONFORMITY, TB_NCF_PARTICIPANTS, TB_NCF_STATUS,… | revamp | 2026-09-25 | 0/3 |
| `revamp-notification` | Serviço de notificações responsável por entregar notificações via e-mail (AWS Secrets Manager + Infra.Mail), push (Firebase/FirebaseAdmin), tempo… | revamp | 2026-09-25 | 0/17 |
| `revamp-post` | Módulo de feed social do estate Revamp, responsável pela criação de posts, interações (reações, comentários), reclamações, bloqueio de usuários e… | revamp | 2026-09-25 | 7/6 |
| `revamp-praise` | 10 projetos (net8.0), 3 controllers/18 rotas, 15 casos de uso, 2 Lambdas; tabelas Praise, TB_WCM_USER, Title, PraiseTeam, PraiseUser. | revamp | 2026-09-25 | 2/0 |
| `revamp-project` | 5 projetos (net8.0), 2 controllers/4 rotas, 6 casos de uso, 0 Lambdas; tabelas TB_PJT_TYPE, TB_PJT_PROJECT, TB_PJT_STATUS, TB_PJT_PROJECT_MEMBER,… | revamp | 2026-09-25 | 0/3 |
| `revamp-quiz` | Módulo de Quiz do estate Revamp. | revamp | 2026-09-25 | 3/0 |
| `revamp-rca` | Módulo de Root Cause Analysis (RCA). | revamp | 2026-09-25 | 10/5 |
| `revamp-reaction` | Módulo responsável pelo gerenciamento de reações (emoji/like) em itens de conteúdo ao longo do estate Revamp. | revamp | 2026-09-25 | 4/0 |
| `revamp-scorecard` | Módulo de ScoreCard/KPI do estate Revamp, responsável pelo gerenciamento de scorecards e indicadores de desempenho. | revamp | 2026-09-25 | 1/0 |
| `revamp-subtitle` | Módulo responsável por gerar, gerenciar e traduzir legendas para conteúdos de mídia, utilizando os serviços AWS Transcribe e AWS Translate por meio… | revamp | 2026-09-25 | 19/0 |
| `revamp-survey` | Módulo de Survey (pesquisas/questionários) do estate Revamp. | revamp | 2026-09-25 | 2/1 |
| `revamp-training` | 8 projetos (net8.0), 5 controllers/5 rotas, 6 casos de uso, 0 Lambdas; tabelas TB_TRN_PRIORITY, TB_TRN_STUDENT_TRAINING, TB_CAF_FUNCIONARIO_FUNCAO,… | revamp | 2026-09-25 | 1/3 |
| `revamp-unsafecondition` | Módulo responsável pelo gerenciamento de condições inseguras (UNC) no estate Solvace Revamp. | revamp | 2026-09-26 | 3/2 |
| `revamp-users` | Módulo central de gerenciamento de usuários do estate Revamp, responsável pelo ciclo de vida de usuários, papéis, times, layout físico… | revamp | 2026-09-30 | 1/20 |
| `revamp-views` | Módulo responsável pelo gerenciamento de Views no estate Revamp. | revamp | 2026-09-25 | 0/0 |
| `revamp-whiteboard` | Módulo WhiteBoard responsável por fornecer funcionalidades de quadro branco colaborativo, com suporte a armazenamento de arquivos via S3, acesso a… | revamp | 2026-09-25 | 1/0 |
| `revamp-whywhy` | WhyWhy implementa a análise dos 5 Porquês (5 Whys) para identificação de causa raiz via questionamento iterativo. | revamp | 2026-09-25 | 3/1 |
| `revamp-workpermit` | Módulo de gerenciamento de Work Permits (Permissões de Trabalho). | revamp | 2026-09-25 | 0/3 |
| `revamp-wiki` | Wiki do revamp mantida por LLM (padrão LLM Wiki): uma página por módulo em wiki/modules (descrição, building blocks e versões, eventos) e por… | infra | 2026-06-30 | 0/0 |

Localizar código: `revamp-repos.sh where <Módulo>` / `grep <padrão> <Módulo>` (skill analisar-bug).
