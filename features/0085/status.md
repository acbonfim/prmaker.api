# Status — Feature 0085

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Domínio, migração, API de itens, sync plano→item, cancelar com desfazer | concluída | agente B1 | e784fe9, 457f397, df0f63d | migração `WorkItems0085`; tipo do item vem do request (front manda `type`); dependências lazy p/ evitar ciclo na DI; sem projeto de testes (18 casos num console descartável) |
| B2 | Fila/executor 1.3.0, MCP, regras, seeds, especialista no servidor | concluída | agente B2 | 706a346…a237e1b (11) | `GET Skills/rules`, `GET WorkItems/catalog`, `POST Azure/card/{id}/actions/set-state`, MCP `WorkItemMcpTools`, `SeedWorkItems0085`, executor 1.3.0; fechar PR = rota existente `PUT PullRequest/{card}/github/{id}/status` |
| F1 | Página Bancada (rota, cabeçalho, lateral, plano, mobile, tour) | concluída | agente F1 | front fd3d8b5, e20af48, f808dd2 | tour 13 passos; aba em evidência automática (itens × plano); monta PRs/horas do F3 |
| F2 | Itens de trabalho no front (lista, modais, serviço) | concluída | agente F2 | front 71247a1, d079763, c90ea38, 2ced36e, 8aa9730 | 9 specs do modelo ok; "Pedir ao especialista" = mensagem `@especialista` no CaseChat; reordenar por setas |
| S1 | Skill especialista-do-caso (regras, itens, sem duplicar) | concluída | agente S1 | 5196d38 | `prmake-case.sh rules|items|catalog` (cache 10 min em dados/); testado com API falsa |
| F3 | PRs/ações GitHub+DevOps e horas do card na Bancada | concluída | agente F3 | front 586de17 | `CardPrStateService` local (providers do componente) — singleton da tela de PR intocado; chips de branch pelas regras; 18 specs ok |
| S2 | Skills executar/desfazer item, analisar-us, analisar-bug | concluída | agente S2 | 983f029, 3a29d9b | `prmake-plan.sh rules|work-items|work-task|close-pr|delete-branch`, `open-pr/worktree --item`; Kind `analisar-us`; testado com API/git falsos |
| BA | Ajustes: item automático da análise, `GET WorkItems/{n}/live`, tempo real do pedido do item | concluída | agente BA | 8c671ff, 49d61f5 | 81 testes executionplans; item da análise fora da checagem de duplicidade |
| FA | Ajustes: timeline em botão, lateral = Especialista, tags do card, robô/conexão, sugestões, sugerir análise inicial | concluída | agente FA | front d43fa4c | `workbench-facts.ts` (cópia do `infoFacts`); tour v2 |
| FB | Ajustes: ▶ e sessão de execução ao vivo ao lado (abas, sub-tarefas e feed em tempo real) | concluída | agente FB | front bf703a6 | `app-work-item-execution`; 17 specs |
| Q1 | Build + e2e local + revisão do contrato | concluída (local) | Claude | b5acd3a, 7217cf1, front 5b674b8 | ver Log; roteiros em `e2e/` (harness `.t0085`, Postgres 55485) |

