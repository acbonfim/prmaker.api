## Depende de

**Banco compartilhado**
- `revamp-users` — usa tabelas TB_CAF_* (ex.: TB_CAF_CARGO) (revamp-Authentication/Solvace.Authentication/src/Solvace.Authentication.Infra.Data.Local.SqlServer/Queries/GetLastEmployeePositionInfoByUserIdLocalQuery.cs:29)

**Serviço externo**
- `ext:cognito` — AWS Cognito: https://cognito-idp. (código) (revamp-Authentication/Solvace.Authentication/src/Solvace.Authentication.Application/Services/Mcp/CognitoOAuthClient.cs:112)


## Usado por

- `revamp-copilot` — HTTP: chama https://api-authentication-<amb>.solvacelabs.com (Mcp:AuthorizationServerUri)
