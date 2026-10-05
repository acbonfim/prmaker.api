# Feature 0065 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0065` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Endpoint `Home/cards/by-numbers` | back | — | 1 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | 33194eb |
| F1 | `TabsService` + persistência + rotas com `data.tab` | front | — | 1 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | front fba8cc3 |
| F2 | `TabRouteReuseStrategy` + rolagem por aba | front | F1 | 2 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | front fba8cc3, fix DI (próximo commit) |
| F3 | `<app-tab-bar>` + layout do PageContainer | front | F1 | 2 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | front fba8cc3 |
| F4 | Rótulo do card (título, plano, PRs, amarelo) | front | B1, F3 | 3 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | front fba8cc3 |
| F5 | Integração com register/home/sino + dirty | front | F2, F3 | 3 | ✅ | Claude (sessão principal) | 2026-10-05 | 2026-10-05 | front (commit "tela de PR e Home integradas") |
| Q1 | Build, teste manual, regressão, PRs | ambos | todas | 4 | ✅ | Claude + usuário | 2026-10-05 | 2026-10-05 | ver notas |

## Decisões

Defaults em `spec.md` (D1–D12). Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1–D12 | ✅ implementadas como na spec | O usuário autorizou a execução e o merge; nenhuma decisão foi alterada. |

## Notas de handoff

### B1 ✅
`GET Home/cards/by-numbers?cards=a,b` (até 10, ordem pedida, qualquer card; card sem dados volta `registered=false`) — `HomeCardsService.GetByNumbersAsync` reaproveita `BuildAsync`. Testado no Postgres isolado.

### F1–F5 ✅
- `services/tabs.service.ts` (estado, limite, congelamento, persistência `prmake.tabs.v1.<externalId>`, restauração), `services/tab-route-reuse.strategy.ts`, `services/tab-card-info.service.ts`, `components/tab-bar/*`, `data.tab` nas rotas, `--cime-tabs-h` (30 px; 28 px no celular).
- **Achado no teste**: o `Router` depende da `RouteReuseStrategy`; injetar o `TabsService` (que injeta o `Router`) direto na estratégia dá NG0200 (tela em branco). A estratégia resolve o serviço sob demanda (`Injector`).
- **Achado na análise**: com duas telas de PR vivas, o `effect` da tela inativa copiaria a descrição/RC da aba ativa para o modelo local (risco de salvar texto de outro card). A tela de PR agora só reage ao estado compartilhado enquanto sua aba está ativa; guarda uma foto (`CardPrStateService.snapshot/restore`) ao sair e devolve ao voltar.
- Aba com edição não salva (`dirty`) não congela e pede confirmação ao fechar.
- Ctrl/Cmd+clique (ou botão do meio) num cartão da Home abre o card em aba nova. No macOS, Ctrl+clique é clique direito: use Cmd.

### Q1 ✅ (teste ponta a ponta, Chrome headless + API + Postgres isolado, 17 verificações)
Aba inicial e faixa de 30 px · abrir card na aba (nome `#card`, ícones de plano/PR, âmbar "aguardando você") · + abre Home · voltar à aba reanexa a mesma tela (sem recarregar) · Cmd+clique abre card em aba nova · duas telas de PR vivas com cards diferentes · limite de 10 e + desabilitado · F5 mantém abas/ativa (demais congeladas) · logout/login restaura · +11 min congela tudo menos a ativa · clicar numa congelada reativa pela URL · fechar aba. Celular (390 px) verificado por captura. `ng build` (dev e produção) e `dotnet build` ok.

**Limitações conhecidas**: sem integração com o DevOps (como no teste) a aba mostra "Pull Request" no lugar do título do card; o histórico do navegador é único (trocar de aba usa `replaceUrl`); requisições em andamento de uma tela que acabou de ficar inativa podem gravar no estado compartilhado (janela curta).

## Log

- 2026-10-05 — Spec, plano e status escritos; worktrees `prform.api-0065` e `prform-app-0065` criados a partir da `master`.
- 2026-10-05 — B1, F1–F5 implementadas e testadas ponta a ponta (Q1); PRs abertos e mesclados juntos (merge autorizado pelo usuário).
