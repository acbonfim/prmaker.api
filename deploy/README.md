# Deploy — Cloud Run (Terraform + GitHub Actions)

Publica as duas APIs deste repo no **Google Cloud Run**, com scale-to-zero (custo ~$0 para uso baixo).
O banco de dados **permanece no MonsterASP** — as APIs conectam por internet pública.

- `cime-pullrequest` — API principal (Solvace.PullRequests), MySQL + SQL Server
- `cime-auth` — Auth API (Cime.Auth), SQL Server

O frontend Angular é um repo separado (`../solvace.prform.web`) — ver seção no fim.

## O que o kit provisiona (Terraform)

- Artifact Registry (Docker)
- Secret Manager (connection strings, tokens, senhas) — **nada de segredo na imagem**
- Cloud Run x2 (scale-to-zero, porta 8080, público)
- Service Account de runtime com acesso só aos secrets usados
- Workload Identity Federation (GitHub Actions autentica via OIDC, **sem chave JSON**)

---

## Passo a passo

### 1. Pré-requisitos
- Conta de faturamento GCP ativa e um projeto (`gcloud projects create` ou console)
- `gcloud`, `terraform` e `docker` instalados; `gcloud auth application-default login`

### 2. Provisionar a infra
```bash
cd deploy/terraform
cp secrets.auto.tfvars.example secrets.auto.tfvars   # preencha com valores REAIS (rotacione!)
terraform init
terraform apply
```
No primeiro apply os serviços sobem com uma imagem "hello" placeholder — o deploy real vem do pipeline.

Anote os outputs:
```bash
terraform output workload_identity_provider   # -> GCP_WIF_PROVIDER
terraform output deployer_service_account     # -> GCP_DEPLOY_SA
terraform output artifact_registry_repo
terraform output service_urls
```

### 3. Configurar variáveis no GitHub
Em **Settings → Secrets and variables → Actions → Variables** (não precisam ser secrets — são identificadores):

| Variável | Valor |
|---|---|
| `GCP_PROJECT_ID` | id do projeto |
| `GCP_REGION` | `us-central1` |
| `GCP_AR_REPO` | `cime` |
| `GCP_WIF_PROVIDER` | output `workload_identity_provider` |
| `GCP_DEPLOY_SA` | output `deployer_service_account` |

### 4. Deploy
Push na `master` (ou rode o workflow manualmente em Actions). O pipeline builda, envia a imagem e atualiza o Cloud Run dos dois serviços.

---

## Onde fica cada configuração (importante)

| Tipo | Onde | Como |
|---|---|---|
| Segredos (conn strings, tokens, JWT, SMTP) | **Secret Manager** | `secret_values` no tfvars → env var no Cloud Run |
| Config não-secreta (URL da auth, URL do front, flags) | **Cloud Run env var** | `plain_env` no tfvars |
| Identificadores de CI (projeto, região, WIF) | **GitHub repo variables** | 5 variáveis (tabela acima) |

> Recomendação: **não** coloque connection string em GitHub variables. Secret Manager permite
> rotacionar sem novo deploy e não expõe segredo no GitHub. GitHub variables guardam só os 5
> identificadores do pipeline. A URL da API de auth / do backend (não secretas) vão em `plain_env`.

## SignalR (WebSocket) no Cloud Run

O módulo RealTime usa SignalR (hub em `/ws`). Já está configurado para funcionar:
- **`cime-pullrequest` com `max_instances = 1`**: mantém todas as conexões WebSocket na mesma
  instância, então **não precisa de backplane (Redis)**. Continua com scale-to-zero quando ocioso.
- **`timeout = 3600s`**: conexões WebSocket de longa duração (máximo do Cloud Run). Após isso o
  cliente reconecta (o SignalR reconecta sozinho).
- **`session_affinity = true`**: reforça que reconexões voltem para a mesma instância.
- CORS/RealTime `AllowedOrigins` deve conter a origem do frontend (via `plain_env`), pois SignalR
  com credenciais não aceita `AllowAnyOrigin`.

> Se um dia o uso crescer e precisar de mais de 1 instância, aí sim entra um backplane
> (Redis/Memorystore) ou o Azure SignalR Service. Para 5 usuários, `max_instances = 1` é o certo.

## Notas importantes

