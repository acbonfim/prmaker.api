# Status 0106

- Branch: `feature/0106` (prform.api-0106 e prform-app-0106), a partir de origin/master.
- Merge: aguardando o ok do usuário.

| Fase | Estado | Notas |
|---|---|---|
| B1 — lições | ✅ | `GovernanceLesson` (L<n>: escopo por skill, quando → lição, por quê, origens, reincidência) e `GovernanceLessonSignal` (S<n>), migração `GovernanceLessons0106` com as 7 lições do 79047 (origem `seed`); reforço por semelhança (≥ 0,5 de palavras em comum) em vez de duplicar; recusa lição que libera merge/aprovar/senha; markdown injetado em `prmake_rules`/`Skills/rules` (`skill` filtra, 25 mais reincidentes) e no `prmake_case_context` (15, especialista); REST `Governance/lessons` (editar/aposentar/reativar = admin); MCP `prmake_lesson_record`, `prmake_lesson_signal_dismiss`, `prmake_lessons` |
| B2 — sinais | ✅ | item do especialista cancelado em ≤ 30 min, item reaberto com `redo`, entrega refeita (`prmake_work_task_redo`), pedido de mudanças em PR — chave única por acontecimento, best-effort |
| B3 — review | ✅ | `GitHubService.Reviews` direto na REST do GitHub (o Octokit 9 lê id de comentário como int — os ids reais passam do limite; o e2e pegou); MCP `prmake_pr_reviews` (só PR do card; linha nova × contexto × desatualizada), `prmake_pr_review_reply`, `prmake_pr_request_review`; `prmake_analysis_start` (E1); `changesRequested` nos links do plano e da sub-tarefa |
| B4 — fluxo | ✅ | migração `SeedReviewFlow0106`: tipos `pr-update`/`review-reply`, modelo `atender-review-pr`, `revisar-pr` com `timeline` (padrão "não") só sobre o semeado; gatilho no `SyncPullRequestsAsync` (changes requested novo → `PullRequestReviewHandler`: item pendente sem duplicar, aviso do PRMake na conversa, sinal) |
| B5 — skills | ✅ | especialista-do-caso (regras 12–16, ações, exemplo 31430), executar-item (tipos novos, `pr-review` só no diff, seção "Atender o review"), analisar-bug/us, gestão, validar-ambiente, README; scripts pedem as lições da skill (`--skill`, cache por skill) |
| F1 — front | ✅ | Governança → aba **Lições** (sinais em aberto com descartar, lista com reincidência/escopo/origem/cards, editar/aposentar/reativar); selo **Mudanças pedidas** no PR da etapa e da sub-tarefa |
| Q1 — testes | ✅ | prform 721 · executionplans 463 · governance 86 (9 novos de lições) · specs do front 10 · ng build ok |
| Q2 — e2e | ✅ | `.t0106` (banco `cime0106` copiado do 0103, auth falso, GitHub falso `fakegh.py`): `e2e.py` 0 falhas (semente nas regras, filtro por skill, reforço, recusa, sinal de cancelamento → lição, reaberto → sinal → descarte, admin aposenta/edita, contexto do caso, ferramentas no MCP); `e2e_review.py` 0 falhas (sync vê changes requested → item `atender-review-pr` pendente + aviso + sinal, sem duplicar; ler review, responder na thread, pedir revisão, PR de outro card recusado); telas em `shots/` |

## Decisões

- Lições no módulo Governança (mesmo lugar das regras e padrões) e injetadas no que os agentes já leem no início — nada de
  editar skill para a lição valer. Lição nunca sobrepõe regra fixa/governança (texto e recusa no domínio).
- Sinal ≠ lição: o PRMake só registra o acontecimento; quem generaliza é o próximo agente no card (grava com `signal` ou
  descarta com motivo). Lição gravada pelo agente reforça a parecida (conta a reincidência), as mais reincidentes vêm primeiro.
- Pedido de mudanças vira item **pendente** (nada muda código sem alguém clicar em Executar). Um por rodada: com item aberto
  do mesmo PR, só avisa. PR que já tinha pedido de mudanças antes do deploy não dispara (só a transição).
- PR já mesclado no destino: a mesma branch da regra + PR novo (nunca perguntar nome de branch).

## Log

- 2026-11-27 — feature implementada e testada (unidade + e2e local + telas). PRs abertos.
