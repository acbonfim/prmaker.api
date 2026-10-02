# Status — Feature 0044

Branch `feature/0044` em `prform.api-0044` (backend + executor) e `prform-app-0044` (front).

| Fase | Status |
|---|---|
| B1–B5 | ✅ concluída |
| A1 | ✅ concluída (executor 1.0.3) |
| F1–F2 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-02 — avaliação do card 69795 pelo transcript local: 42 respostas, saída 22.019, cache lido 1.859.702, cache
  escrito 123.191, entrada nova 84; custo real US$ 0,68 + 0,55 + 0,56 = 1,80 (a tela somava 3,71).
- 2026-10-02 — teste local (Postgres 18 isolado, executor 1.0.3 publicado, Claude falso com custo acumulado e transcript
  com respostas repetidas no streaming): pedidos 0,68 / 0,55 / 0,57 com as partes; plano de análise com 24 respostas
  (só o dele), correção com 18 (15 + 3, linha de base descontada); relatório analysis 24 × correction 18 com as partes;
  "Gasto hoje" US$ 1,80; leitor do transcript do executor conferido no transcript real do 69795 (42/84/22.019/1,86 M/
  123.191). Backfill: 0,68/1,23/1,80 → 0,68/0,55/0,57 e sessão que recomeçou mantém o valor. Popovers no plano,
  pedidos, relatório e consumo de IA (desktop e 420 px).
