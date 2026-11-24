# 0100 — Aba AWS na Bancada (esteiras por ambiente, "nosso código está lá?") + ajustes de layout

Origem: pedido do usuário (2026-11-24, sessão do Claude Code), com print da barra de abas da Bancada
(Itens de trabalho · Execução · Arquivos · Relatado · Card):

> Na nossa máquina nós temos o CLI da AWS, e com ela nós conseguimos ver coisas relacionadas a deploy, esteira e tal. Eu
> preciso que exista uma aba chamada AWS, lá teremos itens da AWS inclusive as pipelines de deploy referentes ao
> repositório que trabalhamos naquele card. Ex.: caso seja o edv-solvace-apps, identificar as pipes de cada ambiente DEV,
> QA, Sandboxefeso e mostrar em tela os nomes, status atual de cada pipe, última data que rodou e se o nosso
> commit/branch já está naquele ambiente. Deve dizer claramente se o nosso código tratado no caso em específico já está
> em cada ambiente.
>
> Se necessário, vamos criar uma subtask automaticamente, em cada item de trabalho que envolva alteração de código, para
> que essa subtask tenha o trabalho de usar uma sessão do executor para mapear todas essas informações e deixar
> registrado, para que ele possa depois consultar com mais facilidade.
>
> Além do que falei acima, verifique outros itens da AWS como logs etc., para entender se faz sentido trazer também, e
> outras aplicações da AWS que podemos mapear ali referentes àquele caso em específico. Você tem liberdade para trazer
> coisas que acredita que são pertinentes (lembrando que tudo somente leitura por hora).
>
> Ajustes adicionais:
> 1. a seção de abas (itens de trabalho, execução, etc.) agora ocupa uma linha inteira até o chat com o especialista;
> 2. a seção de horas do card, sugestão etc. também ocupa uma linha;
> 3. a seção de execução ganha botão para abrir em tela cheia e expande/recolhe com a mesma animação que a do card e
>    linha do tempo.

Complemento (mesma sessão): **pipes novas são criadas, então ele deve ser capaz de atualizar essa lista sempre.**

## O que a AWS da máquina enxerga (levantado em 2026-11-24, conta 367983645102, us-east-1, só leitura)

- **CodePipeline**: 1.078 pipelines. Fonte quase sempre `CodeStarSourceConnection` com `FullRepositoryId`
  (`electradv/<repo>`) + `BranchName`; pipelines V2 têm gatilho por `push` com `branches` e **`filePaths`** (o
  edv-solvace-apps é um monorepo: uma pipeline por projeto × ambiente, ex.: `edv-solvace-action-plan-qa` só roda para
  `projects/action-plan/**` + libs compartilhadas). Por repositório: revamp-api-infra 374, revamp-BuildingBlocks 320,
  edv-solvace-apps 166, revamp 68, edv-solvace 56, revamp-<Módulo> 6–53, edv-solvace-api 11.
- **Ambiente da pipeline**: sufixo do nome = variável `ENV` do CodeBuild (`dev|qa|edge|rc|rt|prod|rv`). No
  edv-solvace-apps: `development→dev`, `qa→qa`, `edge→edge`, `release-version→rc` e `rt`, `master→prod`.
- **Ambiente da tela → tier**: descoberto pelo **CloudFront** (aliases × origens S3 `<app>-<tier>.solvacelabs`):
  `devsp`→dev, `qa`→qa, `sandboxefeso`→**rc**, `sandboxrt`→rt, `sandbox<cliente>`→edge, `<cliente>`/`demo`→prod. O
  legado de cada host fica num Elastic Beanstalk (`sandboxefesonew-solvacelabs` etc.).
- **Execuções**: `list-pipeline-executions` traz `sourceRevisions[].revisionId` (o commit implantado),
  `revisionSummary` (mensagem — os merges trazem `AB#<card>`), status, início/fim e o gatilho (branch/commit).
  `get-pipeline-state` traz o estágio atual e o link do build.
