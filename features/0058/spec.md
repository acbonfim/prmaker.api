# Feature 0058 — Engenharia reversa: etapa OPCIONAL de infra (AWS)

## Pedido (usuário, 2026-10-05)
A engenharia reversa só lia o código (arquivos de deploy, `appsettings`, `serverless.template`) — nunca a AWS. Criar uma
etapa **opcional** que consulte a AWS pelo **CLI real** e mapeie **tudo** o que a Solvace usa lá: esteira que faz deploy,
buckets S3, Secrets Manager consultado, onde achar os logs no CloudWatch, Lambdas, filas etc. O usuário autoriza a leitura
da conta explicitamente (sem CLI falso).

## Solução
- `re.sh infra <modulo>` + `re_infra.py`: **somente leitura** (lista branca list/describe/get; nunca `get-secret-value`,
  `get-parameter`, `receive-message`, `get-object`). Mapeia a conta (Lambda, S3, CodePipeline/CodeBuild/CodeDeploy,
  Secrets Manager — nomes e último acesso —, SSM, CloudWatch Logs, SQS, SNS, EventBridge, RDS, Cognito, CloudFront,
  Route 53, ACM, API Gateway, DynamoDB, ElastiCache, alarmes, Step Functions, ECS/ECR, EC2, Beanstalk, ELB, KMS,
  CodeArtifact, Glue, SES, Kinesis), liga ao módulo (nome, sigla, o que o código cita, vizinhos de um salto) e lê as
  esteiras dos repositórios (GitHub Actions, buildspec, appspec, Dockerfile, serverless.template).
- Saída em `~/.prmake/reverse/<modulo>/infra/` (`modulo.md`, `resumo.md`, `conta-<id>.json`) + `inventario-infra.json`
  (a cobertura exige só o que é do módulo).
- Servidor: tipo de item **`INF`** (evidência `**Onde:** aws <conta>/<região> · <serviço>:<nome>`), seção **opcional**
  "Infraestrutura e AWS" no levantamento de arquitetura (não entra nos cabeçalhos obrigatórios), etapa de andamento
  `infra` (criada só quando roda), config `ReverseEngineeringInfra` (`accounts` por id, `regions`) semeada por migração.
- Permissão negada em algum serviço não derruba nada: vai para `resumo.md` e vira `GAP`.

## Fora do escopo
Escrever na AWS; ler valores de segredo; varrer todas as regiões automaticamente (as regiões vêm da config/`--region`).
