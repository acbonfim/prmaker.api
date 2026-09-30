## Peças
| Onde | Como |
|---|---|
| **Cognito** | Um **user pool por ambiente**, com o nome do ambiente (`demo`, `qa`, `takeda`, `sandboxtakeda`…). Atributo `custom:environment`. Consulta: `cognito-query.sh pools|user|groups <ambiente> …` (somente leitura) |
| Front (`edv-solvace-apps`) | Amplify + `lib-shared-core/src/lib/auth` (`AuthInterceptor` → `Authorization: Bearer`, `AuthCheckService`) |
| ewcm-core-api | `ApiAuthentication`: `CognitoService`, chaves do pool por URL, login interno/externo |
| Revamp | `revamp-Authentication` (Corporate tier); demais módulos validam o token (`BuildingBlocks.AspNetCore.Security`) |
| Legado | Sessão por cookies GUID compartilhados ASP ↔ core — ver `edv-solvace` › Login e permissões |
| Usuário | `TB_WCM_USER` (Global): `USERNAME`, `EMAIL`, `SSO_ID`, `SSO_USERNAME`, `FEDERATED`, `ACTIVE`, `LAST_SITE_ID`, tipo, área |

## Diagnóstico rápido (padrão C da skill)
1. Achar o usuário no banco por **pedaço do sobrenome** (`LIKE`) → e-mail/SSO exatos.
2. Cognito do ambiente: `UserStatus` (`FORCE_CHANGE_PASSWORD`, `UNCONFIRMED`), `Enabled`, grupos, `custom:*`.
3. Duplicidade **federado + nativo**, e-mail trocado no IdP do cliente, usuário inativo, sem planta/área, `LAST_SITE_ID` sem acesso.
4. Loop após horas logado → cookies de sessão/idle (card 73821).
A skill **não escreve** no Cognito: o ajuste vira etapa do usuário com o passo a passo.
