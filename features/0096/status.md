# Status 0096

- Branch: `feature/0096` (prform.api e prform-app), a partir de origin/master.
- Merge: **não autorizado** — só abrir PR quando o usuário pedir (e mesclar os dois juntos).

| Fase | Estado | Notas |
|---|---|---|
| B1 — primeira pessoa | ✅ | `CardSituationTests` ajustado |
| B2 — comentário por card | ✅ | |
| B3 — versão nova adota o item | ✅ | testes de domínio em `SpecialistGuardian0096Tests` |
| B4 — etapa ajustada no lugar | ✅ | `prmake_steps` NÃO vai para o especialista: o upsert apaga as pendentes que ficam de fora |
| B5 — guardião (guard + filtro MCP) | ✅ | `CaseSpecialistGuardTests` (permitidas/bloqueadas, toda edição do plano roda como quem pediu) |
| S1 — skill + prompts | ✅ | executor sem versão nova (libera `mcp__prmake` inteiro) |
| F1 — front | ✅ | `ng build` ok |

Testes: `solvace.executionplans.tests` 252 ✅ · `solvace.prform.tests` 403 ✅ · `dotnet build Solvace.Master.sln` 0 erros.

## Log

- 2026-11-13 — implementado a partir do card 78344 (ajuste simples virou proposta → v2 → item #803 igual ao #800 →
  comentário duplicado). No card, o texto foi corrigido pelo PRMake (`PUT ExecutionPlan/{v1}/handoff`, mesmo comentário).

## Pendente

- Validar com conversa real: pedir ao especialista um ajuste de texto de etapa e o texto da situação (cartão da ação,
  mesmo comentário na discussion) e "já fiz a minha etapa" (o tratamento retoma para o fechamento).
- Pausa → aplica → retoma com o tratamento rodando: conferir que o Claude segue sem perder a vez.
- 78344: a v2 continua `pending` ao lado da v1 (dado antigo) — depois do deploy, cancelar a v2 pela tela.
