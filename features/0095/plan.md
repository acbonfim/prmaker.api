# Plano 0095 — Ver tela ao vivo

| Fase | O quê | Onde |
|---|---|---|
| S1 | `env_browser.py`: Chrome com `--remote-debugging-port=0` (só loopback); sessão grava `cdpPort`, `targetId`, `card`, `task` (inclusive ao reaproveitar a sessão); `env.sh browser start` repassa `--card/--task` | `skills/validar-ambiente` |
| E1 | Executor 1.4.0: `LiveView` (TcpListener em 127.0.0.1:`liveViewPort`=47615): `GET /live/sessions` (só origens `liveViewOrigins`, CORS + Private-Network), `GET /live/{sid}/stream?t=` (MJPEG via `Page.startScreencast` numa conexão CDP própria, ≤5 quadros/s por espectador, liga com o 1º espectador e desliga com o último); Host só 127.0.0.1/localhost | `tools/Cime.ExecutionAgent` |
| B1 | `WorkItemLiveResponse.Mine` (dono do pedido/plano/item == quem vê; nunca cai em "quem vê") + teste | `Solvace.ExecutionPlans` |
| F1 | Botão `web` "Ver tela ao vivo" no título "O que está sendo feito" (só com `mine` + execução viva + sessão do card/sub-tarefa no executor local, consulta a cada 4 s); modal `LiveBrowserDialogComponent` com o MJPEG; `LiveBrowserService` trata a permissão de rede local (prompt → espera; denied → não pergunta) | front `work-items/` |
