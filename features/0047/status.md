# Status — Feature 0047

Branch `feature/0047` em `prform.api-0047` (API, executor, skill) e `prform-app-0047` (front).

| Fase | Status |
|---|---|
| B1–B3 | ✅ concluída |
| E1 | ✅ concluída (executor 1.0.6) |
| S1 | ✅ concluída |
| F1 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-02 — B1/B2: `ExecutionModelUsage` em `ExecutionSession.Models`/`BaseModels` (JSON da sessão; migração só do
  modelo do EF), `NetModelUsage` (sessão antiga entra no modelo dela), `usage.models` no plano e `models` (média por
  plano) no relatório. B3: `PhaseOfAsync` na fila (correção = plano de correção aberto, `propor-solucoes` concluída ou
  todas as perguntas dela respondidas) e `claim.model` pela "Skills Configurations" (`ExecutorAnalysisModel=opus`,
  `ExecutorCorrectionModel=sonnet`, seed só das chaves que faltam).
- 2026-10-02 — E1: executor 1.0.6 passa `--model`, grava o modelo do `system/init` (o `modelUsage` de sessão retomada é
  acumulado) e manda o transcript por modelo. S1: `model: opus` na `analisar-bug`; `usage` por modelo; `<synthetic>`
  não vira o modelo da sessão (skill e executor). F1: estimativa = soma de cada modelo pelo seu preço; painel com uma
  linha por modelo.
- 2026-10-02 — T1 (Postgres 55447, executor 1.0.6 real + Claude falso): pedido de análise → `phase=analysis`,
  `model=opus`, Claude chamado com `--model opus`, pedido gravado com `claude-opus-5-5`; resposta do usuário → retomada
  `phase=correction`, `model=sonnet`; correção na mesma sessão mostra só o Sonnet (linha de base por modelo), análise só
  o Opus, relatório por modelo, custo por pedido (0,90 e 0,50). Tela: plano com Opus + Sonnet → Sonnet US$ 0,44 +
  Opus US$ 0,71 = US$ 1,15 (pela regra antiga, tudo no Sonnet: US$ 0,79).
- Limitação conhecida: execução NOVA do executor já na correção (sessão perdida na máquina) chama `/analisar-bug`, e o
  `model: opus` da skill vale naquela primeira resposta.
