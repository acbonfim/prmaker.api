# Feature 0075 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0075` em `prform.api` (back + skills) e `solvace.prform.web/prform-app` (front).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Code refs: modelo, API, MCP, config | back | — | 1 | ⬜ | | | | |
| F1 | Markdown com extensões (callouts, `ref:`, auto-link, `anexo:`) | front | — | 1 | ⬜ | | | | |
| S2 | `prmake-plan.sh coderef` + `coderef.py` | skills | B1 (teste) | 1 | ⬜ | | | | |
| F2 | Popover da ref (diff anotado, highlight) | front | B1, F1 | 2 | ⬜ | | | | |
| F3 | Imagens `anexo:` no markdown, Antes × Depois | front | F1, B3 | 2 | ⬜ | | | | |
| B2 | Regra das citações no `advance` | back | B1 | 2 | ⬜ | | | | |
| B3 | Arquivo do plano por nome + `prmake_attachment` | back | — | 2 | ⬜ | | | | |
| S1 | Modelo novo da análise e das soluções | skills | B1, S2 | 2 | ⬜ | | | | |
| S3 | Mocks visuais (`mock-shot.sh` + receitas) | skills | B3 | 2 | ⬜ | | | | |
| B4 | Situação do card (serviço, handoff, Timeline, Home) | back | B5 | 3 | ⬜ | | | | |
| B5 | Revisões dos PRs (aprovações) | back | — | 3 | ⬜ | | | | |
| B6 | Dev Test in QA automático + Ready for QA na conclusão | back | — | 3 | ⬜ | | | | |
| B7 | Publicar situação no DevOps (manual) | back | B4, D1 | 3 | ⬜ | | | | |
| F4 | Cartão "Situação atual" + fato + chip | front | B4, F1, F2 | 3 | ⬜ | | | | |
| F5 | Concluir "Validar em QA" → Ready for QA | front | B6 | 3 | ⬜ | | | | |
| S4 | Skill: handoff, QA, handover com situação | skills | B4, B6 | 3 | ⬜ | | | | |
| Q1 | Integração e e2e local (`.t0075/`) | ambos | todas | 4 | ⬜ | | | | |
| Q2 | Produção | ambos | Q1 | 4 | ⬜ | | | | |

PR-A (análise legível) = B1, B2, B3, F1, F2, F3, S1, S2, S3. PR-B (situação + QA) = B4–B7, F4, F5, S4.

## Decisões
- **D1** (pendente) — publicar a situação na discussion do DevOps: manual (padrão da spec) ou automático por config.
- **D2** — fotografia: foco ± 30 linhas, máx. 600 linhas/64 KB por ref, 80 refs por plano (config).

## Notas de handoff
- Worktrees: back `../prform.api-0075`, front `../solvace.prform.web/prform-app-0075` (`node_modules` → `prform-app-0019`).
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).

## Log
- 2026-11-04 — spec, plano e status escritos; branches `feature/0075` criadas nos dois repos a partir da master
  (back 94a08a4, front b08550d).
