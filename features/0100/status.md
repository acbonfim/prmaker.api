# Status 0100

- Branch: `feature/0100` (prform.api-0100 e prform-app-0100), a partir de origin/master.
- Merge: autorizado pelo usuário em 2026-11-24 (back e front juntos, depois do Q1).

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — entidades/migração/seeds | ✅ | agente back | 304bd2e — migrações `AwsDeploy0100` (ExecutionPlanContext: AwsPipelines, AwsCardSnapshots, AwsRefreshes + WorkItems.AwsDeployMapApplied) e `SeedAwsDeploy0100` (DefaultContext: AwsDeployEnvironments, AwsDeployFollowMinutes=2, AwsDeployFollowMax=15, kind aws-deploy-map); up/down testados num Postgres isolado |
| B2 — API + executor + MCP + acompanhamento | ✅ | agente back | 4a086ad — contratos do plano (JSON camelCase); extras compatíveis: `POST AwsDeployExecutor/{id}/heartbeat` → `{stop}`, `source` no claim, `skipped` no POST do índice, `updated` guardado/devolvido como texto; acorda o executor com `executionQueueReady` no grupo `execworkers:{dono}`; smoke test local (409 NO_EXECUTOR, claim só do dono, 403 no report de outro, follow agendado) |
| B3 — sub-tarefa automática aws-deploy-map | ✅ | agente back | 8ff75db — item avulso: depois da última code/cherry-pick/pr (criar/editar/acrescentar), não volta se tirada; item do plano: etapa `mapear-deploy-aws` (taskKind aws-deploy-map, fixa no lugar) entra no plano de CORREÇÃO e o espelho ganha a sub-tarefa pela sincronização |
| B4 — testes | ✅ | agente back | d4e075a — 37 testes novos em executionplans.tests (330 ok) + 5 em prform.tests (449 ok) |
| S1 — script de coleta (aws-deploy) | ✅ | sessão principal | d54a6cf — testado na AWS real: 77971 (apps, lib compartilhada: 112 esteiras, 92 s) e 78721 (legado, EB do host, 61 s); esteiras `primary`, branch de trabalho e paradas > 120 dias fora do veredito |
| S2 — skills (executar-item, especialista, análises) | ✅ | sessão principal | d54a6cf — linha `aws-deploy-map` no guia, especialista e analisar-bug (a US herda) consultam `prmake_aws_status`; permissão `awsdeploy-v1` |
| S3 — executor 1.5.0 (laço AWS) | ✅ | sessão principal | a2febd0 — laço próprio (padrão da promoção), api-key da pessoa, heartbeat, doctor (CLI da AWS, skill) |
| F1 — layout (abas, horas, tela cheia da Execução) | ✅ | agente front | front f7bf67c — abas esticadas na linha inteira (longo → curto → só ícone), horas/situação/sugestões em linha própria, seção Execução inteira em tela cheia (`FullscreenPanel`, plano com `[showExpand]=false` e `[wide]` = layout etapas \| detalhes); tour v11 |
| F2 — aba AWS | ✅ | agente front | front a075839 — `AwsDeployService` + `app-aws-deploy-panel` (veredito grande por ambiente, esteiras principais/secundárias, lista longa recolhida, nosso código, logs, outros recursos, negados, comando manual com 409/executor desligado), `?focus=aws`, ponto na aba (falhou/deploy), `listenAws` (`action: "aws"`; `listenWorkItems` ignora "aws"); ng build ok, spec 8/8; prints com os retratos reais 77971/78721 (harness interceptado) |
| Q1 — build/testes/e2e | ✅ | sessão principal | testes do agente back (executionplans 330, prform 449) + ng build/spec 8/8; e2e com API local: índice 1.078 pipes (semente 7 s, incremental 5 s, 0 alterações), coleta real 78721 gravada e lida (GET/`show`) |
| Q2 — PRs | ⏳ | | |

## Log

- 2026-11-24 — spec/plano a partir do pedido + levantamento da AWS da máquina (1.078 pipelines, CloudFront → tier/EB).
- 2026-11-24 — S1–S3: coletor validado contra a conta real (somente leitura), executor 1.5.0.
- 2026-11-24 — F1–F2: layout da Bancada (abas/horas/tela cheia da Execução) e aba AWS no front (f7bf67c, a075839).
- 2026-11-24 — B1–B4 (agente back): API da aba AWS, fila de coleta do executor de quem pediu, acompanhamento do deploy, sub-tarefa/etapa automática `aws-deploy-map`, `prmake_aws_status`. Atenção S2: o plano de correção ganha a etapa `mapear-deploy-aws` — a analisar-bug/analisar-us precisam saber executá-la (rodar aws.sh collect e concluir com o resumo). S3: claim `{id, card, requestedBy, attempt, source}`; heartbeat opcional estende a trava de 5 min (índice inteiro passa disso).
- 2026-11-24 — Q1 ok; etapa `mapear-deploy-aws` do plano documentada na correção (dd30e00). PRs back + front.
