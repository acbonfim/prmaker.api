# Feature 0013 — Relay de tempo real no MonsterASP

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0013` nos dois repos, a partir de `master` (worktrees `../prform.api-0013` e `../solvace.prform.web/prform-app-0013`).

## 1. Análise

### Backend
- **`Cime.BuildingBlocks.RealTime`** tem 263 linhas:
  - `RealTimeHub`: só `AddToGroup`/`RemoveFromGroup`.
  - `RealTimeNotifier`: `IHubContext` → `Clients.Group/All.SendAsync`.
  - `RealTimeApiKeyMiddleware`: chave fixa em header/query.
  - `RealTimeCorsPolicyProvider` e `RealTimeOptions` (`HubPath`, `ApiKey`, `AllowedOrigins`).
- Os 12 pontos de publicação usam só `IRealTimeNotifier` (`NotifyGroupAsync`/`NotifyAllAsync`). A 0012 (webhook do DevOps) também vai usá-lo. **Nenhum chamador muda.**
- `PluginRealTimeOptionsProvider` (plugin "Realtime Configurations") monta um `RealTimeOptions` novo copiando só 3 campos. Os campos novos precisam vir do ambiente.
- O payload hoje sai pelo protocolo JSON padrão do SignalR (System.Text.Json, camelCase). O relay repassa um `JsonElement` serializado com `JsonSerializerDefaults.Web`, então o JSON que chega ao front é o mesmo.
- **Cloud Run com `cpu_idle = true`**: fora de um request a CPU é estrangulada. Por isso a notificação ao relay precisa ser **aguardada dentro do request** (com timeout curto), e não fire-and-forget.
- `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.2` já é usado em `Cime.BuildingBlocks.Security`. O `System.IdentityModel.Tokens.Jwt` fica alinhado na 7.1.2.

### Front
- `WsService` concentra o SignalR: `startConnection`, `addToGroup`, `on/off`, reinscrição de grupos na reconexão. Hoje a URL é `environment.urlWs` e a chave vai em `environment.apiKeyWS`.
- Consumidores: `register.component` (grupo de configuração + grupo do card) e `card-timeline` (grupo da timeline). **Nenhum recarrega dados ao reconectar.** O `_reconnected` também dispara na primeira conexão, então não serve para isso.

### MonsterASP
- Suporta SignalR/WebSocket, .NET 8, Let's Encrypt em domínio próprio e Web Deploy (GitHub Actions oficial com `rasmusbuchholdt/simply-web-deploy`, runner Windows).
- É hospedagem compartilhada IIS: o app pode ser reciclado. O SignalR reconecta e o `WsService` reinscreve os grupos. O F2 garante que os dados sejam recarregados.

## 2. Solução

```
browser ──(1) GET /api/v1/RealTime/connection  (x-api-key)──▶ API (Cloud Run)
        ◀── { url, accessToken (JWT 10 min), expiresAt }
