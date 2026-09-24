# Feature 0006 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0006` em `prform.api` (backend) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| B1 | Descrição sem limite de 2000 (coluna longtext + limite 100.000) | back | — | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F1 | Mensagem de erro ao salvar registro | front | — | ⬜ | | | | |
| Q1 | Publicação, teste real e recuperação dos registros 85/86 | ambos | B1, F1 | ⬜ | | | | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0005 mergeada (#10 back, #6 front). `feature/0006` criada a partir de `master` nos dois repos. Causa confirmada: `varchar(2000)` + MySQL sem modo estrito (corte silencioso). Plano criado; B1 iniciada. |
