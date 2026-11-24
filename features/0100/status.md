# Status 0100

- Branch: `feature/0100` (prform.api-0100 e prform-app-0100), a partir de origin/master.
- Merge: autorizado pelo usuário em 2026-11-24 (back e front juntos, depois do Q1).

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — entidades/migração/seeds | ⏳ | agente back | |
| B2 — API + executor + MCP + acompanhamento | ⏳ | agente back | |
| B3 — sub-tarefa automática aws-deploy-map | ⏳ | agente back | |
| B4 — testes | ⏳ | agente back | |
| S1 — script de coleta (aws-deploy) | ✅ | sessão principal | d54a6cf — testado na AWS real: 77971 (apps, lib compartilhada: 112 esteiras, 92 s) e 78721 (legado, EB do host, 61 s); esteiras `primary`, branch de trabalho e paradas > 120 dias fora do veredito |
| S2 — skills (executar-item, especialista, análises) | ✅ | sessão principal | d54a6cf — linha `aws-deploy-map` no guia, especialista e analisar-bug (a US herda) consultam `prmake_aws_status`; permissão `awsdeploy-v1` |
| S3 — executor 1.5.0 (laço AWS) | ✅ | sessão principal | a2febd0 — laço próprio (padrão da promoção), api-key da pessoa, heartbeat, doctor (CLI da AWS, skill) |
| F1 — layout (abas, horas, tela cheia da Execução) | ✅ | agente front | front f7bf67c — abas esticadas na linha inteira (longo → curto → só ícone), horas/situação/sugestões em linha própria, seção Execução inteira em tela cheia (`FullscreenPanel`, plano com `[showExpand]=false` e `[wide]` = layout etapas \| detalhes); tour v11 |
| F2 — aba AWS | ✅ | agente front | front a075839 — `AwsDeployService` + `app-aws-deploy-panel` (veredito grande por ambiente, esteiras principais/secundárias, lista longa recolhida, nosso código, logs, outros recursos, negados, comando manual com 409/executor desligado), `?focus=aws`, ponto na aba (falhou/deploy), `listenAws` (`action: "aws"`; `listenWorkItems` ignora "aws"); ng build ok, spec 8/8; prints com os retratos reais 77971/78721 (harness interceptado) |
| Q1 — build/testes/e2e | ⏳ | | |
| Q2 — PRs | ⏳ | | |

## Log

- 2026-11-24 — spec/plano a partir do pedido + levantamento da AWS da máquina (1.078 pipelines, CloudFront → tier/EB).
- 2026-11-24 — S1–S3: coletor validado contra a conta real (somente leitura), executor 1.5.0.
- 2026-11-24 — F1–F2: layout da Bancada (abas/horas/tela cheia da Execução) e aba AWS no front (f7bf67c, a075839).
