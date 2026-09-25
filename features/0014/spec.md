Tipo: Melhoria técnica (banco de dados da autenticação)
Prioridade: média
Origem: conversa de 2026-09-25 (redigida pelo Claude a pedido do usuário).

Objetivo: tirar a API de autenticação (Cime.Auth) do SQL Server e levá-la para o MySQL, o mesmo servidor do PullRequest, migrando **todos** os dados sem perda. O SQL Server foi um erro de processo que acabou ficando.

Motivação:

1. Dois SGBDs diferentes para um sistema pequeno. Mais uma connection string, outro provider EF e outro tipo de lock de migração.
2. Se um dia o banco for para outro lugar (ex.: GCP), SQL Server custa ~5× mais que MySQL (sem opção compartilhada, licença Microsoft).
3. A API de PR já lê `AspNetUsers` (nome, departamento) por um segundo contexto que também é SQL Server.

Requisitos:

1. A Cime.Auth e o `AuthenticationContext` da API de PR passam a usar MySQL (Pomelo, mesma versão do resto da solução).
2. Os dados da autenticação ficam **separados** dos dados do PullRequest: database próprio no mesmo servidor MySQL (decisão de arquitetura no plano).
3. Migrar todas as tabelas (usuários, papéis, vínculos, claims, logins, tokens, serviços, códigos de acesso) preservando:
   - ids;
   - `ExternalId` (usado em toda a API de PR, na timeline e nas api-keys);
   - hashes de senha e security stamps (ninguém precisa trocar a senha);
   - datas e flags.
4. Verificação objetiva de "nenhum dado perdido": contagem e hash por linha, comparando origem e destino.
5. O SQL Server fica intacto e é mantido por um período como fallback. Rollback sem perda.
6. Eliminar a armadilha dos seeds não determinísticos (`HasData` com `Guid.NewGuid()`/`DateTime.Now`/hash aleatório) e a senha fixa do admin no código.