- **Chave das integrações pessoais (`user-integrations-encryption-key` → `UserIntegrations__EncryptionKey`):** gere uma vez com `openssl rand -base64 32` e **não troque** depois que os usuários salvarem tokens em "Minhas integrações" — com outra chave os valores salvos ficam ilegíveis e cada um precisa salvar de novo. Sem ela, a API sobe, mas recusa salvar tokens pessoais (503).
- **Segredos rotacionados:** as chaves atuais estão no `appsettings.json` versionado (senhas de banco, token GitHub, PAT Azure, JWT, SMTP). Gere novas e coloque só no `secrets.auto.tfvars` / Secret Manager. Considere remover os valores do `appsettings.json`.
- **Host do banco:** de fora do MonsterASP use o host `.public.databaseasp.net` (a Auth API usava o host interno `db30567.databaseasp.net` — no Cloud Run tem que ser `db30567.public.databaseasp.net`).
- **Cold start:** com `min_instances = 0`, a 1ª request após ociosidade demora alguns segundos (abre conexão nova com o banco remoto). Se incomodar, suba `min_instances = 1` no `terraform.tfvars` (sai do custo zero).
- **Resiliência:** os `DbContext` já têm `EnableRetryOnFailure` (adicionado no `Program.cs` das duas APIs) para tolerar quedas na conexão via internet.
- **`ServerVersion.AutoDetect`** (API principal, MySQL) abre uma conexão ao banco no startup a cada cold start. Para reduzir latência/fragilidade, considere fixar a versão: `new MySqlServerVersion(new Version(8, 0, 0))`.
- **Migrations (seguras para múltiplas instâncias):** rodam no startup fora de `Development`, protegidas por *advisory lock* do próprio banco, então só uma instância migra por vez:
  - `prform.api` (MySQL): `GET_LOCK`/`RELEASE_LOCK` cobrindo os 3 contexts (Default, Vacation, Timeline) — `Startup/StartupMigrator.cs`.
  - `auth.api` (SQL Server): `sp_getapplock`/`sp_releaseapplock` — `Services/MigrationService.cs`.
  - Falha de migration é **fatal**: o app não sobe e o Cloud Run mantém a revisão anterior servindo (não publica schema quebrado). Antes o erro era engolido e o app subia mesmo quebrado.
  - Alternativa "de manual" (padrão ComandaCerta): mover as migrations pra um **job separado no pipeline** (Cloud Run Job) rodando após o deploy. Mais desacoplado, porém mais peças. Para este volume, o lock no startup é suficiente.

## Domínio custom (softhouse.app.br) — Opção A, grátis

Meta do cutover (desligar o MonsterASP):

| Subdomínio | Destino | Mecanismo | Custo |
|---|---|---|---|
| `api.softhouse.app.br` | `cime-pullrequest` | Cloud Run domain mapping (SSL do Google) | R$ 0 |
| `auth.softhouse.app.br` | `cime-auth` | Cloud Run domain mapping (SSL do Google) | R$ 0 |
| `app.softhouse.app.br` | frontend | Firebase Hosting (SSL + CDN) | R$ 0 |

O DNS **continua no Registro.br** (como hoje) — só trocamos os registros. O Terraform dos
mappings de API já está pronto em `domain.tf`, **desligado** por padrão.

### Passo a passo do cutover
1. **Baixe o TTL** dos registros atuais no Registro.br (ex.: 300s) alguns dias antes.
2. **Verifique o domínio** `softhouse.app.br` no [Google Search Console](https://search.google.com/search-console)
   (adiciona 1 registro TXT no Registro.br). Cobre todos os subdomínios.
3. `enable_domain_mapping = true` no tfvars e `terraform apply`.
4. `terraform output domain_dns_records` → cadastre os **CNAMEs** retornados no Registro.br
   (`api` e `auth`).
5. **Frontend (`app.`)**: no repo do Angular, `firebase init hosting` + `firebase deploy`, e em
   Firebase Console → Hosting → Add custom domain → `app.softhouse.app.br` (ele te dá o registro A/TXT
   pra pôr no Registro.br).
6. Valide cada subdomínio no GCP e então **remova os registros antigos do MonsterASP**.

> Passos manuais (uma vez, no seu acesso): verificação no Search Console e cadastro dos registros
> no Registro.br. O resto (`domain.tf`) é aplicado por CLI.

## Frontend (Angular, repo separado)
Duas opções baratas:
1. **Cloud Run** (container nginx servindo o `dist`) — mesma pipeline/infra.
2. **Firebase Hosting / Cloud Storage + CDN** — mais simples e barato para estático.

Posso gerar o Dockerfile nginx + workflow do frontend quando quiser.
