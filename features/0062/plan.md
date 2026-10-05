# Feature 0062 — Plano

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)

Padrão seguido: `devops-actions-menu` / `smart-actions-menu` (cc-popover dentro da `app-action-toolbar`).

| Fase | Descrição | Repo | Depende de |
|---|---|---|---|
| F1 | Formatação da cópia (`pr-copy-format.ts`: texto, tabela markdown e HTML) + `copyRichToClipboard` no `CliipboardService` | front | — |
| F2 | `app-github-actions-menu` (menu + checklist de PRs + formato + Copiar), ícone SVG do GitHub, `toolbarSvgIcon` na `action-toolbar` | front | F1 |
| F3 | Troca do botão "Abrir PR" do rodapé da tela de card pelo novo menu | front | F2 |
| Q1 | Teste na tela real (card com PRs abertos e mesclados) | ambos | F3 |