- **CodeBuild**: `batch-get-builds` (fases, status, log em `/aws/codebuild/<projeto>`).
- **CloudWatch Logs**: 4.822 grupos — `/aws/codebuild` (1.305), `/aws/lambda` (674), `/aws/elasticbeanstalk` (637),
  `/aws/ecs` (41) e grupos **por cliente/módulo** (`/aws/<cliente>/<módulo>`, ex. `/aws/sandboxefeso/actionplan`).
- **Lambda** (lista), **CloudWatch alarms** (estado), **CloudFront** (lista): liberados.
- **ECS, EKS, ECR**: negados para o usuário IAM desta máquina — a coleta tem de degradar ("sem permissão") e não falhar.
- Custo da coleta: `get-pipeline` das 1.078 levou ~6 min com a CLI em paralelo (24) → o índice é **incremental**
  (`list-pipelines` traz `updated`; só busca a definição das novas/alteradas, remove as que sumiram).
- Prova com o git local (card 77971, edv-solvace-apps): 29–30 commits `AB#77971` em `development` e `qa`, nenhum em
  `release-version`/`edge`/`master`; o commit implantado em QA (`38f2a1a`) é o merge do PR do card → "está em DEV e QA,
  não está em Sandboxefeso nem em produção".

## Requisitos

1. **Aba AWS** na Bancada (desktop e celular), somente leitura, com:
   - **Ambientes** do card (padrão DEV, QA, Sandboxefeso, SandboxRT, Produção — configurável no PRMake), cada um com um
     veredito claro e grande: **"Nosso código está em QA"** / **"Ainda não está em Sandboxefeso"** / "Na branch,
     aguardando deploy" / "Deploy em andamento" / "Deploy falhou" / "Não se aplica (este repositório não vai para
     este ambiente)".
   - Por ambiente, as **pipelines** do(s) repositório(s) do card (só as que o código do card afeta, pelo `filePaths`):
     nome, status atual, última execução (data/hora, duração), commit implantado (com a mensagem), link do console e do
     build, e se esse commit contém o nosso.
   - **Logs**: atalho para os grupos do CloudWatch do módulo no ambiente (`/aws/<host>/<módulo>`) e do build com
     falha (últimas linhas de erro do CodeBuild).
   - **Outros recursos do caso** (o que a conta deixa ler): lambdas do módulo, alarmes em ALARM ligados ao módulo /
     ambiente, distribuição CloudFront do host. ECS/EKS/ECR aparecem como "sem permissão nesta máquina" quando negados.
   - "Coletado em <data> por <pessoa>", **Atualizar** (pede a coleta à máquina de quem clicou) e estado vazio
     explicando como coletar.
2. **Lista de pipelines sempre atualizada**: cada coleta revisa o índice (novas/alteradas/removidas); o índice fica
   no PRMake (compartilhado), então qualquer máquina que coleta mantém a lista em dia.
3. **Coleta pela máquina** (a API roda no Cloud Run e não tem credencial da AWS): script somente leitura nas skills,
   executado pelo executor de quem pediu (regra de segurança 78723) **sem sessão do Claude** (determinístico, sem
   tokens); os resultados vão para o PRMake.
4. **Sub-tarefa automática** `aws-deploy-map` em todo item de trabalho que altera código (antes da validação/QA): roda
   a coleta e registra o resultado como entrega, para o executor e o especialista consultarem depois
   (`prmake_aws_status`).
5. **Layout da Bancada**: (1) abas numa linha inteira até o chat do Especialista; (2) horas + situação + sugestões numa
   linha própria; (3) Execução com botão de tela cheia que leva a seção inteira (seletor do item + plano), com a mesma
   animação do Card e da Linha do tempo (`FullscreenPanel`).
6. Tudo **somente leitura** na AWS: a coleta só usa `list-*`, `get-*`, `describe-*`, `batch-get-*`, `filter-log-events`.
