# Plano — Feature 0084 (só front; docs/e2e aqui)

| Fase | O quê | Depende |
|---|---|---|
| F1 | Tour genérico: `TourStep.prepare` (o host muda a tela antes do passo e o tour espera o elemento), `requires` (passos de admin), `TourHost.hasCard` opcional (tela de uma fase só), `help` (subtítulo/dicas do guia por tour), contador sem os opcionais pulados | — |
| F2 | `base-solvace.tour.ts` (21 passos) + âncoras `data-tour` na Base Solvace, botão de ajuda, selo "IA pronta / Configurar a IA" (abre Minhas integrações), host (visão geral, gaveta da lista no celular, mapa, sistema mais completo) | F1 |
| Q1 | Build + e2e local (usuário, admin, celular, Técnico, guia/"Mostrar na tela", regressão do tour da tela do card) | F2 |
