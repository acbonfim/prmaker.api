## Estrutura
- `projects/<app>` — um app por módulo (builder `browser` na maioria; `application` em action-plan, center-line, cil,
  digital-obeya, knowledge-center). Apps: action-plan · adm · alert · authentication · bos · center-line · cil · communication ·
  copilot · defect-tag · digital-obeya · external-features · feed · knowledge-center · lpp · multilingual · myworkspace ·
  notification · praise · quiz · rca · scorecard · survey · unsafe-condition · users.
- Libs: `lib-shared-core` (auth, `AuthInterceptor`, `AuthCheckService`, loading), `lib-shared-component` (UI), `lib-shared-feature`
  (features compartilhadas, a maior), `lib-shared-business-component`, `lib-shared-common`, `lib-shared-icons`, `zard-components`,
  `lib-storybook`.
- `projects/environments/environment.<amb>.ts` (local, qa, edge, rc, rt, prod): **mapa de URLs das APIs** (`apiUrlActionPlan`,
  `apiUrlAuth`, `apiUrlBOS`, `apiUrlCil`, `apiUrlDefectTag` / `apiUrlDefectTagLegacy`, `apiUrlLpp`, `apiUrlMultilingual`,
  `apiUrlNotification`… ~50). Ex. QA: auth `https://apigw-qa.solvacelabs.com/authentication`, action plan
  `https://api-actionplan-dev.solvacelabs.com`.

## Como achar o código de uma tela
URL do navegador → app (`projects/<app>`) → rota → componente → service → qual `apiUrl<Módulo>` chama → backend (revamp ou
ewcm-core-api). Componentes compartilhados (tabelas, filtros, uploads) quase sempre vêm de `lib-shared-*` — mudança ali afeta
todos os apps.

## Autenticação no front
`lib-shared-core/src/lib/auth/`: login no **Cognito do ambiente** (Amplify), `AuthInterceptor` adiciona o token (`Authorization:
Bearer`) e bloqueia requisições quando a sessão é inválida (`AuthCheckService`); requisições com o cabeçalho de "não autorizado"
ou "não coletar" são tratadas à parte.

## Qualidade
Jest (`jest --no-cache`), Playwright (e2e), ESLint, SonarCloud (`sonar-project.properties`). Tradução com Transloco.
