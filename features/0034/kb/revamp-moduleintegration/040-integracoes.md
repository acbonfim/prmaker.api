## Depende de

**Banco compartilhado**
- `revamp-digitalobeya` — usa tabelas TB_DOB_* (ex.: TB_DOB_WIDGET_GRAPHBAR_VALUE) (revamp-ModuleIntegration/Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.Infra.Data.Local.SqlServer/Queries/ObeyaLocalQueries.cs:80)
- `revamp-rca` — usa tabelas TB_SA3_* (ex.: TB_SA3_A3) (revamp-ModuleIntegration/Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.Infra.Data.Local.SqlServer/Queries/RootCauseLocalQueries.cs:30)
- `revamp-actionplan` — usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN) (revamp-ModuleIntegration/Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.Infra.Data.Local.SqlServer/Queries/ActionPlanLocalQueries.cs:53)
- `revamp-incident` — usa tabelas TB_ICD_* (ex.: TB_ICD_INCIDENT) (revamp-ModuleIntegration/Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.Infra.Data.Local.SqlServer/Queries/IncidentsLocalQueries.cs:27)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `MODULE_INTEGRATION_RCA_WORKER_LOCAL` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.RCA.Worker/Worker.cs:34)
- `MODULE_INTEGRATION_RCA` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.RCA.Worker/Worker.cs:36)
- `MODULE_INTEGRATION_ICD_WORKER_LOCAL` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.INC.Worker/Worker.cs:33)
- `MODULE_INTEGRATION_ICD` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.INC.Worker/Worker.cs:35)
- `MODULE_INTEGRATION_ACP_WORKER_LOCAL` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.ACP.Worker/Worker.cs:33)
- `MODULE_INTEGRATION_ACP` (Solvace.ModuleIntegration/src/Solvace.ModuleIntegration.ACP.Worker/Worker.cs:35)
