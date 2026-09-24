# Feature 0008 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0008` em `prform.api` (backend — só documentação) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| F1 | Item "Minha API Key" no menu do usuário + diálogo | front | — | ✅ | Claude (sessão 0008) | 2026-09-24 | 2026-09-24 | front 5e467f0 |
| Q1 | Publicação e teste com usuário não-admin | ambos | F1 | ⬜ | | | | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### F1 — Minha API Key no menu do usuário ✅ (front 5e467f0)
- Novo `components/my-api-key-dialog` (signals, zoneless), aberto pelo item **Minha API Key** do menu do nome (`top-menu`), entre "Minhas integrações" e "Sair".
- O diálogo só gera a chave ao clicar em **Gerar minha API Key** (`authService.generateApiKey()` → `GET v2/integration/key/generate`, Bearer). Mostra a chave mascarada (mostrar/ocultar), copiar, os cargos gravados nela e um exemplo `curl` (copiado já com a chave). Erro do backend aparece no próprio diálogo.
- Avisos no diálogo: a chave não expira; gerar outra não invalida as anteriores (se vazar, avisar um admin, que pode desativar o usuário); cargos ficam gravados na chave.
- Tela de gestão de usuários: botão "Minha API Key" e `ApiKeyDialogComponent` removidos (o menu vale para todos, inclusive admin).
- Validação: `ng build` ok (só os avisos de budget de CSS que já existiam). **Não testado no navegador.**

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | `feature/0008` criada a partir de `master` nos dois repos, em worktrees separados (a 0007 roda em paralelo nos diretórios principais). Backend de geração já existe (`GET v2/Integration/key/generate`, qualquer usuário logado): feature só de front. Plano criado; F1 iniciada. |
| 2026-09-24 | F1 | Concluída (front 5e467f0). PRs abertos nos dois repos. Falta Q1: merge/deploy do front e teste com usuário não-admin. |
