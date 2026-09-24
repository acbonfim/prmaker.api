# Feature 0004 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0004` em `prform.api` (docs) e `solvace.prform.web/prform-app` (código), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| F1 | Markdown automático na timeline | front | — | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 907072a |
| Q1 | Validação na tela | front | F1 | ⬜ | | | | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### F1 — Markdown automático na timeline ✅ (repo front, 907072a)
- `helpers/markdown-detect.ts` → `looksLikeMarkdown(texto)`: true só com sinal forte — bloco de código, título `# `, `**negrito**`/`__negrito__`, código inline, `[link](url)`, citação `> `, tabela (`|---|`) ou **2+** linhas de lista (`- `/`* `/`+ `/`1. `). Asterisco/hífen soltos, `#123`, `a > b`, snake_case e URL solta continuam texto comum.
- `pipes/timeline-markdown.pipe.ts` (puro): markdown → HTML com `marked` (GFM + quebras de linha), `null` para texto comum.
- `card-timeline`: markdown → `<div [innerHTML]>` (sanitizado pelo Angular); texto comum → `{{ }}` com `pre-wrap`, **igual a antes**. Clique em link abre nova aba (`window.open` com `noopener`). Edição continua no texto original. Estilos com `:host ::ng-deep` (HTML injetado não recebe o escopo do componente).
- Backend e endpoint **inalterados** (spec 4).
- Validação: `ng build` ok; teste Node do detector com os dois registros da `img.png` + casos de borda: **20/20**. Não testado no navegador.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0003 mergeada (#8 back, #4 front). `feature/0004` criada a partir de `master` nos dois repos. Plano criado; F1 iniciada. |
| 2026-09-24 | F1 | Concluída (front 907072a). Falta a validação na tela (Q1). |
