# Deploy — Cloud Run (Terraform + GitHub Actions)

Publica as duas APIs deste repo no **Google Cloud Run**, com scale-to-zero (custo ~$0 para uso baixo).
O banco é um **PostgreSQL 18 premium no MonsterASP (EUA, `db70152`)**, um database com um schema por módulo (`auth`, `prform`, `vacations`, `timeline`) — as APIs conectam por internet pública (TLS, `SSL Mode=Require`).

- `cime-pullrequest` — API principal (Solvace.PullRequests + módulos), schemas `prform`/`vacations`/`timeline` (+ leitura de `auth`)
- `cime-auth` — Auth API (Cime.Auth), schema `auth`

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

## Tempo real (SignalR) — relay no MonsterASP (feature 0013)

O hub SignalR **não roda mais no Cloud Run**. No Cloud Run, a CPU é cobrada enquanto houver um
request aberto, e um WebSocket é um request aberto: uma aba esquecida mantinha a `cime-pullrequest`
cobrada 24 h (~US$ 61/mês). Agora:

```
browser ──GET /api/v1/RealTime/connection (x-api-key)──▶ cime-pullrequest ──▶ { url, accessToken (10 min) }
browser ══wss://prformapi.runasp.net/ws?access_token=…══▶ relay (MonsterASP site40755, custo fixo)
cime-pullrequest ──POST /publish (X-Relay-Key)──▶ relay ──▶ grupo
```

- **Relay**: `CIME/modules/Cime.RealTime/src/Cime.RealTime.Relay`. Deploy pelo workflow
  `deploy-realtime.yml` (Web Deploy; secrets listados no topo do arquivo). Health: `GET /health`.
- **API**: `RealTime__Mode = Relay` + `RealTime__RelayUrl` (env) e `RealTime__RelayKey` /
  `RealTime__TokenSigningKey` (Secret Manager). A publicação é best-effort (timeout 3 s): relay fora
  do ar não quebra nenhuma operação, só o aviso em tempo real.
- **Chaves**: `realtime-relay-key` e `realtime-token-signing-key` têm que ser **iguais** no Secret
  Manager e nos secrets do GitHub (`REALTIME_RELAY_KEY`, `REALTIME_TOKEN_SIGNING_KEY`).
- **Endereço**: `https://prformapi.runasp.net` (site40755, que antes rodava a API antiga), variável
  `realtime_relay_url`. Para usar domínio próprio: adicionar o domínio no painel, criar o CNAME
  (`deploy/dns`, variável `realtime_cname_target = "site40755.siteasp.net."`), ativar o Let's Encrypt
  e trocar `realtime_relay_url`.
- **Rollback**: `RealTime__Mode = InProcess` e, na `cime-pullrequest`, `max_instances = 1`,
  `timeout = 3600`, `session_affinity = true` (hub volta para a API, com o custo de antes).

## Autenticação no PostgreSQL (feature 0014) — virada e rollback (histórico: concluída em 2026-09-26)

A Cime.Auth e a leitura de usuários da API principal (`AuthenticationContext`) usam PostgreSQL
(MonsterASP), com as tabelas no schema `auth` e a chave `ConnectionStrings__AuthDatabase`
(secret `postgres-auth-connection`). O migrador fica em `tools/Cime.DataMigrator` (perfil `auth`).

```bash
cd tools/Cime.DataMigrator
export CIME_SOURCE='Server=db30567.public.databaseasp.net; Database=db30567; User Id=…; Password=…; TrustServerCertificate=True'
export CIME_TARGET_POSTGRES='Host=…; Port=5432; Database=…; Username=…; Password=…; SSL Mode=Prefer'
dotnet run -- auth schema    # cria o schema auth (migrações da auth)
dotnet run -- auth check     # só leitura: colunas, valores que o Postgres recusaria, destino vazio
dotnet run -- auth copy      # transação única, ids originais, sequências ajustadas
dotnet run -- auth verify    # 0 diferenças = nenhum dado perdido
dotnet run -- auth reset --confirm <database>   # apaga o schema auth para refazer
```

**Virada** (janela curta, fora do horário de uso):
1. Antes: backup do SQL Server no painel; secret `postgres-auth-connection` no `secrets.auto.tfvars` e
   `terraform apply` (o código antigo ignora a env nova).
2. `reset` (se houve ensaio) → `schema` → `check` → `copy` → `verify` = 0.
3. Merge do PR → deploy de `cime-auth` e `cime-pullrequest` já no PostgreSQL.
4. `verify` de novo: escritas no SQL Server durante o deploy aparecem aqui (reconciliar à mão).
5. Testar login, api-key, gestão de usuários e nomes na timeline.

**Rollback** (enquanto o SQL Server existir): voltar o tráfego das duas APIs para a revisão anterior,
que lê o SQL Server pelas chaves antigas (`DefaultConnection` na auth, `AuthenticationConnection` na
API principal):
```bash
gcloud run services update-traffic cime-auth --region us-central1 --to-revisions=<revisão-anterior>=100
gcloud run services update-traffic cime-pullrequest --region us-central1 --to-revisions=<revisão-anterior>=100
```
Escritas feitas no PostgreSQL depois da virada: `verify` lista (reaplicar à mão, se houver).

