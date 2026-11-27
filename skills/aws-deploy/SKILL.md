---
name: aws-deploy
description: Esteiras de deploy do card na AWS (feature 0100), SOMENTE LEITURA — pela CLI da AWS e o git da maquina, descobre as pipelines (CodePipeline) dos repositorios do card em cada ambiente (DEV, QA, Sandboxefeso, SandboxRT, Producao — configuravel no PRMake), o status e a ultima execucao de cada uma, o commit implantado e se o NOSSO codigo (commits do card) ja esta em cada ambiente; traz tambem os logs do modulo no ambiente (erros das ultimas 24 h, fim do log do build que falhou), lambdas, alarmes e CloudFront. Grava o retrato no PRMake (aba AWS da Bancada; MCP prmake_aws_status) e mantem o indice de pipelines em dia (pipes novas entram sozinhas). Roda sem sessao do Claude pelo executor (botao Atualizar da aba AWS), pela sub-tarefa `aws-deploy-map` dos itens de codigo ou manualmente. Use quando perguntarem "ja esta em QA/sandboxefeso/producao?", "o deploy rodou?", "a pipeline falhou?", "nosso commit ja subiu?", "status da esteira", "logs do modulo no ambiente", ou com /aws-deploy <card>.
---

# aws-deploy

Responde **"o nosso código do card já está em cada ambiente?"** com prova: esteira, execução, commit implantado.
Instalada e atualizada pelo PRMake (fonte: `skills/aws-deploy`). Token: `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card>     # coleta e grava no PRMake (~15 s a ~2 min)
bash ~/.claude/skills/aws-deploy/scripts/aws.sh show <card>        # o que está gravado
bash ~/.claude/skills/aws-deploy/scripts/aws.sh index              # só o índice de pipelines
```

## Regras

- **Somente leitura na AWS**: o script só aceita `list-*`, `describe-*`, `get-*` (nunca `get-secret-value` nem
  parâmetros decifrados), `batch-get-*` e `filter-log-events`. Nada grava na AWS. Não rode `aws` à mão para "corrigir"
  uma esteira — isso é com o time de SRE.
- **Antes de dizer que algo está (ou não) num ambiente**, consulte o retrato: MCP `prmake_aws_status` (ou `aws.sh show`).
  Retrato velho (mais de 1 h) ou ambiente em andamento → rode `collect` de novo.
- O veredito por ambiente vem do script — não reinterprete: `deployed` (nosso código está lá), `deploying` (deploy em
  andamento com ele), `waiting` (na branch do ambiente, ainda não implantado), `failed` (a execução com o nosso código
  falhou), `absent` (não chegou na branch do ambiente), `partial` (uma versão anterior do nosso código está lá; o ajuste
  mais recente ainda está no PR aberto), `not-applicable` (o repositório não vai para o ambiente),
  `unknown` (sem como conferir — ver os avisos). `deployed` exige o código **mais recente** do card: ajuste mesclado
  depois do último deploy vira `waiting`/`deploying` (campo `newer` da esteira), e PR do ajuste ainda aberto, `partial`
  (campo `unmerged`) — nunca diga que o ajuste está no ambiente só porque a 1ª versão está.
- Em `failed`, mostre o fim do log do build (`failedLogTail`) e o link do build; não conclua que a falha é do nosso
  código sem ler o log (a esteira pode quebrar por outro commit ou por infraestrutura).

## Governanca

As regras de governanca vem do PRMake (`prmake_rules` e, com a governanca ligada, o hook `PreToolUse`, que consulta o
PRMake antes de cada comando/ferramenta) — nao repita nem invente regras aqui.
- **Barrado** ("Barrado pela governanca do PRMake: ... (regra ...)" ou "Bloqueado pelo executor do PRMake"): nao
  contorne, nem por outro comando, script ou ferramenta equivalente. Registre na sub-tarefa o que precisa de uma pessoa
  (com a regra) e siga com o resto.
- **Aviso** ("Aviso da governanca do PRMake: ..."): siga e cite o aviso (com a regra) no resultado da sub-tarefa.
- A coleta so le: nunca leia `~/.aws/credentials` nem rode `aws` fora do `aws.sh` para "completar" um dado.

## Itens ja concluidos e cards antigos

O retrato e do **card**, nao do item: o botao Atualizar da aba AWS (ou `aws.sh collect <card>`) funciona em qualquer
card, com itens concluidos, cancelados ou sem item. O PRMake junta os PRs do card e as entregas de codigo de todos os
itens; card tratado fora do PRMake (sem PR/entrega registrados) → o script procura nos clones da maquina os commits que
citam o card (ou informe `--repo <nome>`). A sub-tarefa automatica so entra em item ainda aberto.

## Como funciona (para explicar a quem pergunta)

1. **Índice de pipelines** compartilhado no PRMake: cada coleta faz `list-pipelines` e busca a definição só das
   novas/alteradas (pelo `updated`); as removidas saem. A 1ª carga (~1.100 pipelines) leva ~6 min, depois segundos.
2. **Ambiente → esteiras**: o host do ambiente no CloudFront diz o *tier* (origens S3 `<app>-<tier>`: devsp→dev,
   qa→qa, sandboxefeso→rc, sandboxrt→rt, clientes→prod) e o Elastic Beanstalk do legado. Esteira com deploy no
   Elastic Beanstalk do host é a **principal** do legado; sem isso vale o tier (sufixo do nome ou `ENV` do CodeBuild).
   Esteira de branch de trabalho (`sre/…`, `perf/…`) e parada há mais de 120 dias fica de fora (aviso).
3. **Esteiras do card** = repositório do card **e** algum arquivo alterado casa com o `filePaths` do gatilho
   (edv-solvace-apps é monorepo: mudar `lib-shared-*` afeta todos os apps).
4. **Nosso código** = commits do card (SHAs registrados nas entregas/PRs + commits que citam o card, ex. `AB#78720`)
   conferidos no git local: está na branch da esteira? está no commit implantado (último sucesso da esteira ou da
   ação de deploy do ambiente)?
5. Também: grupos de log `/aws/<host>/<módulo>` com os erros das últimas 24 h, lambdas e alarmes do módulo
   (autoscaling fora), CloudFront do host; ECS/EKS/ECR negados aparecem como "sem permissão".

## Sub-tarefa `aws-deploy-map` (itens de trabalho com código)

O PRMake põe sozinho essa sub-tarefa nos itens que mexem em código. Ao executá-la (skill
`executar-item-de-trabalho`): `aws.sh collect <card>` e conclua a sub-tarefa com o resumo por ambiente que o script
imprime (uma linha por ambiente). Repositório fora desta máquina → conclua com o aviso (não falhe o item).
