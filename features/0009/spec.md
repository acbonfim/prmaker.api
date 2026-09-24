Tipo: Débito técnico (vem da feature 0008 — "Fora do escopo")
Prioridade: a definir

Objetivo: Permitir revogar API Keys (x-api-key). Hoje uma chave vazada não pode ser invalidada sem desativar o usuário.

Situação atual (confirmada no código em 2026-09-24):

1. A API Key é um JWT assinado com `Auth:Secret` e a claim `tokenType = x-api-key` (`Cime.Auth` → `TokenService.GenerateAccessToken(claims, true)`). As claims são id, usuário, ExternalId, cargos e serviços do usuário no momento da geração.
2. A chave não expira: o `XApiKeyAuthenticationHandler` (`Cime.BuildingBlocks.Security`) valida com `ValidateLifetime = false`.
3. Não existe registro das chaves emitidas: gerar outra (`GET v2/Integration/key/generate` ou `GET user/generate-api-key`) não invalida as anteriores.
4. O login do front gera uma chave nova a cada login e guarda no `localStorage` (`apiKey`). Cada sessão cria mais uma chave válida para sempre.
5. A única proteção por request é `GET user/is-user-active?username=` (anônimo), chamado pelo handler em toda requisição. Desativar o usuário bloqueia todas as chaves dele.
6. Os cargos ficam gravados na chave: tirar um cargo de alguém não tira a permissão das chaves já emitidas.
7. Trocar o `Auth:Secret` invalida as chaves de todo mundo, e os tokens Bearer também.

Features:

1. Revogar as chaves do próprio usuário na tela "Minha API Key" (feature 0008), com o botão "Revogar todas as minhas chaves" e uma confirmação.
2. O admin, na gestão de usuários, pode revogar as chaves de qualquer usuário sem precisar desativar a conta.
3. Opcional (avaliar na análise): lista das chaves pessoais emitidas, com nome/descrição, data de criação e último uso, e revogação de cada uma.
4. Opcional: revogar as chaves automaticamente quando os cargos do usuário mudarem (`PUT user/UpdateRoles`), para que o cargo removido não continue valendo nas chaves antigas.

Regras:

1. A checagem da revogação não pode criar uma nova chamada HTTP por request. Ela deve usar a chamada ao Cime.Auth que o handler já faz (hoje `is-user-active`), ampliando essa chamada ou criando um endpoint equivalente.
2. Chaves já emitidas (sem a claim nova) não podem parar de funcionar no deploy. Definir uma regra de transição, por exemplo tratar a ausência da claim como versão 0.
3. A chave usada pela sessão do front (gerada no login) não pode ser derrubada por engano. Se o usuário revogar as próprias chaves, o front precisa gerar outra na hora ou mandar o usuário logar de novo.
4. Mudança de schema só no SQL Server do Cime.Auth, com migração. Atenção à armadilha dos seeds não determinísticos: a migração não pode levar `UpdateData` dos seeds (ver memória `auth-api-contract`).
5. Precisa de redeploy do Cime.Auth e da API prform, porque o handler fica no `Cime.BuildingBlocks`.

Sugestão de abordagem (validar no plano):

- Mínimo viável: coluna `ApiKeyVersion int` (default 0) em `AspNetUsers`. A chave passa a levar a claim `akv`. O handler manda `username + akv` na checagem que já existe, e o Cime.Auth recusa quando `akv` for diferente da versão atual. "Revogar" = `ApiKeyVersion++`. Esse caminho resolve as features 1, 2 e 4.
- Completo: tabela `UserApiKeys` (Id/jti, UserId, Nome, CriadaEm, UltimoUso, RevogadaEm) com a claim `jti`. Resolve a feature 3, mas é preciso separar a chave de sessão do front das chaves pessoais, senão cada login cria uma linha nessa tabela.
