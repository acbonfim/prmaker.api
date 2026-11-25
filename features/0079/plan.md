# Feature 0079 — Dados de uso dos usuários

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0079` a partir da `master` (worktree `../prform.api-0079`; no front, worktree
> `../solvace.prform.web/prform-app-0079` com `node_modules` linkado de `../prform-app-0019/node_modules`).

## Análise
- **Login**: `Cime.Auth` → `UserService.LoginUser` (usado por `v2/oauth/login` e pelo legado `User/Login`). Hoje só
  grava `DataUltimoLogin`. Sem forwarded headers (IP tem que vir do `X-Forwarded-For`). Migrações da auth rodam no job
  `cime-auth-migrate` do deploy quando `Migrations/` muda.
- **Tempo real**: o hub (`Cime.BuildingBlocks.RealTime.RealTimeHub`) só tem `AddToGroup/RemoveFromGroup`; roda no
  relay (MonsterASP, produção) ou em processo (dev). O token de conexão só leva `sub` (ExternalId). No modo em processo
  o middleware valida o token mas não preenche `Context.User`. O relay sobe pelo `deploy-realtime.yml` quando
  `Cime.BuildingBlocks.RealTime/**` muda.
- **API**: padrão a copiar = `AiUsageRecord` (0042): entidade com `Cut`, mapeamento inline no `DefaultContext`,
  gravação aguardada dentro da requisição (Cloud Run estrangula CPU depois da resposta). `AuthenticationContext` lê
  `auth.AspNetUsers` sem migrações.
- **Front**: `TabsService` já escuta `NavigationEnd`/`visibilitychange`/`pagehide` e é iniciado no
  `PageContainerComponent` (área logada); `data.tab.title` dá o nome amigável da tela. `WsService` (SignalR) com
  `addToGroup/on/off/_reconnected`. Não há biblioteca de gráficos (barras em CSS no `ai-usage-dialog`). Gestão de
  usuários = `pages/authenticated/user/manager` (cartões, signals). `sendBeacon` não manda cabeçalho → `fetch` com
  `keepalive` (x-api-key no cabeçalho; preflight já tem Max-Age de 2 h).

## Contratos

### Hub (relay / em processo)
- Token de conexão: claims `sub` (ExternalId), `name`, `role` (uma por papel). `RoleClaimType = "role"`,
  `NameClaimType = "name"`. O middleware em processo passa a preencher `context.User`.
- `UpdatePresence(report)` — qualquer conectado. `report = { sessionId, state: 'active'|'busy'|'background'|'away',
  visible, focused, screen, route, card, busy, lastInputAt, since, startedAt, fullName,
  client: { browser, os, device, screenSize, language, timeZone, appVersion } }`.
- `WatchPresence()` → `PresenceEntry[]` (só admin; senão `HubException`); entra no grupo `presence:watchers`.
  `UnwatchPresence()`.
- Eventos para o grupo: `presenceChanged` (`PresenceEntry`) e `presenceRemoved` (`{ connectionId, userId }`).
- `PresenceEntry = { connectionId, userId, userName, fullName, sessionId, state, visible, focused, screen, route, card,
  busy, lastInputAt, since, startedAt, connectedAt, updatedAt, ip, userAgent, client }`.
- `AddToGroup` recusa grupos que começam com `presence:`.

### API (`api/v1/Usage`)
- `POST batch` (qualquer logado) — `{ session: { id, startedAt, screen, route, foregroundMs, backgroundMs, awayMs,
  ended, client: {...} }, events: [{ at, kind, screen, route, card, name, detail, durationMs, status }] }` → 204.
  Até 500 eventos; `kind ∈ view|click|action|error|slow|track|login|logout`. Upsert da sessão (contadores = maior
  valor recebido) + insert dos eventos num `SaveChanges`. IP = último salto do `X-Forwarded-For`; user agent do
  cabeçalho.
- Admin (`[Authorize(Roles = "admin")]`), período `from`/`to` (ISO, padrão 7 dias), `tzOffsetMinutes`:
  - `GET summary?from&to&user&screen&tzOffsetMinutes` → `{ kpis, days, heatmap, screens, actions, errors, clients,
    users }`.
  - `GET logins?from&to&user&success&page&size` → `{ items, total }` (lê `auth.LoginEvents`).
  - `GET sessions?from&to&user&page&size` → `{ items, total }`.
  - `GET events?from&to&user&session&kind&screen&search&page&size` → `{ items, total }`.
  - `GET last-seen` → `[{ userExternalId, lastSeenAt, lastScreen, lastIp, lastLoginAt }]`.
  - `GET users/{externalId}` → `{ logins, sessions, events, totals }` (30 dias).

### Auth
- Tabela `auth.LoginEvents` (`Id`, `At`, `UserId?`, `UserExternalId?`, `UserName`, `Success`, `Reason`, `Ip`,
  `UserAgent`), gravada em todo `LoginUser`. `UserListItemDto.LastLoginAt` (`DataUltimoLogin`).

## Fases
| Fase | Descrição | Depende |
|---|---|---|
| B1 | Auth: `LoginEvent` + migração + registro em todo login (motivo, IP, user agent) + `LastLoginAt` na listagem | — |
| B2 | Tempo real: claims `name`/`role` no token, principal no middleware em processo, `PresenceTracker` + métodos do hub, bloqueio do grupo `presence:`, registro no relay | — |
| B3 | API: `UsageSession`/`UsageEvent` + migração, `UsageController` (lote + leituras admin), `LoginEvents` no `AuthenticationContext`, retenção de 180 dias | — |
| F1 | Front: `UsageTrackerService` (sessão por aba, telas, cliques, ações/erros pelo interceptor, ocupado, ausente, lotes com `keepalive`) + presença no hub + login/logout | B2, B3 (contrato) |
| F2 | Gestão de usuários: selo de presença, faixa "Online agora", filtro só online, painel do usuário (abas ao vivo, logins, sessões, caminho) | F1 |
| F3 | Análise de uso (`auth/user/usage`): filtros, indicadores, gráficos SVG, tabela por usuário e tabelas paginadas; menu e alternância na gestão | F1 |
| Q1 | Build dos dois lados, teste local ponta a ponta (`.t0079/`), PRs, merge e deploy (API, auth + job de migração, relay, front) | todas |

Ondas: B1, B2, B3 em paralelo com F1–F3 (o front segue o contrato acima).