**Cuidados no código da auth**: as datas são `DateTime.Now` gravadas em `timestamp without time zone`
(o Npgsql recusa `DateTime.UtcNow` nessas colunas); o Postgres diferencia maiúsculas (as consultas já
normalizam com `ToUpper`/`ToLower`); `DateTime.MinValue` é gravado como `-infinity` (padrão do Npgsql,
volta como `MinValue` na leitura). Papéis padrão e admin inicial só são criados com o banco vazio
(`AuthSeeder`, senha em `Seed__AdminPassword`).

## API principal no PostgreSQL (feature 0015) — virada e rollback (histórico: concluída em 2026-09-26)

Os contextos `DefaultContext`, `VacationContext` e `TimelineContext` usam o mesmo database da auth,
cada um no seu schema (`prform`, `vacations`, `timeline`), com a chave `ConnectionStrings__PrformDatabase`
(secret `postgres-prform-connection`). Migrador: perfil `prform` (origem MySQL).

```bash
cd tools/Cime.DataMigrator
export CIME_SOURCE='Server=db31021.public.databaseasp.net; Database=db31021; Uid=…; Pwd=…; SslMode=Preferred'
export CIME_TARGET_POSTGRES='Host=…; Port=5432; Database=…; Username=…; Password=…; SSL Mode=Require'
dotnet run -- prform schema | check | copy | verify
dotnet run -- prform reset --confirm <database>   # apaga só prform/vacations/timeline (nunca o auth)
```
O `check` avisa tabelas da origem fora do modelo (não copiadas — ex.: `PullRequestsLegacyBackup`,
que fica no backup final do MySQL) e a collation da origem.

**Virada** (janela curta, fora do horário de uso — a API principal recebe escrita o dia todo):
1. Antes: `postgres-prform-connection` no `secrets.auto.tfvars` e `terraform apply` (o código antigo ignora).
2. `reset` (se houve ensaio) → `schema` → `check` → `copy` → `verify` = 0.
3. Merge do PR → deploy da `cime-pullrequest` já no PostgreSQL.
4. `verify` de novo: escritas no MySQL durante o deploy aparecem aqui (reconciliar à mão).
5. Testar no app: card (descrição/RC/resumo/PRs), timeline, handover, férias, plugins, integrações.

**Rollback** (enquanto o MySQL existir): tráfego da `cime-pullrequest` para a revisão anterior (lê o MySQL
pela `DefaultConnection`); escritas feitas no PostgreSQL depois da virada: `verify` lista.

**Cuidados no código**: `DateTime` é `timestamp without time zone` gravado com `Kind=Unspecified`
(`PostgresConventions.UseUnspecifiedDateTimes`, igual ao MySQL — o JSON continua sem `Z`);
`DateTimeOffset` é `timestamptz`. O Postgres diferencia maiúsculas: comparações de texto que precisam
ignorar caixa usam `ToLower()` nos dois lados. Dev local: `docker compose up -d` (Postgres 18).

## Notas importantes

- **Chave das integrações pessoais (`user-integrations-encryption-key` → `UserIntegrations__EncryptionKey`):** gere uma vez com `openssl rand -base64 32` e **não troque** depois que os usuários salvarem tokens em "Minhas integrações" — com outra chave os valores salvos ficam ilegíveis e cada um precisa salvar de novo. Sem ela, a API sobe, mas recusa salvar tokens pessoais (503).
- **Segredos:** nenhum valor real fica nos `appsettings.json` (0016); produção lê tudo do Secret Manager e o Development usa valores locais. Os valores antigos continuam no **histórico do git** — por isso a rotação (checklist R1 em `features/0016/status.md`).
- **Banco:** PostgreSQL premium `db70152` (EUA, perto do Cloud Run us-central1). Trocar de banco sem mudar código: atualizar os secrets `postgres-auth-connection`/`postgres-prform-connection` no `secrets.auto.tfvars` e o `plain_env.Database__Host` (força revisão nova, que relê o secret); aplicar primeiro com `-target` nas versões dos secrets e depois o resto.
- **Cold start:** com `min_instances = 0`, a 1ª request após ociosidade demora alguns segundos. Se incomodar, suba `min_instances = 1` (sai do custo zero).
- **Resiliência:** os `DbContext` usam `EnableRetryOnFailure` (Npgsql) para tolerar quedas da conexão via internet.
- **Migrations (seguras para múltiplas instâncias):** rodam no startup fora de `Development`, protegidas por *advisory lock* do PostgreSQL (`pg_try_advisory_lock` com prazo), então só uma instância migra por vez:
  - `prform.api`: um lock cobrindo os 3 contexts (Default, Vacation, Timeline) — `Startup/StartupMigrator.cs` + `Cime.BuildingBlocks.Persistence`.
  - `auth.api`: lock próprio, com o seed (papéis/admin só com banco vazio) dentro dele — `Services/MigrationService.cs`.
  - Falha de migration é **fatal**: o app não sobe e o Cloud Run mantém a revisão anterior servindo.
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
