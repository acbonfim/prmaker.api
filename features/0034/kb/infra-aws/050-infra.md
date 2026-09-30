## Contas e regiões
Conta **367983645102**, região **us-east-1**. Acesso aos bancos exige **VPN**.

## Bancos
| Recurso | Motor | Uso |
|---|---|---|
| `prod-solvacelabs`, `prod1-…`, `prod2-…`, `prod3-…`, `prod4-…` (+ `*-std-*`) | SQL Server | Global/Local dos clientes, agrupados por instância. Aliases da skill: `prod` (DEMO, INTR, MDLZ), `prod1` (Gerdau), `prod3` (TABUK, TEVA), `prod4` (ASTRAZ, LYNXEO, NOVAWATER). Descobrir: `SELECT name FROM sys.databases` |
| `test-solvacelabs`, `test1-…` | SQL Server | Ambientes de teste |
| `solvace-corporate`, `solvace-corporate-dev` | SQL Server | Camada Corporate |
| `solvace-pstgdev` / `solvace-pstgprd` (Aurora) | PostgreSQL | `KnowledgeCenter` (schema `knowledge_center`), `Multilingual`, `module_integration_<dev/qa/sandbox_revamp/stradal>`, `banner`, `stradal` |
| `db-audittrail` | DocumentDB | Trilha de auditoria |

## Mensageria e eventos
~**695 Lambdas**. Convenção: `<MÓDULO>_<Entidade>_EventCreated[_createevent]_<AMBIENTE>` (ex.: `ALR_Material_EventCreated_createevent_PRODUCTION`,
`COM_Team_EventCreated_EDGE`) — propagam cadastros (Material, Supplier, PhysicalLayout, Team, UDA, Failure, Classification…) entre
módulos. Ambientes nos nomes: DEVELOPMENT, EDGE, RELEASEVERSION, PRODUCTION. Algumas Lambdas via **SQS** (ex.: ActionPlan
RegisterDeleted). Dado "não sincronizou" entre módulos → procure a Lambda do evento e os logs dela.

## Execução e deploy
- Revamp: **ECS** (alvo EKS), imagens por módulo; **CodePipeline por branch** (development, edge, release-version, hotfix-version,
  master). Legado core: containers .NET Core 3.1 (ex.: `request_download_consumer`, Digital Obeya via CloudFormation `digital-obeya-ecs.yaml`).
- Logs: **CloudWatch** (`revamp-api-*`), Sentry nos módulos revamp.
- Segredos: **Secrets Manager** — `environment/<host>.solvacelabs.com` (configuração de cada ambiente/sandbox de cliente),
  `solvacetokensecrets/<amb>`, `solvaceemailsecrets/<amb>`, `corporate/<amb>`, `multilingual/<amb>`, `elasticsearch/post/<amb>`,
  `vsightintegration/<amb>`. Nunca copie valores para análises.
- Pacotes: **CodeArtifact** domínio `solvace`, repositório `revamp` (`aws codeartifact login --tool dotnet ...`).
- Outros: S3 (anexos/arquivos — `inc_linkAmazonS3.asp` no legado), OpenSearch (busca), Redis (cache), QLDB (building block).

## Hosts
Ambientes `https://<ambiente>.solvacelabs.com` (clientes e `sandbox<cliente>`, `devsp`); APIs `https://api-<módulo>-dev.solvacelabs.com`;
gateway `https://apigw-<amb>.solvacelabs.com`; KC `https://api-knowledge-center-dev.solvacelabs.com`.
