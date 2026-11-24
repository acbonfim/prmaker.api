# Status 0098

- Branch: `feature/0098` (prform.api-0098 e prform-app-0098), a partir de origin/master.
- Merge: não autorizado. Nada enviado ao GitHub ainda.

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — módulo Governance + motor + seed | ✅ | principal | f2ac45d — schema `governance`, seed no `--migrate` (só o que falta) |
| B2 — trilha (actor, sink, middleware) | ✅ | principal | 4699cdb — fila em memória + gravação em lote; enriquecedor acha card/fase pelo plano/item |
| B3 — aplicação das regras (HTTP, MCP, evaluate, hooks/*) | ✅ | principal | 4699cdb + 5570bee (especialista pode chamar evaluate/hooks/events; `source` por evento) |
| B4 — admin (regras, hooks, configurações, eventos) | ✅ | principal | 4699cdb — mudança da governança gera 1 evento com antes/depois |
| B5 — métricas e linha do tempo do card | ✅ | principal | 5570bee + eaec194 — testado com dados semeados (marcos, medianas, custo) |
| B6 — gatilhos do servidor + prmake_rules | ✅ | principal | 4699cdb (timeline/webhook) + c94e301 (`prmake_rules` lista os guardrails ativos) |
| S1 — script de hook + instalador | ✅ | agente skill | aa643f5 — JSON `permissionDecision`, `|| true`, fail-open, fila em lote |
| S2 — executor 1.5.0 | ✅ | agente skill | 11c1b28 — hook com `--from-executor`; guard reporta bloqueio |
| S3 — SKILL.md | ✅ | agente skill | a79471c |
| F1 — rota, menu, serviço, casca | ✅ | agente front | 95a7604 |
| F2 — visão geral, usuários, cards, tarefas | ✅ | agente front | c36565a + d3f392f |
| F3 — auditoria | ✅ | agente front | 4520984 |
| F4 — regras, hooks, configurações | ✅ | agente front | 40fc4d4 |
| F5 — ação barrada nas telas | ✅ | agente front | 8bf733c + CORS do `X-Governance-Warning` (5570bee) |
| Q1 — testes | ✅ | | governance 44 · prform 456 · SkillTests 98 · ExecutionAgent 40 · specs da governança 34 · ng build ok |
| Q2 — e2e local | 🟡 | principal | API + Postgres 18 isolado + front real (`.t0098`): 8 abas sem erro, bloqueio 403, aviso, hooks/events, métricas. Falta: Claude Code de verdade com o hook instalado |
| Q3 — deploy em monitor | ⏳ | | precisa de ok do usuário |

## Log

- 2026-11-24 — spec/plano; worktrees criados.
- 2026-11-24 — B1–B6 (principal), S1–S3 (agente skill) e F1–F5 (agente front) em paralelo; ajustes de contrato (allOf,
  monitored, wouldDecide, simulador com corpo plano, `users/{id}` = `{ user, cards, recent }`); e2e local com prints.

## Pendente / observações

- Q2: hook real num Claude Code (terminal e executor 1.5.0) contra a API local.
- O deploy sobe o modo **monitor**: nada é barrado até o admin ligar o "Aplicando" na tela.
- Todo mundo atualiza o `prmake-skills.sh` uma vez (a `toolVersion` passa a incluir o script de governança).
- Regras que só olham ações do servidor (`POST ...`, `mcp:...`) ficam fora dos `toolMatchers` do hook (não vão à rede).
- Aprovação de verdade (`approval`) ainda barra com "precisa de aprovação" — rodada 2.
- Specs antigos do front quebram o `ng test` completo (não são da 0098); os da governança rodam com tsconfig próprio.
