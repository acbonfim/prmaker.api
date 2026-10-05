# Feature 0063 — Etapa própria de consulta à Base Solvace e volta à base a cada dúvida

## Achado (reanálise do card 75294, 2026-10-05)
Com a engenharia reversa no contexto, a análise levou 10,1 min contra 15,9 (−37%), com metade das leituras de código
(15 × 30) e metade dos tokens de cache (2,16 M × 4,18 M). Mas: a consulta à base ficava escondida em `investigar-codigo`;
a sessão consultou só no começo e nunca voltou (inclusive na causa raiz e ao propor soluções, onde a opção 2 estimou "afeta
todos os módulos legados" sem `prmake_base_impact`).

## Solução
- Plano de análise: etapa **`consultar-base`** entre `coletar-dados` e `investigar-codigo` (que vira "Confirmar no código").
  As etapas padrão vão para a configuração (`AnalysisDefaultSteps`, Skills Configurations, semeada por migração); a skill
  só tem a reserva.
- Trava da engenharia reversa (`ReverseEngineeringGateStep`) aceita lista: `consultar-base,investigar-codigo` (planos
  antigos continuam travando na investigação). A migração só troca o valor se ainda for o semeado (`investigar-codigo`).
- Plano de correção: etapa **`impacto-na-base`** (primeira, com código) — `prmake_base_impact` em cada ponto que a
  correção toca; vira o roteiro de regressão. `corrigir-<repo>` depende dela.
- Regras na `analisar-bug`: voltar à base antes de abrir arquivo/função/tabela que nenhum item cita; hipótese nova ou
  outro módulo → base antes do código; antes de propor soluções, `prmake_base_impact` em cada ponto de alteração,
  citado em cada opção.
