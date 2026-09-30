## Sessão compartilhada ASP ↔ .NET Core
- O login acontece pelo front/Cognito e pelo `redirect.asp`; a sessão é mantida por **cookies com nomes GUID** lidos pelos dois lados:
  - ASP: `systems/includes/asp/inc_auth.asp` — verifica os cookies de login do .NET Core (`hasNetCoreLoginCookies`), o cookie de
    última atividade e o **idle timeout** (cookie `399E3DB1…`), e redireciona quando expira.
  - Core: `Startup.cs` de cada módulo usa `AddSession` com cookie próprio (ex.: `.AspNetCore.Session.actionplan`) e, a cada
    requisição, `SessionCookieHelper`/`UserCookieHelper` recarregam a sessão pelo `userId` do cookie
    (`helpers/Helpers/SessionHelper.cs` → `UserSessionService.FindSessionUser` no Global).
  - `EnvironmentCookieHelper` guarda a URL do ambiente e a URL da API em cookies (`SetApiURLCookie`).
- Dados da sessão: userId, SSO (`SSO_ID`, `SSO_USERNAME`, federado), site de origem e `LAST_SITE_ID`, área, fornecedor/contratado.
- Deep link para telas do core passa por `BuildCoreDeepLinkLoginURL` (regex com timeout; se falhar, cai no fluxo do `redirect.asp`).

## Onde costuma quebrar
- **Loop de sessão/login** depois de horas logado (card 73821: loop após 8 h em usuários de "big screen") → cookies de atividade/idle
  e reload de sessão entre ASP e core.
- Usuário **federado (SSO) + nativo duplicados**, e-mail trocado no SSO → ver Cognito (`cognito-query.sh`) e `TB_WCM_USER`.
- Usuário sem planta/área ou `LAST_SITE_ID` apontando para site sem acesso → telas vazias.
