# Status — Feature 0049

Branch `feature/0049` em `prform.api-0049` (API, skill) e `prform-app-0049` (front). Executor sem mudança (1.0.6 serve).

| Fase | Status |
|---|---|
| B1–B3 | ✅ concluída |
| S1 | ✅ concluída |
| F1 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-02 — B1: `ExecutionRequest.Phase` (gravada no claim) e `NewSession`; `PhaseOfAsync` decide também a sessão:
  na correção, sessão começada por pedido da análise (ou, sem pedido, presente no plano de análise) → `StartInNewSession`
  (o pedido sai sem `sessionId`; o executor 1.0.6 abre sessão nova com o `FreshPrompt`). `FreshPrompt` da correção pede
  `contexto-correcao`. `CreateAsync` do plano de correção registra a sessão em execução (`RunningSessionAsync`) em vez
  da do plano pai.
- 2026-10-02 — B2: comentário cria o pedido com `NotBefore` (+120 s, `gathering`); outro comentário empurra (máx. 3×
  desde o pedido); "Continuar" (resume-request ou "Continuar com Claude") e outra ação do usuário → `RunNow`. Achado no
  T1: o consumo final gravado depois do `finish` tocava `LastActivityAt` e a resposta/comentário dos 150 s seguintes não
  retomava o card — `RecordUsageAsync(..., sessionEnded: true)` não toca mais.
- 2026-10-02 — B3/S1/F1: chaves semeadas por migração (só as que faltam); skill com `contexto-correcao` (+`pull_plan`),
  resumo obrigatório no checkpoint de `propor-solucoes`, `prmake_file` direto no executor e no `ToolSearch` inicial;
  front com "Começar agora" e "correção numa sessão nova".
- 2026-10-02 — T1 (Postgres 55449, API local, worker por HTTP): análise → resposta → claim `phase=correction`,
  `sessionId=null`, `newSession=true`, prompt com `contexto-correcao`; plano de correção com a sessão nova e consumo
  sem linha de base (análise só com a dela); comentário → `gathering` + 204 no `next`; 2º comentário empurra; Continuar
  limpa; retomada seguinte continua a sessão nova. Card antigo (correção herdou a sessão da análise) → sessão nova;
  `ExecutorCorrectionNewSession=false` → retoma como antes. `contexto-correcao` mostra resumo, respostas e comentários.
- 2026-10-02 — Fix (card 75067): a correção em sessão nova rodou inteira no Opus. No `claude -p` a execução toda é uma
  vez só, e o `model: opus` da skill invocada por `/analisar-bug` vale por cima do `--model sonnet` até o fim (testado
  com skill de teste `model: haiku` + `--model sonnet`: 7/7 respostas no Haiku; lendo a SKILL.md com Read/cat: 6/6 no
  Sonnet). O prompt da correção agora manda ler a SKILL.md (`cat`) e não invocar a skill.
