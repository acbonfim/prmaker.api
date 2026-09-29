# Status — Feature 0023 (Plano de execução da skill analisar-bug no PRMake)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0023` no backend (`../prform.api-0023`) e no front (`../solvace.prform.web/prform-app-0023`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Módulo `Solvace.ExecutionPlans` (schema `execution`), API, tempo real, migração | 1 | — | ✅ concluída | Claude | `b4a5d9b` |
| S1 | Skill: `prmake-plan.sh` (fila local, retentativas, pausa) + `SKILL.md` | 1 | — | ✅ concluída | Claude | ver log |
| F1 | Serviço + `app-execution-plan` (etapas animadas, detalhes, status, ações, tela cheia) | 2 | B1 | ⬜ pendente | Claude | — |
| F2 | Visualizador de arquivos (SQL, markdown, JSON, imagem; copiar, baixar, zip) | 2 | B1 | ⬜ pendente | Claude | — |
| F3 | Seção entre PRs e Linha do tempo (layout responsivo) | 2 | F1 | ⬜ pendente | Claude | — |
| T1 | Teste ponta a ponta local (Postgres do docker, API, skill, navegador) | 3 | B1, S1, F1–F3 | ⬜ pendente | Claude | — |
| Q1 | PRs e deploy | 4 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **B1** — testado com a API local (Postgres 18 isolado em docker, porta 55433; auth falso só com `is-user-active`; api-key de dev assinada com a chave de Development): criar plano, upsert de etapas (pendente que some é removida), etapa em andamento tira o plano de pendente, logs com `clientId` repetido não duplicam, atividade da etapa acompanha o último log, pausar → `control` devolve `wait` e a skill não tira da pausa, continuar, cancelar etapa (motivo + nome) → aparece em `cancelledSteps`, etapa concluída não cancela, cancelar plano cancela as pendentes com motivo e trava novas alterações (`stop`), download (`attachment`/`inline`, `nosniff`), `.zip` com pastas por tipo, 10 MB+1 recusado, 20 atualizações simultâneas na mesma etapa → 20 × 200 (xmin + retentativas com espera aleatória). Migração `InitialExecutionPlans` aplicada pelo `--migrate`.
- **S1** — skill atualizada em `~/.claude/skills/analisar-bug` (cópia em [`skill/`](./skill/)). Testado contra a API local (também no bash 3.2 do macOS): `start` cria (etapas padrão) e **retoma** o plano aberto (mesmo sem o `.prmake-plan.json`), `steps`, `step`, `log` (mensagem longa vira pedaços de 18 000), `checkpoint`, `sync` (só envia o que mudou, pelo sha256; imagem em qualquer pasta vira `image`), `pull`, `control` (0/10/11), `wait` (tela pausa → skill espera → tela continua → segue), etapa cancelada na tela aparece para pular, plano cancelado → `stop` e 400 nas alterações. API fora do ar → envios na fila local `.prmake-outbox.jsonl`, reenviados em ordem na chamada seguinte. **Bug achado e corrigido no teste**: o reenvio da fila sobrescrevia o corpo do envio atual (um log foi enfileirado com o corpo de outra chamada) — a fila usa arquivo próprio. **Antes do deploy**: `start` contra produção → 404 → `PLANO INDISPONIVEL` e os comandos viram no-op (a análise segue como antes, verificado com o token real, só GET); 5xx também; 401/403 param com mensagem de token.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0023` no backend e no front.
- 2026-09-28 — B1 concluída (`b4a5d9b`).
