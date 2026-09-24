# Feature 0001 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0001` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo de dados e migração | back | — | 1 | ⬜ | | | | |
| B2 | GitHubService multi-repo | back | — | 1 | ⬜ | | | | |
| F1 | Estado compartilhado + componentes extraídos | front | — | 1 | ⬜ | | | | |
| B3 | Aplicação e endpoints de PR do card | back | B1, B2 | 2 | ⬜ | | | | |
| F2 | Reestruturação da tela principal | front | F1 | 2 | ⬜ | | | | |
| F3 | Modal "Abrir PR" | front | F1 (+B3 p/ integrar) | 2 | ⬜ | | | | |
| B4 | Sync de status e hardening | back | B3 | 3 | ⬜ | | | | |
| F5 | IA: stepper vertical multi-repo | front | F1 (+B3 p/ integrar) | 3 | ⬜ | | | | |
| F4 | Painel de PRs abertos do card | front | F3, B4 | 4 | ⬜ | | | | |
| F6 | IA: passo Resumo + prompt multi-repo | front | F5 | 4 | ⬜ | | | | |
| Q1 | Integração, regressão e publicação | ambos | todas | 5 | ⬜ | | | | |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | a confirmar | |
| D2 | a confirmar | |
| D3 | a confirmar | |
| D4 | a confirmar | |
| D5 | a confirmar | |
| D6 | a confirmar | |
| D7 | a confirmar | spec 4.3 truncada |
| D8 | a confirmar | |
| D9 | a confirmar | |
| D10 | a confirmar | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-23 | — | Plano criado; branches `feature/0001` criadas a partir de `master` nos dois repos. |
