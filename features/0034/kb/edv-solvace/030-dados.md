## Bancos (SQL Server)
- **Global** por cliente/ambiente (`DB_<CLIENTE>_<AMB>_GLOBAL`, no QA `DB_SOLV_QA_GLOBAL`): usuários (`TB_WCM_USER`, preferências
  `TB_WCM_USER_PREFERENCE`, áreas `TB_WCM_USER_AREA`, fotos), sites, parâmetros globais, menus/sistemas (`TB_SYS_*`).
- **Local** por site (`DB_<CLIENTE>_<AMB>_LOCAL_<SITE>`): dados operacionais dos módulos (ex.: plano de ação `TB_ACP_PLAN`,
  `TB_ACP_PHASE`, `TB_ACP_STATUS`, `TB_ACP_TYPE`, `TB_ACP_SYSTEM`, `TB_ACP_PRIORITY`, donos `TB_ACP_PLAN_OWNER/_AREA_OWNER/_TEAM_OWNER`).
- **Corporate** (`solvace-corporate`): cross-BU.
- **Multilingual**: traduções (PostgreSQL `Multilingual` no Aurora e/ou contexto próprio).

## Como a conexão é escolhida
- Core: `data_access/Context/ContextService.cs` — `SetUserContext(local, global)` a partir da sessão; `GetDynamicLocalContext` para
  ler outro site. O **site ativo** do usuário decide o banco Local.
- ASP: `systems/includes/asp/all_conn.asp` monta `rsGlobal`, `rsConexao` (banco de `session("activeSiteInstanceName")`),
  `rsLocalSiteUser` e `rsDirectSite`. (Credenciais ficam nesse arquivo — nunca copie para análise/PR.)

## Consultas úteis (somente leitura, `sql-query.sh`)
- Usuário por nome: `SELECT TOP 50 USER_ID, USER_FULLNAME, USERNAME, EMAIL, SSO_ID, SSO_USERNAME, ACTIVE, LAST_SITE_ID FROM TB_WCM_USER WHERE USER_FULLNAME LIKE '%<parte>%'`
- Descobrir o host do cliente: `SELECT name FROM sys.databases` em cada alias (`prod`, `prod1`…`prod4`).
