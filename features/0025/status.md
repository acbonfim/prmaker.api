# Status — Feature 0025 (PRs e plano sem cliques; o PRMake acorda o Claude)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)

| Fase | Descrição | Onda | Status | Commits |
|---|---|---|---|---|
| B1 | Sincronização de PRs do plano a cada leitura (proteção de 8 s) | 1 | ✅ concluída | ver log |
| F1 | Vigia silencioso de PRs na tela do card; plano reage ao evento de PR | 1 | ✅ concluída | ver log |
| S1 | `prmake-plan.sh watch` + SKILL.md (vigia em segundo plano, continua sozinha) | 1 | ✅ concluída | ver log |
| T1 | Teste local | 2 | ✅ concluída | — |
| Q1 | PRs e deploy | 2 | ⬜ pendente | — |

## Notas
- **T1** — API local (Postgres 18 em docker) + skill do repositório + Chrome headless:
  - vigia da skill: usuário conclui a etapa dele na tela → vigia termina em 40 s com "etapa validar-qa: pending -> completed (etapa do usuario) · prontas: pr-edv"; PR mesclado → etapa de PR e plano concluídos → vigia termina em 41 s com exit 11;
  - tela sem cliques: PR passa a mesclado → plano "Concluído" em 26 s e a lista de PRs sem PR aberto em 42 s (em produção a mudança detectada pela tela ou pelo vigia também dispara o evento e o plano atualiza na hora).
  - Achado no teste: o vigia da tela dependia das configurações da tela carregarem — passou a ser ligado no início.

## Log
- 2026-09-29 — Diagnóstico, plano e implementação (B1, F1, S1) e T1.
