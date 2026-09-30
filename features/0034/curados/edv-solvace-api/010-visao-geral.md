## Estrutura (`solvace-api/ewcm-core-api/`, solução `ewcm-core-api.sln`)
| Parte | O que é |
|---|---|
| `ApiGateway` | **Ocelot**: rotas em `appsettings*.json` (`UpstreamPathTemplate` → serviço de cada módulo; ~1300 rotas). Rota 404/502 estranha → comece aqui |
| `ApiAuthentication` (+ Tests) | Autenticação com **AWS Cognito** (`CognitoService`, chaves do pool por URL, `LoginInternalCommand`/`LoginExternalCommand`) |
| `<modulo>` (action_plan, administration, alert, centerline, checklist, communication, complaint, defect-tag, digital_obeya, document, ged, incident, kaizen, lil, lpp, moc, non_conformity, project, MasterData_Api…) | API .NET 6 por módulo: `Controllers/`, `BLL/`, `Services/`, `Interceptor/`, `Infrastructure/`, Dockerfile próprio |
| `dob_*` | Widgets do **Digital Obeya** (action_plan, calendar, checklist, defect_tag, document, gauge, graphbar, html, label, meeting, number, picture, register, reunion, sa3, status, table, team, trend, zwibbler) |
| `api_OpenSearch`, `api_feed`, `api_subtitle`, `api_toast`, `api_util` | Serviços transversais (busca, feed, legendas, notificações toast) |
| `ApiEntity`, `ApiHelpers`, `DataShared` | Entidades (~970 arquivos de tabelas/requests/responses) e utilitários compartilhados |
| `lambda_PivotTableMonitoring` | Lambda de monitoramento de tabela dinâmica |
| `docker-compose*.yml` | Subida local |

Frameworks: quase tudo `net6.0` (alguns projetos ainda `netcoreapp3.1`). Testes: `Solvace.Core.Test.*`, `actionplan.tests`.

## Relação com os outros mundos
O front Angular chama estas APIs pelas `apiUrl<Módulo>` quando o módulo ainda não foi para o revamp (ou para a parte legada dele —
ex.: `apiUrlDefectTagLegacy`). Usa os mesmos bancos Global/Local do legado.
