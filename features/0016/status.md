# Status — Feature 0016 (Limpeza dos bancos, do código e da configuração legados)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0016` no backend (`../prform.api-0016`) e no front (`../solvace.prform.web/prform-app`, só a C3), a partir de `master`.
> **Pré-requisito**: 0015 em produção.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| C1 | Remover Pomelo/SqlServer/Sqlite; `AuthenticationContext` na `PrformDatabase` | A | 0015 estável | ✅ concluída | Claude | `4f1b606` |
| C2 | Valores reais fora dos `appsettings` e do código (chaves JWT da auth); chaves mortas; dev local | A | 0015 estável | ✅ concluída | Claude | `5dd721d` |
| C3 | Tempo real só com token (back + front) | A | 0015 estável | ✅ concluída | Claude | `423bcf9`, front `0069568` |
| C4 | Arquivos e docs obsoletos (`CLAUDE.md`, `deploy/README.md`, `deploy.md`, pipeline e scripts antigos) | A | — | ✅ concluída | Claude | `0b170fe` |
| C5 | Deploy da onda A + testes | A | C1–C4 | ✅ concluída | Claude (autorizado) | PRs back #21, front #14 |
| R1 | Rotação das credenciais expostas | B | — | ⬜ pendente | usuário + Claude | — |
| B1 | **Ponto sem volta**: grants por secret (moved/removed) e depois remover secrets/envs de fallback (Terraform) | B | C5; SQL Server ≥ ~2026-10-26; MySQL ≥ 0015 + 30 dias | ⬜ pendente | usuário + Claude | — |
| B2 | Backups finais (mysqldump, .bak) | B | B1 | ⬜ pendente | usuário | — |
| B3 | Excluir `db30567`, `db31021` e `db70140` (Postgres gratuito, substituído pelo premium) no painel | B | B2 | ⬜ pendente | usuário | — |
| B4 | Remover o migrador | B | B3 | ⬜ pendente | Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Datas
- Virada da 0014 (auth → Postgres): 2026-09-26 → SQL Server `db30567` pode sair a partir de ~2026-10-26.
- Virada da 0015 (PR → Postgres): 2026-09-26 → MariaDB `db31021` pode sair a partir de ~2026-10-26. (A origem era **MariaDB 10.11**, não MySQL; tem também a `pullrequestslegacybackup` com 217 linhas e as tabelas `aspnet*`/`services` legadas, que precisam entrar no backup final.)
- **2026-09-26 02:19 UTC — banco trocado para o PostgreSQL premium `db70152` (EUA, Salt Lake City; fuso do servidor `America/Denver`)**, perto do Cloud Run (us-central1). O `db70140` (gratuito, Alemanha) não podia ser promovido a premium pelo painel. Cópia com `pg_dump`/`pg_restore` dos schemas `auth/prform/vacations/timeline`: 24 tabelas, 690 linhas, checksums (sessões em UTC) idênticos; nenhuma escrita no antigo durante a troca. Revisões: `cime-auth-00021-l7d`, `cime-pullrequest-00024-pf7` (env `Database__Host=db70152` no `plain_env` para forçar a revisão). O `db70140` fica parado como fallback por alguns dias → excluir na onda B (sem backup necessário além do que já está no `db70152`; um `pg_dump` final por garantia). As versões antigas dos secrets (apontando para o `db70140`) foram destruídas pelo Terraform; para voltar, reverter o `secrets.auto.tfvars` (backup no scratchpad) e aplicar.
- Suporte ao .NET 8 acaba em 2026-11-10: a onda A (sem Pomelo) precisa estar pronta antes do upgrade.

## Onda A — notas (2026-09-26)
- **C1**: Pomelo, EF SqlServer e EF Sqlite removidos de 6 projetos (Sqlite nunca foi usado). O `AuthenticationContext` usa a `PrformDatabase` (mesmo database, schema `auth`). A env `ConnectionStrings__AuthDatabase` da `cime-pullrequest` sai no Terraform, mas só **depois** do deploy (a revisão atual ainda a lê).
- **C2**: nenhum valor real nos `appsettings.json`. Produção já lia JWT/PAT/token do GitHub/SMTP do Secret Manager; as chaves de IA eram placeholders.
  - **Achado**: as chaves de assinatura da auth (`Settings.Secret`/`SecretRefresh`) estavam **no código**. Passaram para `Auth:Secret`/`Auth:SecretRefresh`, com os **mesmos valores**. Prova: o hash do `Settings.Secret` é igual ao do `Auth:Secret` da API, e a API valida em produção, com o `jwt-secret`, as api-keys que a auth assina. O refresh ganhou o secret `jwt-refresh-secret` (valor antigo, 106 chars). A auth não sobe sem as duas.
  - Development com chaves só de dev (auth e API locais se entendem), sem senha SMTP; `GITHUB_*`/`AZURE_*` soltos (não lidos) removidos.
  - Testado localmente: auth sem segredos não sobe; login, refresh e api-key com os segredos da config; a API aceita a api-key válida e recusa assinatura alterada/assinada com outra chave (403) e sem chave (401); `departments` lê usuários pela conexão única.
