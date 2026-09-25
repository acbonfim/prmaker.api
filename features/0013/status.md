# Status — Feature 0013 (Relay de tempo real no MonsterASP)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0013` no backend (`../prform.api-0013`) e no front (`../solvace.prform.web/prform-app-0013`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Building block (modo Relay, token) + `GET RealTime/connection` | 1 | — | ✅ concluída | Claude | `01a2b66`, `a65c1f1` |
| B2 | Projeto `Cime.RealTime.Relay` (hub, `/publish`, `/health`) | 1 | — | ✅ concluída | Claude | `66a9eba` |
| F1 | `WsService` com token e URL vindos da API (fallback legado) | 1 | — | ✅ concluída | Claude | front `94ba3e4` |
| F2 | Recarregar dados ao reconectar (`resynced`) | 2 | F1 | ✅ concluída | Claude | front `08276d7` |
| I1 | Workflow de deploy no MonsterASP, Terraform, DNS, README | 2 | B1, B2 | ✅ concluída | Claude | `a65c1f1` |
| Q1 | Publicação e teste | 3 | todas | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Relay próprio no MonsterASP em vez de Ably/Pusher/Firebase: custo zero (hospedagem já paga), sem fornecedor novo, sem dependência de GCP, mesmo SignalR (usuário, 2026-09-25: "se tiver algo que possa subir lá já zeraria custos").
- Token de 10 min emitido pela API (JWT HS256, chave compartilhada API↔relay) no lugar da chave fixa do front.
- Notificação ao relay aguardada no request, timeout 3 s, sem exceção (Cloud Run estrangula CPU fora do request).
- Modo `InProcess` mantido para dev e rollback.

## Pendências do usuário (para o Q1)
- Site no MonsterASP: criar um novo ou reaproveitar o slot do `prformapi.runasp.net` (código antigo, dá 500 no `POST /PullRequest`).
- Ativar o Web Deploy no painel e cadastrar no GitHub (`acbonfim/prmaker.api`): `MONSTER_WEBSITE_NAME`, `MONSTER_SERVER_COMPUTER_NAME`, `MONSTER_SERVER_USERNAME`, `MONSTER_SERVER_PASSWORD`, `REALTIME_RELAY_KEY`, `REALTIME_TOKEN_SIGNING_KEY`.

## Notas da implementação
- **Testado localmente** (relay rodando + console C# no scratchpad, 12/12): publish sem `X-Relay-Key` → 401; conexão sem token, com token de outra chave ou expirado → 401; `RealTimeConnectionService` no modo Relay devolve `url` + token; WebSocket direto e via negotiate (como o browser); evento no grupo com payload camelCase idêntico ao SignalR em processo (`{"cardNumber":"12345","action":"register-saved","entityId":7}`); `NotifyAll` sem payload; evento de outro grupo não vaza; relay fora do ar não lança exceção. CORS do preflight com a origem do app ok.
- **Cliente JS do front** (`@microsoft/signalr` 9.0.6, o mesmo do app) conectou no relay por WebSocket com `accessTokenFactory` e recebeu o evento publicado; sem token → 401 no negotiate.
- `dotnet publish -r win-x86` gera o `web.config` (AspNetCoreModuleV2, in-process), como o MonsterASP espera.
- **Não testado**: a API real (banco = produção) e o app no browser. A tela só foi compilada (`ng build`). Validar no Q1.
- O `WsService` decide token × legado na primeira conexão da sessão: API antiga (404) → legado; API nova sem `TokenSigningKey` → URL da API/env + chave legada. Por isso a ordem de deploy de API e front é livre.
- Reconexão automática agora não desiste (0, 2, 10, 30 s e depois a cada 60 s). Com o hub fora do Cloud Run, conexão parada não custa nada.
- `deploy/dns/main.tf` veio da branch `migracao-gcp` (era a pendência "commitar `deploy/dns/`") com o CNAME `realtime` opcional. O `terraform` não está instalado nesta máquina: instalar (ou usar `gcloud`) no Q1.

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- No worktree do front, `node_modules` é um symlink para `../prform-app/node_modules`. Remover antes de commitar (ou não adicioná-lo).
- Banco de dev = produção: não rodar a API local contra o banco. O teste do relay (B2) não precisa de banco.

## Log
- 2026-09-25 — Planejamento: spec, `plan.md` e `status.md` criados; worktrees `feature/0013` criadas nos dois repos.
- 2026-09-25 — B1, B2, F1, F2 e I1 implementadas e testadas localmente (relay + cliente C# e JS). Falta o Q1 (site no MonsterASP, secrets, terraform apply, merge e teste).
