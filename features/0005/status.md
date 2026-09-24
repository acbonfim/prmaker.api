# Feature 0005 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0005` em `prform.api` (docs) e `solvace.prform.web/prform-app` (código), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| F1 | Componentes de filtro (cópia do ComandaCerta) | front | — | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 93dc9c7 |
| F2 | Filtros no painel de PRs + altura dos cabeçalhos | front | F1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 2bd9eee |
| Q1 | Validação na tela | front | F2 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Cor de destaque do filtro ativo | padrão adotado, a confirmar | Azul `#3d8bfd` (tom da primária do CIME, com contraste para o check branco) em vez do roxo do ComandaCerta |
| D2 | Valores "pré-carregados" | padrão adotado, a confirmar | Opções do card já listadas ao abrir; nenhuma marcada |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### F1 — Componentes de filtro ✅ (repo front, 93dc9c7)
- `components/popover/cc-popover.component.ts`, `components/filter-bar/filter-bar.component.ts` e `filter-bar.models.ts` copiados do ComandaCerta (`shared/popover`, `shared/filter-bar`), com os mesmos seletores (`cc-popover`, `cc-filter-bar`), template, CSS e comportamento.
- Única mudança de lógica: `openFilter()`. Na 1ª abertura cria o controle de busca, como no original; nas seguintes recarrega as opções com o termo atual, porque os valores mudam com os PRs do card.
- Tokens `--cc-*` no `:root` do `styles.scss`: valores escuros do ComandaCerta, superfícies ligadas às `--surface-*` do CIME e destaque `--cc-special: #3d8bfd` (D1).
- O build avisa NG8102 (`??` redundante) em 4 pontos do filter-bar. O original tem o mesmo; mantido para a cópia continuar fiel.

### F2 — Filtros no painel de PRs + altura dos cabeçalhos ✅ (repo front, 2bd9eee)
- `github-pr-list`: `<cc-filter-bar>` acima da lista, fora da área que rola, visível quando o card tem PRs. Filtros Status, Branch (`prefixo+nome`) e Repositório, todos múltiplos. As opções vêm de `filterOptions(prs, key, term)`: só valores que existem nos PRs do card; status na ordem OPEN, MERGED, CLOSED, LEGADO. A lista usa `filterPrs(prs, values)`: OU dentro de um filtro, E entre filtros. Sem resultado: "Nenhum PR corresponde aos filtros."
- Filtros zerados na troca de card (input `resetKey` = `prState.cardNumber()`; a barra é recriada) e quando a lista fica vazia. Atualizar o status (⟳) mantém os filtros.
- Cabeçalhos: `.panel__header` do `app-card-panel` (PRs, Descrição, Root Cause) e `.timeline__header` com `height: 56px` fixo (`box-sizing: border-box`, `padding: 0 16px`).
- Validação: `ng build` sem erros. Teste Node de `filterOptions`/`filterPrs` extraídos do próprio componente: **17/17**. Não testado no navegador.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0004 mergeada (#9 back, #5 front). `feature/0005` criada a partir de `master` nos dois repos. Plano criado; F1 iniciada. |
| 2026-09-24 | F1, F2 | Concluídas (front 93dc9c7, 2bd9eee). Falta a validação na tela (Q1). |