- **C3**: `RealTimeTokenMiddleware` (só token); saem `ApiKey` (opções, plugin, appsettings, env) e o caminho legado do `WsService`/`apiKeyWS` no front. Testado com o cliente SignalR do front no modo em processo: com token conecta (WebSocket); sem token e com a chave `123456789` → 401.
- **C4**: `CLAUDE.md` e `deploy/README.md` no estado atual; removidos pipeline do Bitbucket, README/script MySQL antigos da auth, `appsettings.QA.json`, `deploy.md`, `.idea`/`Cime.Auth.sln` (a auth entrou na `Solvace.Master.sln`, 29 projetos).
- **C5 — ordem obrigatória do deploy**:
  1. Terraform passo 1: cria `jwt-refresh-secret`, dá à auth acesso a `jwt-secret`/`jwt-refresh-secret` e as envs `Auth__Secret`/`Auth__SecretRefresh`; mantém as envs da API principal. O código atual ignora as envs novas. Plano: 4 criados, 2 alterados (na API principal, só o ajuste cosmético do `scaling`).
  2. Merge dos PRs (backend e front) → deploy.
  3. ~~Terraform passo 2~~ **não aplicado**: o plano mostrou que tirar `ConnectionStrings__AuthDatabase`/`RealTime__ApiKey` da `cime-pullrequest` destruiria os grants `pullrequest:postgres-auth-connection` e `pullrequest:realtime-apikey`. Como **todos os serviços usam a mesma conta de serviço**, o binding é o mesmo do da auth, e a auth perderia acesso ao `postgres-auth-connection` (instâncias novas não subiriam); as revisões de rollback perderiam acesso ao `realtime-apikey`. As envs voltaram ao `main.tf` (sem uso, inofensivas) com o aviso, e a remoção foi para a **B1**, depois de trocar os grants para um por secret (`moved` de uma chave e `removed { destroy = false }` das duplicadas).
  4. Testes: login, api-key, tempo real (relay), card, férias.
- **Deploy (2026-09-26 ~02:50–03:00 UTC)**: Terraform passo 1 aplicado (4 criados, 2 alterados); PRs #21 (back) e #14 (front) mergeados; deploys ok: `cime-auth-00023-49d` (subiu com os segredos da config), `cime-pullrequest-00025-klm`, `cime-web-00015-k5j`, relay republicado. Revisões de rollback: `cime-auth-00022-sv7`, `cime-pullrequest-00024-pf7`. Sem erros nos logs; relay 200; auth respondendo.

## Checklist de rotação (R1)
| Credencial | Onde está exposta | Trocada no provedor | Secret atualizado | Teste |
|---|---|---|---|---|
| Senha SMTP (`email-password`) | `appsettings` da auth (git) | ⬜ | ⬜ | ⬜ |
| Token GitHub (`github-token`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| PAT Azure DevOps (`azuredevops-pat`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Segredo JWT (`jwt-secret`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Chaves de IA | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Senha Web Deploy MonsterASP | conversa (0013) | ⬜ | ⬜ | ⬜ |
| Senha do `db70140` | conversa (0014) | — (banco será excluído) | — | — |
| Senha do `db70152` (premium, em uso) | conversa (2026-09-26) | ⬜ | ⬜ (`postgres-auth-connection` e `postgres-prform-connection`) | ⬜ |
| api-key pessoal do usuário (x-api-key) | conversa (0015, Q1) | ⬜ (gerar nova no app) | — | ⬜ |

## Decisões
- D1: duas ondas (código cedo; bancos/secrets depois do período de segurança).
- D2: remoção dos secrets de fallback só com "ok" explícito do usuário (ponto sem volta).
- D3: nenhum valor real em `appsettings` versionado.
- D4: API principal com uma chave de conexão só (`PrformDatabase`).
- D5: rotação antes de apagar.
- D6: tempo real em processo continua (dev), mas só com token.

## Log
- 2026-09-26 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0016`.
