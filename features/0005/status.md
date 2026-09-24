# Feature 0005 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0005` em `prform.api` (docs) e `solvace.prform.web/prform-app` (código), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| F1 | Componentes de filtro (cópia do ComandaCerta) | front | — | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F2 | Filtros no painel de PRs + altura dos cabeçalhos | front | F1 | ⬜ | | | | |
| Q1 | Validação na tela | front | F2 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Cor de destaque do filtro ativo | padrão adotado, a confirmar | Primária do CIME (azul) em vez do roxo do ComandaCerta |
| D2 | Valores "pré-carregados" | padrão adotado, a confirmar | Opções do card já listadas ao abrir; nenhuma marcada |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0004 mergeada (#9 back, #5 front). `feature/0005` criada a partir de `master` nos dois repos. Plano criado; F1 iniciada. |