## Log
- 2026-11-05 — spec, contexto mapeado, plano com contrato; agentes disparados em paralelo.
- 2026-11-05 — pedido 2: área de PRs (ver/abrir manual) com ações GitHub/DevOps e seção de horas (Bug × média de bugs; US planejado × realizado) → agente F3; médias por tipo semeadas pelo B2.
- 2026-11-05 — Q1 (harness `.t0085`: Postgres 18 isolado 55485 com prform+execution+auth migrados, auth falsa 8099, API 5083,
  front dev em localhost:4200, Chrome headless). Testado pela API: catálogo/regras (`hotfix/77434-rv`), criar por modelo,
  409 duplicado (REST e MCP com mensagem do item existente), `allowDuplicate`, editar só pending (409 `notEditable` depois
  de executar), "criar novo a partir deste", executar dois itens em paralelo (2 vagas `work-item`; executor 1.3.0 recebe
  os dois prompts), sub-tarefa start/complete com `undo`, cancelar com desfazer → `work-item-undo` na fila, plano de
  correção → item automático (uma sub-tarefa por etapa) com sincronização ao vivo, `POST ExecutionPlan/{id}/cancel` com
  desfazer. Tela: lista expansível, filtros, expandir/recolher todos, modal do item (abas), editor com modelos, aba
  Plano, celular. Testes: executionplans 63, prform 151, executor 22. Build front dev+prod ok.
  Correções do Q1: (1) números das sub-tarefas saíam ao contrário (#4,#3,#2) — default do banco segue a ordem de insert
  do EF; agora o repositório numera em ordem de leitura; (2) rota `tasks/{n}/{action}` dava 404 (`action` é valor de
  rota reservado do MVC) → `{verb}`; (3) a Bancada sumia quando o DevOps falhava — agora mostra aviso e segue com os
  dados do PRMake; (4) `close-pr` sem rota inventada e `devops set-state` no `prmake-plan.sh` para o desfazer.

## Como funciona
- Tela: `auth/workbench?card=<n>[&item=<número>][&focus=items|plan|chat|timeline]`. A tela de PR (`auth/register`) não mudou.
- Numeração: sequência `execution.work_item_number_seq` (1…9999999) compartilhada por itens e sub-tarefas.
- Regras para o especialista/skills: `GET Skills/rules?card=` / MCP `prmake_rules`; cache em `~/.prmake/cards/<card>/dados/regras.md`.
- `-rv`: `BranchSuffixByTarget` (Skills Configurations). `PromotionBranchPattern` virou `{branch}{suffix}` (só se ainda era o padrão).
- Médias de horas por tipo: `AssistantAverageHoursBug` / `AssistantAverageHoursUserStory` (começam com o valor de `AssistantAverageHours`).

## Pendências
- Validar com executor/Claude reais: `executar-item-de-trabalho` (PR para release-version no 77434), `desfazer-item-de-trabalho`, `analisar-us` numa US real, especialista criando item sem duplicar.
- Ajustar as médias por tipo em Skills Configurations (hoje iguais à média geral).
- `ng test` do projeto não compila por specs antigos (`async` de `@angular/core/testing`) — anterior à 0085.
- 2026-11-05 — 0085 em produção (back #155, front #85, mesclados juntos com o ok do usuário; deploys ok).
- 2026-11-05 — ajustes pedidos ao ver as capturas (`ajustes.md`): BA/FA/FB em paralelo na `feature/0085-ajustes`; builds
  dev/prod ok, testes 81/151/22; capturas `fa-*.png` no harness (card simulado no puppeteer — sem DevOps local).
- 2026-11-05 — ajustes (#156/#86) em produção: aba Card + horas ao lado das abas (FC), horas da US liberadas no
  backend (`LoadCardAsync` em effort/log-hours).
- 2026-11-05 — rodada 3 (`feature/0085-r3`): BB (`GET Azure/card/{id}/states`, `GET GitHub/branches`, kinds
  ticket/notify + `input/options` no catálogo, migração `SeedWorkItemKinds0085b`), FD (editor com autocomplete de
  repositório/branch/destino, estados sugeridos, commits), FE (aba "Execução" por item — sem "v1/v2" na Bancada; inputs
  aditivos `planId`/`hideVersions`/`itemNumber` no app-execution-plan; robô do cabeçalho com popover próprio; horas do
  assistente escondidas em toda a Bancada). Testes 81/164/22; builds ok; tela de PR igual (captura antes/depois).
- 2026-11-05 — análise somente leitura das ações recorrentes (30 cards) em `ferramentas-analise.md` → base das
  ferramentas do especialista (rodada 4).
