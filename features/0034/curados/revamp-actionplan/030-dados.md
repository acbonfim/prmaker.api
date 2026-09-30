## Tabelas (banco Local do site, salvo indicação)
| Tabela | Papel |
|---|---|
| `TB_ACP_PLAN` | Plano de ação (a mais consultada) |
| `TB_ACP_PLAN_DRAFT` | Rascunho do plano |
| `TB_ACP_PHASE`, `TB_ACP_STATUS` | Fases e status (On time, Delayed, In approval, Completed, Cancelled — ver ART-23) |
| `TB_ACP_TYPE`, `TB_ACP_SYSTEM`, `TB_ACP_PRIORITY` | Cadastros de tipo, sistema e prioridade (configuráveis nas telas de Settings) |
| `TB_ACP_PLAN_OWNER`, `TB_ACP_PLAN_AREA_OWNER`, `TB_ACP_PLAN_TEAM_OWNER` | Responsáveis (pessoa, área, time) |
| `TB_WCM_USER`, `TB_WCM_USER_PREFERENCE`, `TB_WCM_USER_PICTURE`, `TB_WCM_USER_AREA` (Global) | Usuário, preferências, foto, áreas |
| `TB_SYS_U`, `TB_SYS_USERGRUPOS` | Permissões/grupos do sistema (legado) |

Colunas exatas: abrir as queries em `Infra.Data.Local.SqlServer/Queries` antes de escrever SQL (a confirmar por caso).
