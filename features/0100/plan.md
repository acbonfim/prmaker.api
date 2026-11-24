# Plano 0100 — Aba AWS na Bancada + layout (abas/horas em linhas próprias, Execução em tela cheia)

Branches `feature/0100` em `prform.api-0100` (back, skills, executor) e `prform-app-0100` (front), a partir da
origin/master. Frentes em paralelo: **B** (API), **S** (script/skill + executor), **F** (front); **Q** no fim. Os
contratos abaixo são a fonte da verdade entre as frentes.

## Decisões

- **A API não fala com a AWS** (Cloud Run, sem credencial). Quem coleta é a **máquina** (CLI da AWS do usuário) por um
  script **determinístico, somente leitura**, sem sessão do Claude (sem tokens): `skills/aws-deploy/scripts/aws.sh`
  (bash → `aws_deploy.py`). Ele lê o contexto do card no PRMake, consulta AWS + git local e grava o retrato no PRMake.
- **Três gatilhos** para a mesma coleta:
  1. botão **Atualizar** na aba → pedido `AwsRefresh` na fila própria (padrão da promoção 0074: laço separado no
     executor, não ocupa a pista do Claude nem o "um pedido ativo por card"). Roda **no executor de quem clicou**
     (regra 78723 — nunca no do dono do plano);
  2. sub-tarefa automática **`aws-deploy-map`** nos itens que mexem em código (executada pelo executor do item; a skill
     `executar-item-de-trabalho` só chama o script e conclui com o resumo);
  3. manual: `bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card>` (ou `/aws-deploy <card>`).
- **Acompanhar o deploy**: retrato com ambiente em `deploying` faz o servidor reagendar a coleta (mesmo executor)
  a cada 2 min, até 15 vezes ou até nenhum ambiente estar em andamento.
