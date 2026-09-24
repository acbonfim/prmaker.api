# Feature 0008 — "Minha API Key" no menu do usuário

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0008` nos dois repos, a partir de `master` (com 0001–0006). Desenvolvida em paralelo à 0007 (sem dependência), em worktrees separados (`../prform.api-0008`, `../prform-app-0008`).

## 1. Análise
- **A geração já existe no backend.** O Cime.Auth tem `GET api/v2/Integration/key/generate` (e o legado `GET api/user/generate-api-key`), que monta um JWT com `tokenType = x-api-key` e as claims de id, usuário, ExternalId, cargos e serviços do usuário logado. O filtro global de autorização do Cime.Auth (`Program.cs`, `AuthorizeFilter` + `RequireAuthenticatedUser`) exige só um Bearer válido: **qualquer usuário logado pode gerar a própria chave**. O login do front já chama esse endpoint e guarda a chave em `apiKey`.
- **Hoje o único ponto de UI é a tela de gestão de usuários** (botão "Minha API Key" em `user/manager`, `ApiKeyDialogComponent`), que é **admin-only** (`AdminGuard`). Usuários comuns não têm como obter a chave para usar em integrações (skills, scripts).
- **Como a chave se comporta** (o que a tela precisa explicar):
  - Não expira (o `XApiKeyAuthenticationHandler` valida com `ValidateLifetime = false`).
  - Não há revogação individual: gerar outra chave **não invalida** as anteriores. A proteção existente é a checagem por request de usuário ativo (`user/is-user-active`): **desativar o usuário bloqueia todas as chaves dele**.
  - Os cargos ficam gravados na chave no momento da geração: se os cargos mudarem, é preciso gerar outra.

## 2. Solução
- Novo item **"Minha API Key"** no menu do nome do usuário (top bar), entre "Minhas integrações" e "Sair".
- Novo `MyApiKeyDialogComponent` (em `components/`), no mesmo visual de "Minhas integrações":
  - explica o uso (header `x-api-key`, URL base da API do ambiente) e os avisos acima;
  - botão **Gerar minha API Key** → chama `authService.generateApiKey()`; mostra a chave mascarada com mostrar/ocultar, botão copiar, os cargos embutidos e um exemplo `curl` copiável;
  - estado de erro com a mensagem do backend.
- A tela de gestão de usuários deixa de ter o botão próprio: o item do menu vale para todos (inclusive admin). O `ApiKeyDialogComponent` antigo é removido.
- **Sem mudança de backend, sem migração, sem mudança de contrato.**

## 3. Fases

### F1 — Item "Minha API Key" no menu + diálogo (front)
- [x] `MyApiKeyDialogComponent` (signals — app é zoneless).
- [x] Item no menu do usuário (`top-menu`).
- [x] Remover o botão/diálogo da tela de gestão de usuários.
- [x] `ng build`.

### Q1 — Publicação e teste (usuário)
- [ ] Merge do PR do front → deploy do `cime-web`.
- [ ] Teste com um usuário não-admin: menu → Minha API Key → gerar → copiar → chamar a API prform com o header `x-api-key`.

## 4. Fora do escopo (possível evolução)
- Revogação de chaves: registrada como débito técnico na **[feature 0009](../0009/spec.md)**.
- Abordagens possíveis (lista de chaves emitidas ou versão da chave no usuário, checada no handler) estão na spec. As duas exigem migração no SQL Server do Cime.Auth e mudança no `XApiKeyAuthenticationHandler`.
