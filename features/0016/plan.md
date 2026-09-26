# Feature 0016 — Limpeza dos bancos, do código e da configuração legados

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0016` (worktree `../prform.api-0016`), a partir de `master`. Toca o backend e o front (`../solvace.prform.web/prform-app`).
> **Pré-requisitos**: 0014 em produção (desde 2026-09-26) e **0015 em produção** (virada da API principal).

## 1. Inventário (levantado em 2026-09-26)

### Bancos (MonsterASP)
| Banco | Uso hoje | Fallback até | Destino |
|---|---|---|---|
| SQL Server `db30567` | nenhum (auth foi para o Postgres) | ~2026-10-26 (30 dias da virada da 0014) | backup final + excluir no painel |
| MySQL `db31021` | API principal (até a 0015); tabelas `AspNet*` legadas de um "QA" antigo da auth | 30 dias depois da virada da 0015 | backup final + excluir no painel |

### Pacotes de provider
| Projeto | Pomelo (MySQL) | EF SqlServer | EF Sqlite | SqlClient |
|---|---|---|---|---|
| `solvace.prform.infra` | ✓ | ✓ | ✓ | |
| `solvace.prform.api` | | ✓ | ✓ | |
| `solvace.github.infra` | ✓ | ✓ | ✓ | |
| `solvace.azure.infra` | ✓ | ✓ | ✓ | |
| `solvace.vacations.infra` | ✓ | | | |
| `solvace.timeline.infra` | ✓ | | | |
| `tools/Cime.*DataMigrator` | (MySqlConnector na 0015) | | | ✓ |

- **Sqlite não é usado** em lugar nenhum. GitHub e Azure não têm `DbContext`: os providers são sobra.
- O `Directory.Build.props` fixa a família `System.IdentityModel` em 7.1.2 por causa do SqlClient (conflito 6.35 × 7.1.2). Sem o SqlClient, a fixação pode ficar ou sair: decidir no upgrade para o .NET 10.

### Configuração e secrets
- **Terraform/Secret Manager (fallback)**:
  - `sqlserver-auth-connection`: `cime-auth` `ConnectionStrings__DefaultConnection` e `cime-pullrequest` `ConnectionStrings__AuthenticationConnection`;
  - `mysql-default-connection`: `cime-pullrequest` `ConnectionStrings__DefaultConnection`;
  - `realtime-apikey`: `RealTime__ApiKey` (chave legada do hub em processo).
- **`appsettings*.json` versionados com valores reais**:
  - API principal: `DefaultConnection` (MySQL), `Secret` (JWT), `RealTime.ApiKey`, `AzureDevOps.PersonalAccessToken`, `GitHub.Token`, várias `ApiKey` de IA, `GITHUB_TOKEN`;
  - auth: `Email.Password` (SMTP); `appsettings.QA.json` (connection do `db31021`).
- **Duas chaves para o mesmo database** depois da 0015: `AuthDatabase` (auth e `AuthenticationContext`) e `PrformDatabase` (os 3 contextos do PR), ambas no `db70140`.

### Tempo real (legado da 0013)
- Modo em processo: `RealTimeApiKeyMiddleware` aceita a chave fixa `123456789` além do token; `PluginRealTimeOptionsProvider` lê `ApiKey`/`AllowedOrigins`/`HubPath` do plugin "Realtime Configurations".
- Front: `environment.apiKeyWS` e o fallback legado do `WsService` (API sem `RealTime/connection`), que não existe mais em produção.

### Arquivos e docs obsoletos
- `CIME/modules/Cime.Auth/src/bitbucket-pipelines.yml` e `CIME/modules/Cime.Auth/src/README.md` (pipeline antigo), `scpt.sql` (script MySQL que criou as tabelas legadas), `appsettings.QA.json`, `Cime.Auth.sln` (a avaliar: a auth fora da solução principal).
- `deploy.md` na raiz (build para publicar no MonsterASP, substituído pelo `deploy/README.md`).
- `CLAUDE.md`: descreve `DefaultContext` MySQL, `AuthenticationContext` SQL Server, comandos `dotnet ef` do MySQL e "Bitbucket Pipelines".
- `deploy/README.md`: notas de SQL Server/MySQL e a seção de SignalR antiga.
- `tools/Cime.Auth.DataMigrator` / `tools/Cime.DataMigrator`: só servem enquanto houver banco antigo.

### Credenciais expostas (rotação)
Estão no histórico do git e/ou nas conversas: senha SMTP, token do GitHub, PAT do Azure DevOps, segredo JWT, chaves de IA, senha do Web Deploy do MonsterASP, senha do `db70140`, e as senhas do MySQL/SQL Server, que morrem com a exclusão dos bancos.

## 2. Decisões
- **D1 — Duas ondas**:
  - **Onda A (código)**: pode ir assim que a 0015 estiver estável (~1 semana). O rollback segue possível: as revisões antigas do Cloud Run continuam lá com os secrets de fallback.
  - **Onda B (bancos/secrets)**: só depois do período de segurança de cada banco.
- **D2 — Ponto sem volta explícito**: a fase B1 remove os secrets/envs de fallback. Depois dela, voltar para uma revisão antiga não funciona mais. Exige "ok" do usuário na hora.
- **D3 — Segredos fora dos `appsettings.json`**: os valores reais saem dos arquivos versionados. Produção lê do Secret Manager (já é assim); Development usa `appsettings.Development.json` com valores locais/placeholder e *user-secrets* para chaves pessoais de integração. O histórico do git continua com os valores antigos, por isso a rotação (D5) é obrigatória.
- **D4 — Uma chave de conexão**: a API principal passa a usar `PrformDatabase` também no `AuthenticationContext` (mesmo database, schema `auth`). A auth mantém `AuthDatabase`. Um usuário do Postgres por módulo fica como melhoria futura.
- **D5 — Rotação antes de apagar**: cada credencial listada é trocada no provedor (GitHub, Azure DevOps, MonsterASP, provedores de IA, SMTP) e atualizada no Secret Manager/GitHub Secrets. O JWT (`jwt-secret`) invalida os tokens em uso: fazer fora do horário.
- **D6 — Tempo real**: mantém o modo em processo (útil no dev, sem subir o relay), mas **só com token**. Sai a chave fixa (middleware, `RealTime.ApiKey`, secret `realtime-apikey`, `apiKeyWS` e fallback legado do front). O plugin "Realtime Configurations" fica só com `AllowedOrigins` para o modo em processo; `ApiKey`/`HubPath` saem.

## 3. Fases

| Onda | Fase | Quando |
|---|---|---|
| A | C1, C2, C3, C4 (paralelas) | 0015 estável (~1 semana após a virada) |
| A | C5 | depois de C1–C4 |
| B | R1 (rotação) | qualquer momento; antes de B3 |
| B | B1 → B2 → B3 → B4 | SQL Server: a partir de ~2026-10-26; MySQL: 30 dias após a 0015 |

### C1 — Pacotes e providers
- Remover Pomelo, EF SqlServer e EF Sqlite de todos os projetos.
- `AuthenticationContext` na `PrformDatabase` (D4).
- Build + testes de fumaça locais com Postgres Docker.

### C2 — Configuração
- Tirar os valores reais dos `appsettings*.json` (D3).
- Remover as chaves mortas: `DefaultConnection`/`AuthenticationConnection` da API principal, `appsettings.QA.json` da auth, `GITHUB_TOKEN`/`GITHUB_OWNER` soltos, `RealTime.ApiKey`.
- `appsettings.Development.json` apontando para o Postgres local da 0015 (docker-compose).
- README curto de "como rodar local" (user-secrets para tokens pessoais).

### C3 — Tempo real legado (backend + front)
- Backend: middleware só com token; `RealTimeOptions.ApiKey` e leitura do plugin para `ApiKey`/`HubPath` saem.
- Front (`prform-app`): `apiKeyWS` e o fallback legado do `WsService` saem; a `url` vem sempre do `RealTime/connection`.
- Teste: duas abas, eventos e reconexão, em produção (relay) e local (em processo com token).

### C4 — Arquivos e docs
- Remover `bitbucket-pipelines.yml`, `scpt.sql`, o `README.md` antigo da auth e o `deploy.md` da raiz.
- Avaliar se o `Cime.Auth.sln` sai (auth na `Solvace.Master.sln`).
- Atualizar o `CLAUDE.md` (Postgres, schemas, comandos `dotnet ef` com Npgsql, GitHub Actions/Cloud Run, relay) e o `deploy/README.md` (tirar notas de SQL Server/MySQL/SignalR antigo).
- Atualizar as memórias (`dev-db-is-prod` fica obsoleta).

### C5 — Deploy da onda A
- PR(s) back e front → deploy.
- Conferir login, api-key, card, férias, timeline e tempo real.
- As revisões antigas continuam disponíveis para rollback (os secrets de fallback ainda existem).

### R1 — Rotação de credenciais (D5)
Checklist no status, uma linha por credencial: trocada no provedor, atualizada no Secret Manager/GitHub Secrets, deploy/teste ok. O usuário troca nos painéis; o Claude atualiza Terraform e secrets e valida.

### B1 — Ponto sem volta: secrets e envs de fallback (confirmação do usuário)
Terraform: remover `sqlserver-auth-connection`, `mysql-default-connection` e `realtime-apikey`, e as envs `ConnectionStrings__DefaultConnection` (auth e PR), `ConnectionStrings__AuthenticationConnection` e `RealTime__ApiKey`. `plan` revisado antes do `apply`.

### B2 — Backups finais
- MySQL `db31021`: `mysqldump` completo (inclui as tabelas `AspNet*` legadas).
- SQL Server `db30567`: backup `.bak` pelo painel.
- Arquivos guardados pelo usuário fora do repo, com data e checksum anotados no status.

### B3 — Excluir os bancos no painel do MonsterASP (usuário)
Primeiro o SQL Server; o MySQL depois do período da 0015. Conferir no Monitoring e nos logs que nada tenta conectar neles (nenhum erro nas APIs).

### B4 — Remover o migrador
`tools/Cime.DataMigrator` (e o `Cime.Auth.DataMigrator`, se ainda existir) sai do repo; o runbook da virada vira nota histórica no `deploy/README.md`.

## 4. Riscos
- **Apagar fallback cedo demais**: D1/D2 (duas ondas, ponto sem volta explícito).
- **Quebrar o dev local** ao tirar os valores dos `appsettings`: C2 entrega o docker-compose e o README de setup.
- **Rotação do JWT** derruba sessões: fazer fora do horário e avisar.
- **Algo ainda lendo o MySQL/SQL Server** sem ninguém saber (skill, script, job): antes da B3, conferir as conexões no painel do MonsterASP e buscar as connection strings antigas no repo e nas skills (`~/.claude/skills`).

## 5. Fora do escopo
Upgrade para o .NET 10 (feature própria, logo depois da onda A), usuários de banco por módulo, e outras limpezas de código não relacionadas a banco/config (ex.: pacote `Microsoft.AspNetCore.Identity` 2.2.0 na auth).
