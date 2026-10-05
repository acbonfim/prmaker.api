# Infra (AWS) na engenharia reversa — etapa OPCIONAL (0058)

`bash $RE infra <modulo>` lê a AWS pelo **AWS CLI**, **somente leitura**, e traz para o documento de arquitetura tudo o que
o código sozinho não mostra: onde roda, quem faz o deploy, quais buckets e segredos usa e onde ficam os logs.
Só roda quando o usuário pede (ou aceita a oferta ao fim da leitura). Sem ela o documento segue e registra
`GAP` "infra não lida".

## O que é lido (apenas list/describe/get)
Lambda (config, gatilhos, **nomes** das variáveis), S3 (região, versionamento, notificações), **esteiras** (CodePipeline:
estágios/ações/última execução; CodeBuild: origem, imagem, buildspec com valores mascarados; CodeDeploy), **Secrets
Manager** (nome, descrição, última alteração e **último acesso** — o valor **nunca** é lido), SSM (nomes), **CloudWatch
Logs** (grupos, retenção), SQS (fila, DLQ, retenção), SNS (tópicos e assinaturas, e-mail mascarado), EventBridge
(regras, agendas, alvos), RDS/Aurora, Cognito, CloudFront, Route 53, ACM, API Gateway, DynamoDB, ElastiCache, alarmes,
Step Functions, ECS/ECR, EC2, Beanstalk, balanceadores, KMS (aliases), CodeArtifact, Glue, SES, Kinesis. Serviço sem
permissão de leitura aparece em `resumo.md` ("Sem permissão") — vira `GAP`, não erro.

O script recusa por código qualquer comando que não comece com list/describe/get/batch-get e qualquer `get-secret-value`,
`get-parameter(s)`, `receive-message`, `get-object`.

## Conta e perfil
A configuração do PRMake (`ReverseEngineeringInfra`: `accounts` por **id** e `regions`) diz qual conta mapear; o script
acha o perfil do CLI da máquina que entra nessa conta (`aws sts get-caller-identity`). Outra conta/região:
`bash $RE infra <modulo> --account <id> --region <r> [--profile <p>]`. `--termo <t>` liga recursos que o nome do módulo
não pega; `--so-conta` mapeia só a conta.

## Saída (`~/.prmake/reverse/<modulo>/infra/`)
- `modulo.md` — **leia este**: recursos ligados ao módulo (por nome, sigla, o que o código cita, e vizinhos de um salto:
  gatilho, alvo de regra, notificação de bucket, projeto da esteira), **onde ver os logs** (grupo + `aws logs tail`) e as
  **esteiras dos repositórios** (GitHub Actions, buildspec, appspec, Dockerfile, serverless.template, com `arquivo:linha`).
- `resumo.md` — a conta toda (contagem e nomes por serviço) + o que não pôde ser lido.
- `conta-<id>.json` — tudo, para consulta (`jq`). Contém endpoints de banco: **não copie para o documento**.
- `payload.json` — o recorte (so o ligado ao modulo + resumo da conta) que o `re.sh infra` envia para a aba **Infra** do modulo na tela.
- `../inventario-infra.json` — o que a cobertura exige: cada recurso ligado (`aws-<serviço>`) e cada arquivo de esteira.

## Como escrever (seção "Infraestrutura e AWS (opcional)" do levantamento de arquitetura)
- Um item `INF-NNN` por recurso do módulo: `### INF-004 — Lambda minha-funcao`, com
  `**Onde:** aws 367983645102/us-east-1 · lambda:minha-funcao` (evidência aceita pelo lint) e, quando houver, o arquivo
  do repositório que a cria/implanta (`arquivo:linha`).
- **Esteira**: um item por esteira (repo → branch/gatilho → build → deploy → ambiente), citando o arquivo e o recurso AWS.
- **Segredos**: só o NOME e quem consulta (`último acesso` do Secrets Manager + o código que lê). Nunca valor.
- **Logs**: o grupo do CloudWatch e o comando; mensagens típicas vêm do código (`**Onde:**`).
- Ligação duvidosa (recurso "ligado" só por nome parecido) → "a confirmar" + `GAP`. Recurso da conta que ninguém usa ou
  que o código cita e não existe na conta → `GAP` (divergência código × AWS).
- Nunca: valor de variável, senha, connection string, chave, endpoint com credencial, e-mail de pessoa.
