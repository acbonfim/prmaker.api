# Feature 0079 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

| Fase | Descrição | Repo | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|
| B1 | Login registrado na auth | back | ✅ | Claude | 2026-11-04 | 2026-11-04 | 89a319c, 60221dc |
| B2 | Presença no hub (relay) | back | ✅ | Claude | 2026-11-04 | 2026-11-04 | 6f7fc99 |
| B3 | Lotes de uso + leituras de análise | back | ✅ | Claude | 2026-11-04 | 2026-11-04 | 43a91f5 |
| F1 | Rastreador de uso + presença no front | front | ✅ | Claude (agente) | 2026-11-04 | 2026-11-04 | front 081985b, f655f1d |
| F2 | Gestão de usuários com presença | front | ✅ | Claude (agente) | 2026-11-04 | 2026-11-04 | front 68b8675 |
| F3 | Análise de uso | front | ✅ | Claude (agente) | 2026-11-04 | 2026-11-04 | front 7978fba, 481fc0b, bfb941c |
| Q1 | Build, teste local, PRs, merge, deploy | ambos | 🟡 | Claude | 2026-11-04 | | |

## Log
- 2026-11-04 — feature criada (worktrees `prform.api-0079` / `prform-app-0079`); o usuário pediu para planejar,
  implementar e mesclar ao final sem perguntar.
- 2026-11-04 — B1–B3 prontos. Teste local (`.t0079/`, Postgres isolado na 55479, auth na 2023, API na 5083):
  - login com senha errada / usuário inexistente / sucesso → 3 linhas em `auth.LoginEvents` com motivo, IP (último
    salto do `X-Forwarded-For`) e user agent;
  - `POST Usage/batch` 204 (~0,3 s local, 2 consultas); o segundo lote da mesma aba soma contadores; tipo inválido
    descartado; `summary`/`logins`/`sessions`/`events`/`last-seen`/`users/{id}` 200 com os números esperados;
  - hub em processo (`presence.cjs`): token com `name`/`role`; admin recebe `presenceChanged`/`presenceRemoved` e o
    snapshot; usuário comum recusado no `WatchPresence`; `AddToGroup('presence:watchers')` recusado.
  - Armadilha: `pkill -f solvace.prform.api` derruba a auth junto (o caminho do worktree contém "prform.api").
- 2026-11-04 — F1–F3 prontas (agente no worktree do front); `ng build` ok. Teste ponta a ponta local (Chrome headless,
  login real pela tela): faixa "Online agora", selo no cartão e diálogo da sessão ao vivo (IP, navegador, SO,
  dispositivo, tela, idioma, fuso, rota, últimos logins); Análise de uso com indicadores, gráficos e tabelas batendo
  com a API; 400 px ok. Ajustes do teste: totais do usuário com logins/erros, mapa de calor = usuários ativos, lote ao
  ocultar a aba no máximo 1/min. `NG0303` do top-menu (`*ngIf` do selo DEV) já existia.
  - Datas de `auth.LoginEvents` são `DateTime.Now` da auth: no Cloud Run (UTC) batem; no dev local aparecem 3 h antes.
