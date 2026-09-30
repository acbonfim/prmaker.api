## Estrutura (`Solvace.ActionPlan/src`)
| Projeto | Conteúdo |
|---|---|
| `Solvace.ActionPlan.API` | Controllers: `ActionPlanController` (POST criar, `POST action-plans`, `GET metadata`, `DELETE {id}/comments`), `Audits*Controller` (grupo, prioridade, sistema, tarefa, papel do usuário), `AnalyticsController`, `CountersController`, `DigitalObeyaController`, `ScoreCardsController`, `PriorityController`, `SystemController`, `TypeController`, `ActionPlanKaiController` (integração com o KAI) |
| `Solvace.ActionPlan.Application` | `Commands/` (CreateActionPlan, AddShortActionPlan, Audits*, DeleteActionPlanComments), `Queries/` (contadores, analytics por status/tipo, subordinados, KPIs de incluídos/concluídos, drill-down, planos vinculados, listas de prioridade/sistema/tipo), `Hubs/KanbanHub` (SignalR) |
| `Solvace.ActionPlan.Application.Abstractions` | Contratos (Commands/Queries/Services/Enum) |
| `Solvace.ActionPlan.Infra.Data.Global.SqlServer` / `.Local.SqlServer` | Acesso aos bancos Global e Local (queries com `TB_ACP_*`, `TB_WCM_USER*`, `TB_SYS_*`) |
| `Lambda.ActionPlan.RegisterDeleted` | Lambda disparada por **SQS** (registro de exclusões) |
| `Lambda.ActionPlan.Application` | Lógica das Lambdas |

Building blocks usados: AwsLogging, Security, Swagger, Translate, Cache.Redis, DataAccess (+Migrations), **QLDB**, HttpContextAccessor.

## Onde está cada coisa ligada ao módulo
- Tela nova: `edv-solvace-apps/projects/action-plan` (`apiUrlActionPlan`).
- Versão legada: `edv-solvace/solvace-core/action_plan` (Razor: Board, Kanban, CustomKanban, Plan, Task, Report, Historic…) e
  API legada `ewcm-core-api/action_plan` + widget `dob_action_plan` (Digital Obeya). As **mesmas tabelas** `TB_ACP_*` servem os dois.
