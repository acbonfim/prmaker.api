## Estrutura
| Pasta | O que é |
|---|---|
| `solvace-core/` (~8,6 mil arquivos) | Solução `ewcm-core.sln`: **um projeto web .NET Core 3.1 por módulo** (cada um com `Program.cs`/`Startup.cs`, Controllers, Views Razor), mais bibliotecas compartilhadas |
| `solvace-core/data_access` | EF Core: `Context/GlobalContext`, `LocalContext` (banco do site), `CorporateContext`, `MultilingualContext`, `ContextService` (troca a conexão por usuário/site); `Models/<modulo>/` (48 pastas) |
| `solvace-core/helpers` | Sessão (`Helpers/SessionHelper.cs`), cookies (`Helpers/infra/*CookieHelper.cs`), parâmetros globais |
| `solvace-core/view_shared` | Views/partials Razor compartilhadas (~2,8 mil arquivos, inclui assets) |
| `solvace-core/<workers>` | `request_download_consumer` (Dockerfile da raiz), `aws_lambda_app_sync_users`, `aws_lambda_opsearch_sync_docs`, `AWSLambda_Sap_DefectTag`, `defect_tag_sap_integration_service` |
| `solvace-asp/` (~6,5 mil) | ASP clássico: `default.asp`, `redirect.asp` (entrada/login), `_includes.asp`; `systems/<sigla>/` por módulo; `systems/includes/` (4,4 mil: libs, asp, js) ; `#database` (scripts SQL); `#only-office` |
| `solvace-angular`, `solvace-node`, `solvace-bot` | Resíduos pequenos (poucos arquivos) |

## Módulos do core (projetos em `ewcm-core.sln`)
action_plan · administration · assessment · centerline · checklist · common_app · complaint · connected_worker · contractor_management ·
control_tower · defect_tag · digital_obeya (+ `dfm`/`dfm-back`) · incident · lil · masterdata_global · non_conformity · scorecard ·
supplier_management · team · training · unsafe_condition · user · work_permit.

## Siglas do ASP (`solvace-asp/systems/`)
trn (treinamento?) · lup · graficos · ged (documentos) · qpl · masterdata · qma · melhorias · moc (gestão de mudança) · alertas · rpm ·
pjt (projetos) · soc · sa3 · kpi · caf · scg · application(_boards/_home) · pbk · bmk · times · mlg · ats — *significado das siglas
sem comentário: a confirmar pelo menu do sistema.*

## Branches
`hotfix/<card>`/`bugfix/<card>` a partir de `master`; fluxo de PRs (development, qa, release) vem do PRMake (`branches`).
