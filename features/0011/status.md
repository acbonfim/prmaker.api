# Status — Feature 0011 (Menu "Ações DevOps")

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0011` no backend e no front (`../solvace.prform.web/prform-app`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Configuração por campo nos plugins (`FieldSettings`) | 1 | — | ⬜ pendente | — | — |
| B2 | Campos da 0011 no "AI Configurations" (migração) | 2 | B1 | ⬜ pendente | — | — |
| B3 | Resumo não técnico (colunas + endpoint + comentário) | 2 | — | ⬜ pendente | — | — |
| B4 | Ações no DevOps (endpoints + config efetiva) | 2 | B1, B2 | ⬜ pendente | — | — |
| F1 | Rótulos e campos opcionais em "Minhas integrações" | 2 | B1 | ⬜ pendente | — | — |
| F2 | Botão e menu "Ações DevOps" | 3 | B4 | ⬜ pendente | — | — |
| F3 | Editor do resumo não técnico | 3 | B3, F2 | ⬜ pendente | — | — |
| S1 | Skill `gerar-prmake` salva o resumo pela API | 3 | B3 | ⬜ pendente | — | — |
| Q1 | Publicação e teste | 4 | todas | ⬜ pendente | usuário | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Estimativa inicial: habilita só quando o usuário salvou os 3 valores (6/6/0 aparecem como sugestão), "igual às integrações" (usuário, 2026-09-25).
- Card US: o menu abre com o aviso de que as ações de US virão depois (usuário, 2026-09-25).
- Resumo: ver, editar, salvar e gerar novamente com IA. Salvar grava no banco e cria **ou atualiza** o mesmo comentário da discussion (`SummaryCommentId`) (usuário, 2026-09-25).
- Configuração no "AI Configurations" (spec). O plugin vira pessoal e opcional, com todos os campos pessoais opcionais, para não bloquear a IA.

## Notas de handoff
- Banco de dev = produção: gerar as migrações com a factory temporária (`MySqlServerVersion(8.0.36)` + connection string fictícia) e removê-la depois. Não rodar a API local nem `database update`.
- Commitar só os arquivos da fase (`git commit -- <arquivos>`). No front, `src/environments/environment.ts` é alteração local do usuário e não deve ser commitado.
- O app é zoneless: nos componentes antigos, `cdr.detectChanges()`; nos novos, signals.

## Log
- 2026-09-25 — Planejamento: `plan.md` e `status.md` criados; branches `feature/0011` criadas nos dois repos.
