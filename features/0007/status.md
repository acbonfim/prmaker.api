# Feature 0007 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0007` em `prform.api` (backend) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Plugin "Teams Configurations" + plugin pessoal opcional | back | — | 1 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| B3 | Alterar status do PR no GitHub (REST + GraphQL) | back | — | 1 | ⬜ | | | | |
| B2 | Envio da aprovação para o Teams | back | B1 | 2 | ⬜ | | | | |
| F1 | Plugin opcional no front + estado do Teams | front | B1 | 2 | ⬜ | | | | |
| F2 | Botão Teams + menu de atalhos na linha do PR | front | B2, B3, F1 | 3 | ⬜ | | | | |
| F3 | Abrir PR rápido | front | F2 | 3 | ⬜ | | | | |
| Q1 | Workflow real + teste na tela | ambos | todas | 4 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Mecanismo de envio | ✅ confirmada | Workflow webhook; a URL do Workflow é a chave pessoal |
| D2 | Grupo "configurável" | padrão adotado, a confirmar | Definido no Workflow; plugin guarda o `GroupName` exibido |
| D3 | Quais PRs podem pedir aprovação | padrão adotado, a confirmar | Só `OPEN` e não draft |
| D4 | Registro na linha do tempo | padrão adotado, a confirmar | Sem registro de pedido de aprovação/troca de status |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0006 mergeada (#11 back, #7 front). `feature/0007` criada a partir de `master` nos dois repos. Usuário escolheu Workflow webhook (D1). Plano criado; B1 iniciada. |
