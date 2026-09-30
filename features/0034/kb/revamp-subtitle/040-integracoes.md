## Depende de

**Fila (SQS)**
- `revamp-notification` — envia para a fila NOTIFICATION_WORKER_<amb> (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Services/NotificationService.cs:26)

**Banco compartilhado**
- `revamp-lpp` — usa tabelas TB_LUP_* (ex.: TB_LUP_DOCUMENT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:88)
- `revamp-checklist` — usa tabelas TB_CHK_* (ex.: TB_CHK_INSPECTION) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:181)
- `revamp-actionplan` — usa tabelas TB_ACP_* (ex.: TB_ACP_PLAN) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:11)
- `revamp-defecttag` — usa tabelas TB_DFT_* (ex.: TB_DFT_DEFECT_TAG) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:21)
- `revamp-nonconformity` — usa tabelas TB_NCF_* (ex.: TB_NCF_NONCONFORMITY) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:31)
- `revamp-workpermit` — usa tabelas TB_WRK_* (ex.: TB_WRK_WORK_PERMIT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:41)
- `revamp-unsafecondition` — usa tabelas TB_UNC_* (ex.: TB_UNC_UNSAFE_CONDITION) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:51)
- `revamp-kaizen` — usa tabelas TB_MLH_* (ex.: TB_MLH_MELHORIAS) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:61)
- `revamp-digitalobeya` — usa tabelas TB_DOB_* (ex.: TB_DOB_WIDGET) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:74)
- `revamp-rca` — usa tabelas TB_SA3_* (ex.: TB_SA3_A3) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:109)
- `revamp-documentation` — usa tabelas TB_GED_* (ex.: TB_GED_DOCUMENT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:119)
- `revamp-project` — usa tabelas TB_PJT_* (ex.: TB_PJT_PROJECT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:129)
- `revamp-complaint` — usa tabelas TB_CMP_* (ex.: TB_CMP_COMPLAINT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:139)
- `revamp-incident` — usa tabelas TB_ICD_* (ex.: TB_ICD_INCIDENT) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:149)
- `revamp-assessment` — usa tabelas TB_AST_* (ex.: TB_AST_INSPECTION) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:162)
- `revamp-cil` — usa tabelas TB_LIL_* (ex.: TB_LIL_NINSPECTION) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:209)
- `revamp-centerline` — usa tabelas TB_CLN_* (ex.: TB_CLN_INSPECTION) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:228)
- `revamp-training` — usa tabelas TB_TRN_* (ex.: TB_TRN_STUDENT_TRAINING) (revamp-Subtitle/Solvace.Subtitle/src/Solvace.Subtitle.Application/Helpers/LegacyConfigurationsService.cs:256)


## Usado por

_Nenhum outro módulo mapeado depende deste._

## Filas/tópicos citados no código
- `TRANSCRIPTION_WORKER` (Solvace.Subtitle/src/Solvace.Subtitle.Application/Commands/MessageBroker/MessageBrokerProvider.cs:14)
- `NOTIFICATION_WORKER` (Solvace.Subtitle/src/Solvace.Subtitle.Application/Services/NotificationService.cs:26)