- **Índice de pipelines compartilhado e incremental** (pedido: "pipes novas são criadas, ele deve atualizar a lista
  sempre"): toda coleta faz `list-pipelines` (nome + `updated`), compara com o índice do PRMake, busca
  `get-pipeline` só das novas/alteradas e manda as removidas. Primeira carga ~6 min (1.078 pipes); depois segundos.
- **Ambiente → pipelines** (descoberto, nada fixo por repositório):
  - cada ambiente da config tem `host`; o script resolve no CloudFront o **tier** (origens S3 `<app>-<tier>.solvacelabs`
    → dev|qa|edge|rc|rt|prod) e os **Elastic Beanstalk** do host (origens `*.elasticbeanstalk.com`);
  - pipeline com deploy em Elastic Beanstalk pertence ao ambiente cujo EB bate (legado: `core26-homologation` →
    `SandboxEfesoNew-Solvacelabs` → sandboxefeso); a situação vem da **ação** de deploy daquele EB;
  - sem EB: pertence ao ambiente de mesmo tier (sufixo do nome ∈ tiers, ou `ENV` do CodeBuild) — revamp front/back;
  - `tier` explícito na config vence a descoberta (fallback se o CloudFront negar).
- **Pipelines do card** = fonte `FullRepositoryId` = repositório do card **e** (se a pipeline tem `filePaths`) algum
  arquivo alterado pelo card casa com os `includes` e não com os `excludes` (monorepo edv-solvace-apps).
- **"Nosso código"** = commits do card no repositório: SHAs conhecidos (entregas de código 0091, cabeças das branches
  dos PRs) + commits cujo texto cita o card (`AB#<card>`, `/<card>`, `-<card>-`, regex `(^|\D)<card>(\D|$)`) desde o
  1º commit do card − 1 dia. No repositório local (pasta pelo mapa `repos path`, `git fetch` das branches dos
  ambientes):
  - `inBranch` = algum commit do card é ancestral de `origin/<branch da pipeline>` ou há commit citando o card nela;
  - `deployed` = o mesmo teste contra o **commit implantado** (último `Succeeded` da pipeline/ação).
- **Veredito por ambiente** (o pior das pipelines do card naquele ambiente, nesta ordem): `failed` (execução com o
  nosso código falhou e nenhuma posterior passou) · `deploying` (em andamento com o nosso código) · `waiting` (na
  branch, ainda não implantado) · `absent` (não está na branch) · `deployed` (todas as pipelines do card implantaram
  o nosso código) · `not-applicable` (o repositório não vai para esse ambiente) · `unknown` (sem permissão/erro).
  Texto fixo para a tela: "Nosso código está em QA" / "Ainda não está em Sandboxefeso" / "Na branch de QA, aguardando
  o deploy" / "Deploy em andamento em QA" / "Deploy falhou em QA" / "Não se aplica".
- **Além das esteiras** (só o que a conta deixa ler; negado → `denied` e segue):
  - **Logs**: grupos `/aws/<host-curto>/<módulo>` (case-insensitive) do ambiente + erros das últimas 24 h
    (`filter-log-events` com `?ERROR ?Exception ?Error`, máx. 20 por grupo, mensagens cortadas em 600 caracteres) e o
    fim do log do **build que falhou** (`/aws/codebuild/<projeto>`, 40 linhas);
  - **Lambdas** do módulo (nome contém o token do módulo; nome, runtime, última modificação), **alarmes** em ALARM
    que citam módulo/tier, a **distribuição CloudFront** do host;
  - ECS/EKS/ECR negados nesta conta: aparecem em `denied`.
  - **Nunca**: `get-secret-value`, `get-parameter --with-decryption`, nada que grave.
- Layout (front): abas numa linha inteira de `.wb-main` (até o Especialista); horas + situação + sugestões numa linha
  própria abaixo; a seção Execução inteira (seletor do item + aviso + plano) ganha botão **tela cheia** com o
  `FullscreenPanel` (mesma animação do Card/Linha do tempo) — o botão de tela cheia interno do plano some na Bancada
  (um só botão).

## Contratos (API) — controller `AwsDeployController`, rota `api/v1/AwsDeploy`

Leitura para todo usuário autenticado; escrita do script com a api-key do usuário (ou credencial do executor — incluir
`AwsDeploy` na regex de `ExecutorCredentials`).

- `GET AwsDeploy/config` → `{ environments: [{ key, label, host, tier?, order }], refreshFollowMinutes: 2,
  refreshFollowMax: 15 }` (chave `AwsDeployEnvironments` do "Skills Configurations"; seed: dev/DEV/devsp,
  qa/QA/qa, sandboxefeso/Sandboxefeso/sandboxefeso, sandboxrt/SandboxRT/sandboxrt, prod/Produção/efeso; entrada no
  `PluginSettingsCatalog`).
- `GET AwsDeploy/pipelines/index` → `{ updatedAt, items: [{ name, updated }] }` (só nome + `updated` da AWS: o script
  decide o que buscar).
- `POST AwsDeploy/pipelines/index` → body `{ account, region, upserts: [AwsPipelineDto], removed: [name],
  full: bool }` → `{ total, upserted, removed }`. `full=true` remove o que não veio.
  `AwsPipelineDto = { name, updated, type, repositories: [{ repository, branch, action }], filePathsInclude: [..],
  filePathsExclude: [..], tier?, codebuildEnv?, deployTargets: [{ action, stage, provider, target }],
  stages: [string] }` (≤ 16 KB cada).
- `GET AwsDeploy/pipelines?repository=electradv/edv-solvace-apps` → `AwsPipelineDto[]` (índice do repositório).
- `GET AwsDeploy/card/{card}/context` → `{ card, title, repositories: [{ repository, branches: [..], shas: [..],
  prs: [{ number, base, head, status, url }] }], markers: ["AB#78720", ...], environments: [...config...] }`
  — de PRs do registro (`ListByCard`), entregas `pr|branch|commit|code` dos itens (0091) e regras (`PlanForRepository`
  para as branches de destino).
- `PUT AwsDeploy/card/{card}` → body `AwsCardSnapshot` (abaixo; ≤ 2 MB) → grava, dispara realtime `awsUpdated` no grupo
  `execplan:{card}` (evento `workItemsUpdated` com `action: "aws"`), conclui o `AwsRefresh` ativo (se `requestId`) e
  agenda o acompanhamento se algum ambiente está `deploying`.
- `GET AwsDeploy/card/{card}` → `{ snapshot: AwsCardSnapshot | null, collectedAt, collectedBy, refresh:
  { id, status, requestedBy, requestedAt, error } | null, executorOnline: bool }`.
- `POST AwsDeploy/card/{card}/refresh` → cria `AwsRefresh` (`queued`) para o executor **de quem pediu**; 409
  `{ error, code: "NO_EXECUTOR" }` se a pessoa não tem executor ativo (a tela mostra o comando manual). Um ativo
  por card+pessoa (repete → devolve o existente).
- Executor: `POST AwsDeployExecutor/next` → `{ id, card, requestedBy, attempt }` ou 204; `POST
  AwsDeployExecutor/{id}/report` → `{ status: done|failed, error?, durationMs }`. Lease 5 min; `queued` há mais de
  30 min → `expired`.
- MCP: `prmake_aws_status(card)` → texto curto: por ambiente o veredito + pipelines (status, quando, commit) + coletado
  em; "sem retrato — peça a coleta" quando não houver.

### `AwsCardSnapshot` (JSON; gravado como jsonb)

```json
{
  "version": 1, "card": 78720, "requestId": "guid|null",
  "collectedAt": "2026-11-24T19:30:00Z", "collectedBy": "alex.carlos", "machine": "host", "durationMs": 8123,
  "account": "367983645102", "region": "us-east-1",
  "index": { "total": 1078, "upserted": 3, "removed": 0, "updatedAt": "..." },
  "repositories": [{ "repository": "electradv/edv-solvace-apps", "localPath": true, "fetchedAt": "...",
                    "commits": [{ "sha": "…", "title": "…", "date": "…" }], "files": 12, "branches": ["bugfix/78720-dev"] }],
  "environments": [{
    "key": "qa", "label": "QA", "host": "qa.solvacelabs.com", "tier": "qa", "verdict": "deployed",
    "message": "Nosso código está em QA",
    "pipelines": [{
      "name": "edv-solvace-action-plan-qa", "repository": "electradv/edv-solvace-apps", "branch": "qa",
      "status": "Succeeded", "stage": "Build", "lastRunAt": "…", "lastRunEndedAt": "…", "durationSec": 354,
      "trigger": "WebhookV2", "deployedSha": "38f2a1a…", "deployedMessage": "Merge pull request #14929 …",
      "deployedAt": "…", "inBranch": true, "deployed": true, "verdict": "deployed",
      "consoleUrl": "https://…/codepipeline/pipelines/<name>/view", "buildUrl": "https://…",
      "action": "Deploy SandboxEfesoNew-Solvacelabs|null", "latest": { "status": "InProgress", "sha": "…", "startedAt": "…" },
      "failedLogTail": ["…"]
    }],
    "logs": [{ "group": "/aws/qa/actionplan", "url": "…", "storedBytes": 0, "lastEventAt": "…",
               "errors": [{ "at": "…", "message": "…" }] }],
    "resources": { "cloudfront": { "id": "…", "aliases": ["…"] }, "elasticBeanstalk": ["Qa-Solvacelabs"] }
  }],
  "resources": { "lambdas": [{ "name": "…", "runtime": "…", "lastModified": "…" }],
                 "alarms": [{ "name": "…", "state": "ALARM", "since": "…", "reason": "…" }] },
  "denied": ["ecs", "eks", "ecr"], "warnings": ["…"]
}
```

## Fases

| Fase | Onda | Depende | Conteúdo |
|---|---|---|---|
| B1 | 1 | — | Entidades `AwsPipeline` (índice), `AwsCardSnapshot`, `AwsRefresh` no schema do ExecutionPlans + migração; seed `AwsDeployEnvironments` + catálogo; seed do taskKind `aws-deploy-map` (catálogo `WorkItemTaskKinds`, `WorkTaskKind.Known`) |
| B2 | 2 | B1 | `AwsDeployController` + `AwsDeployExecutorController` + serviço (contratos acima), realtime, regex do executor, MCP `prmake_aws_status`, acompanhamento do deploy |
| B3 | 2 | B1 | Sub-tarefa automática `aws-deploy-map` nos itens com código (criação do item a partir do plano, sync, item avulso com sub-tarefa `code`/`cherry-pick`/`pr`): uma por item, depois do último `pr`/`code`, antes de `env-validation`/`validation`; idempotente; não recriar se o usuário removeu |
| B4 | 3 | B2 | Testes (unit) do serviço: veredito, índice incremental, refresh do executor de quem pediu, auto-subtarefa |
| S1 | 1 | — | `skills/aws-deploy/` (SKILL.md, `scripts/aws.sh`, `scripts/aws_deploy.py`): índice incremental, CloudFront→tier/EB, pipelines do card, git (inBranch/deployed), execuções/ações, logs/lambdas/alarmes, `denied`; `--dry-run` imprime o JSON; permissão `awsdeploy-v1` no `prmake-skills.sh`; bloco Windows/Git Bash |
| S2 | 2 | S1, B1 | `executar-item-de-trabalho`: linha do guia para `aws-deploy-map`; `especialista-do-caso`/`analisar-*`: consultar `prmake_aws_status` antes de dizer que algo está em um ambiente |
| S3 | 2 | S1, B2 | Executor 1.5.0: laço `AwsDeployLoop` (como `PromotionLoop`): `AwsDeployExecutor/next` → roda `aws.sh collect <card> --request <id>` → `report`; acordado pelo relay |
| F1 | 1 | — | Layout: abas em linha inteira (`.wb-seg` flex:1, re-tune dos breakpoints com 6 abas), linha de horas própria, tela cheia da seção Execução (`FullscreenPanel`, esconder o botão interno do plano na Bancada) |
| F2 | 2 | contrato | Aba **AWS** (desktop e celular, `?focus=aws`, passo do tour): `AwsDeployService`, componente `app-aws-deploy-panel` (cartões por ambiente com veredito grande, pipelines, logs/erros, recursos, negados, coletado em, Atualizar + estado do pedido, comando manual sem executor), realtime `action: "aws"` |
| Q1 | 4 | tudo | dotnet build/test, ng build, e2e local (harness) com retrato real do card 77971 (prints) |
| Q2 | 4 | Q1 | PRs back + front (merge só com ok do usuário) |
