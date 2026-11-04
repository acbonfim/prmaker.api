# Feature 0077 — Seção de Pull Requests só quando houver PR

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0077` a partir da `master` (worktree `../prform.api-0077`; no front, worktree
> `../solvace.prform.web/prform-app-0077` com `node_modules` linkado de `../prform-app-0019/node_modules`).

Só front (`pages/authenticated/register`). O back leva apenas esta documentação.

## Análise
- `register.component.html`: grade `.pr-content-row` com três áreas `prs plan timeline` (desktop, 1fr/1.05fr/1fr),
  `prs/plan` empilhados à esquerda entre 901–1280 px e abas no celular (`mobileTab`, 0043).
- PRs do card: `CardPrStateService.githubPrs()` (zerado em cada busca por `loadRegister(card, null)`).
- Etiquetas do topo: `infoFacts()` → `app-pr-info-card`.
- Plano: `app-execution-plan` já é responsivo por `container-type: inline-size`; a tela cheia (`FullscreenPanel`) tira
  o painel da grade — não muda.

## Fases
| Fase | Descrição |
|---|---|
| F1 | `showPrsPanel()` (lista não vazia, ou recarregando o mesmo card que tinha PRs); painel/aba de PRs com `@if`; aba efetiva `visibleTab()`; grade sem a área `prs` (`.pr-content-row--no-prs`): plano em 2.05fr no desktop e na altura toda entre 901–1280 px; etiqueta "Sem pull requests" em `infoFacts()` |
| Q1 | Build + teste visual local (Chrome headless, `.t0077/`), PRs, merge e deploy |
