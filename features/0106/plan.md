# Plano 0106

| Fase | O quê | Depende |
|---|---|---|
| B1 | Lições no módulo Governança: `GovernanceLesson` + `GovernanceLessonSignal`, migração, repositório, `GovernanceLessonsApplication` (gravar com reforço por semelhança, listar, editar, aposentar; sinais), markdown injetado em `prmake_rules`/`Skills/rules` e `prmake_case_context`, REST `Governance/lessons`, MCP `prmake_lessons`, `prmake_lesson_record`, `prmake_lesson_signal_dismiss`; lições iniciais do 79047 semeadas | — |
| B2 | Sinais automáticos: item do especialista cancelado em ≤ 30 min, entrega refeita (`redo`), item reaberto com `redo`, pedido de mudanças em PR | B1 |
| B3 | PR review: `IGitHubService` lê reviews/comentários (linha nova ou contexto), responde comentário, pede revisão; MCP `prmake_pr_reviews`, `prmake_pr_review_reply`, `prmake_pr_request_review`; `prmake_analysis_start` (E1); `ExecutionLinkResponse`/`WorkTaskLinkResponse` com `changesRequested` | — |
| B4 | Fluxo do pedido de mudanças: modelo `atender-review-pr` + tipos `pr-update`/`review-reply` (migração), modelo `revisar-pr` com `timeline` opcional; gatilho no `SyncPullRequestsAsync` (changes requested novo → item pendente + mensagem do PRMake na conversa + sinal) | B1, B3 |
| B5 | Skills: especialista-do-caso, executar-item-de-trabalho, analisar-bug, analisar-us, especialista-de-gestao, validar-ambiente, README; instruções do MCP | B1–B4 |
| F1 | Front: aba Lições na Governança (lista, reincidência, origem, editar/aposentar/reativar, sinais pendentes); selo "Mudanças pedidas" no PR da etapa/sub-tarefa | B1, B3 |
| Q1 | Testes de unidade (lições: reforço/limites/markdown; sinais; reviews; gatilho; modelos) | B1–B4 |
| Q2 | e2e local (API + banco isolado): gravar/reforçar lição, sinal → lição, `prmake_rules` com lições, gatilho do review com GitHub falso | todas |
