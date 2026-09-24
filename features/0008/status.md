# Feature 0008 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0008` em `prform.api` (backend — só documentação) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| F1 | Item "Minha API Key" no menu do usuário + diálogo | front | — | 🟡 | Claude (sessão 0008) | 2026-09-24 | | |
| Q1 | Publicação e teste com usuário não-admin | ambos | F1 | ⬜ | | | | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | `feature/0008` criada a partir de `master` nos dois repos, em worktrees separados (a 0007 roda em paralelo nos diretórios principais). Backend de geração já existe (`GET v2/Integration/key/generate`, qualquer usuário logado): feature só de front. Plano criado; F1 iniciada. |
