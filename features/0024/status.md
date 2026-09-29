# Status — Feature 0024 (Da análise à correção, chamados, PRs por repositório, Timeline completa, skills pelo PRMake)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0024` no backend (`../prform.api-0024`) e no front (`../solvace.prform.web/prform-app-0024`, a criar), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Modelo + API: fase análise/correção, dono/tipo/dependências/`waiting`, perguntas, links, ações do usuário; migração | 1 | — | ⬜ pendente | Claude | — |
| B3 | Skills no repositório (`skills/`), endpoints `Skills`, pacote, `install.sh` | 1 | — | ⬜ pendente | Claude | — |
| B2 | Automação: PR mesclado → etapa/plano concluído; chamado resolvido → etapa concluída; registros na Timeline | 2 | B1 | ⬜ pendente | Claude | — |
| S1 | `prmake-plan.sh`: `ask`/`wait-answers`/`answer`/`link`/`correction`, `control` estendido | 2 | B1 | ⬜ pendente | Claude | — |
| S2 | `SKILL.md` analisar-bug: propor soluções, perguntas, plano de correção, fluxo de branches, nunca merge | 2 | S1 | ⬜ pendente | Claude | — |
| S3 | Atualização automática das skills (`self-update`, hook `SessionStart`, `setup`) | 2 | B3 | ⬜ pendente | Claude | — |
| F1 | Abas Análise/Correção; dono, tipo, dependências e "aguardando" nas etapas | 2 | B1 | ⬜ pendente | Claude | — |
| F2 | Perguntas (faixa de destaque, opções, texto livre, tempo real) | 2 | B1 | ⬜ pendente | Claude | — |
| F3 | Links/chamados/PRs da etapa, status, ações do usuário (iniciar/concluir/pular) | 2 | B1, B2 | ⬜ pendente | Claude | — |
| F4 | Tela "Skills" (instalar, versão, download) | 2 | B3 | ⬜ pendente | Claude | — |
| T1 | Teste ponta a ponta local | 3 | todas | ⬜ pendente | Claude | — |
| Q1 | PRs e deploy (merge pelo usuário) | 4 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- Decidido: chamados só com link + status manual e histórico por etapa (Q-a); revamp backend só `development` (Q-b). Hook de atualização instalado automaticamente (Q-c) — ver `plan.md` §4.
- Regra do usuário: **o Claude só abre PRs, nunca faz merge** (o plano conclui quando os PRs são mesclados por outra pessoa).

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktree `feature/0024` no backend. O link público do Freshservice redireciona para o login (testado) — status só via API com o número do chamado.
- 2026-09-28 — Decisões Q-a (chamado = link + status manual + histórico) e Q-b (revamp backend só `development`).
- 2026-09-28 — Decisão Q-c: o instalador coloca o hook `SessionStart` sem perguntar.
