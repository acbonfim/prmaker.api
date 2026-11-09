# 0092 — Status

| Fase | Status | Commits | Notas |
|---|---|---|---|
| B1 back (config, tipos, renderizador, migração, MCP) | em andamento | | |
| B2 skills (credenciais, validar-ambiente, guias) | pendente | | |
| F1 front (input, prévia, evidências) | em andamento | | agente no worktree prform-app-0092 |
| Q1 integração | pendente | | |

## Log
- 2026-11-08: spec/contrato/plano. Login dos ambientes: Angular `myworkspace/sign-in` com `#username-field`,
  `#password-field`, `#signin-button` (botão "log in with Solvace.com" = SSO). Catálogo de ambientes no corporativo:
  `CORPORATE_SOLVACE_DB.TB_WCM_ENVIRONMENT` (nome, URL, `CATALOG_GLOBAL`, SSO). Plantas no global:
  `TB_WCM_SITE` (`SITE_ACRONYM`, `INSTANCE_NAME` = banco local). Usuários: `TB_WCM_USER` (`USER_EMAIL`, `SITE_ID`, `ACTIVE`).