browser ══(2) wss://realtime.softhouse.app.br/ws?access_token=… ══▶ Relay (MonsterASP)
API ──(3) POST https://realtime…/publish  (X-Relay-Key) { group, event, payload } ──▶ Relay ──▶ grupo
```

### 2.1 Building block (`Cime.BuildingBlocks.RealTime`)
- `RealTimeOptions` ganha:
  - `Mode`: `InProcess` (padrão) ou `Relay`.
  - `RelayUrl`: base do relay.
  - `RelayKey`: chave servidor-a-servidor.
  - `TokenSigningKey`: HMAC-SHA256 dos tokens do browser, compartilhada com o relay.
  - `TokenLifetimeMinutes` (padrão 10) e `PublicHubUrl` (URL do hub para o browser; se vazia, usa `RelayUrl + HubPath` no modo Relay).
- **`RelayRealTimeNotifier`**: `HttpClient` tipado (timeout 3 s), `POST {RelayUrl}/publish`. Erro → log de aviso, nunca exceção.
- **`RealTimeTokenService`**: emite JWT (`iss = cime-api`, `aud = cime-realtime`, `sub` = usuário).
- `AddRealTimeService` escolhe o notifier pelo `Mode`. `UseRealTimeService` só mapeia o hub em processo no modo `InProcess`.
- No modo em processo, o middleware aceita a chave fixa (legado) **ou** um token válido. Assim o front novo funciona nos dois modos.

### 2.2 API
- `GET api/v1/RealTime/connection` (`[Authorize]`) → `{ url, accessToken, expiresAt }`.
  - `url` nula no modo em processo sem `PublicHubUrl`. Nesse caso o front usa `environment.urlWs`.
  - `accessToken` nulo quando `TokenSigningKey` não está configurada (dev).
- `PluginRealTimeOptionsProvider` preserva os campos novos (vêm do ambiente/secrets).

### 2.3 Relay (`CIME/modules/Cime.RealTime/src/Cime.RealTime.Relay`)
- Host ASP.NET Core 8 que reusa o `RealTimeHub` do building block.
- **`/ws`**: SignalR com `JwtBearer`. O token é lido de `access_token` na query (padrão do SignalR). Sem `TokenSigningKey` configurada, o hub fica aberto (dev).
- **`POST /publish`**: exige `X-Relay-Key` (comparação em tempo constante) e recebe `{ group?, event, payload? }`.
- **`GET /health`**: `{ status, time }`.
- CORS com `AllowedOrigins` e credenciais.
- Config em `appsettings.Production.json`, gerado no deploy a partir dos secrets do GitHub (nada de segredo no git).

### 2.4 Front
- `WsService`:
  - Busca `RealTime/connection` antes de conectar e usa `url` (ou `environment.urlWs`) e `accessTokenFactory` (token em cache até 1 min antes de expirar; renovado a cada reconexão).
  - Se o endpoint não existir (API antiga), cai no modo legado (`urlWs` + `apiKeyWS`). Isso deixa a ordem de deploy livre.
- `resynced` (novo): emitido **só quando a conexão volta** depois de ter caído. Os consumidores recarregam em silêncio:
  - `register`: configurações, registro (respeitando edição local não salva) e PRs do card.
  - `card-timeline`: `load(silent)`.

### 2.5 Infra
- Workflow `deploy-realtime.yml`: `dotnet publish` do relay (`win-x86`), gera `appsettings.Production.json` a partir dos secrets e faz Web Deploy. Dispara em push na `master` que toque o relay ou o building block, ou manualmente.
- Terraform (`cime-pullrequest`):
  - `timeout 300`, `session_affinity false`, `max_instances = var.max_instances` (sem hub em memória não precisa mais de 1).
  - Secrets `realtime-relay-key` e `realtime-token-signing-key`.
  - `plain_env`: `RealTime__Mode = Relay` e `RealTime__RelayUrl`.
- DNS: `realtime.softhouse.app.br` (CNAME para o site do MonsterASP) em `deploy/dns`.

## 3. Fases

| Onda | Fases |
|---|---|
| 1 | B1, B2, F1 (independentes) |
| 2 | F2 (depois de F1), I1 |
| 3 | Q1 (usuário + Claude) |

### B1 — Building block + endpoint de conexão (backend)
`RealTimeOptions` (campos novos), `RelayRealTimeNotifier`, `RealTimeTokenService`, escolha por `Mode`, middleware aceitando token, `RealTimeController`, `PluginRealTimeOptionsProvider`. Build da solução.

### B2 — Relay (backend)
Projeto `Cime.RealTime.Relay` (no `.sln`), hub com JwtBearer, `/publish`, `/health`, CORS. Teste local: relay + API em modo `Relay` (sem banco: só o relay e um cliente SignalR de teste), publicar e receber.

### F1 — `WsService` com token e URL da API (front)
`RealTime/connection`, `accessTokenFactory`, fallback legado. `ng build`.

### F2 — Recarregar ao reconectar (front)
`resynced` no `WsService`. Handlers em `register` e `card-timeline`.

### I1 — Deploy e infra (repo backend)
`deploy-realtime.yml`, Terraform (`main.tf`, `secrets.auto.tfvars.example`), DNS, `deploy/README.md` (seção SignalR atualizada).

### Q1 — Publicação e teste
**Pré-requisitos do usuário:** criar o site no MonsterASP (ou reaproveitar o `prformapi.runasp.net`, que hoje roda código antigo), ativar o Web Deploy e cadastrar os secrets no GitHub.

Ordem:
1. Publicar o relay e checar `/health`.
2. Domínio `realtime.softhouse.app.br` + Let's Encrypt.
3. `terraform apply` (secrets e env; o código antigo ignora as envs novas).
4. Merge do backend e do front.
5. Testar os eventos (salvar registro, timeline, PRs) em duas abas.
6. Conferir no Monitoring que o `billable_instance_time` caiu.
7. Remover o secret `realtime-apikey` e o `apiKeyWS` do front numa limpeza posterior.

**Rollback:** `RealTime__Mode = InProcess` (e voltar `timeout`/`max_instances` da `cime-pullrequest`).

## 4. Fora do escopo
- Backplane/Redis e várias instâncias do relay.
- Autorização por grupo.
- Desconectar abas inativas (não é mais necessário para custo).
