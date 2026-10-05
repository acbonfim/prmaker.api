# Feature 0062 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0062` em `solvace.prform.web/prform-app` (front) a partir de `master`; `prform.api` só guarda esta documentação.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

| Fase | Repo | Status | Responsável | Conclusão | Commits |
|---|---|---|---|---|---|
| F1 | front | ✅ | Claude (sessão principal) | 2026-10-05 | front (commit único) |
| F2 | front | ✅ | Claude (sessão principal) | 2026-10-05 | front (commit único) |
| F3 | front | ✅ | Claude (sessão principal) | 2026-10-05 | front (commit único) |
| Q1 | ambos | ⬜ | | | |

## Decisões (padrão adotado, a confirmar)

| # | Tema | Padrão |
|---|---|---|
| D1 | `<card>` no texto | número do card (`75294`), sem `AB#` |
| D2 | Ponto final após o link | omitido (para o link continuar clicável ao colar) |
| D3 | "Ambiente" | rótulo do destino em `ActiveBranchs` (ex.: HV); sem rótulo, a branch de destino em maiúsculas |
| D4 | Quais PRs | só `OPEN` e `MERGED` com URL (fechados sem merge e legados ficam fora) |
| D5 | Tabela | copia HTML (cola como tabela no Teams/Outlook/DevOps) + markdown como texto puro |
| D6 | "Copiar PR" | troca o conteúdo do mesmo popover (com botão voltar), como o "Abrir PR rápido" |

## Notas de handoff

- Testes: `github-actions-menu/*.spec.ts` (6 casos, passam em Chrome headless). O `ng test` do projeto inteiro não compila por specs antigos (`async` removido do Angular) — rodei com um `tsconfig` temporário só para estes arquivos.
- `ng build` ok (só avisos antigos de budget).
- Falta Q1: ver na tela real, incluindo o "⋯" quando a janela é estreita.
