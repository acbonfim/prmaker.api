Tipo: Melhoria técnica (banco de dados da autenticação)
Prioridade: alta
Origem: conversa de 2026-09-25 (redigida pelo Claude a pedido do usuário).

Objetivo: tirar a API de autenticação (Cime.Auth) do SQL Server e levá-la para o **PostgreSQL 18** (MonsterASP), migrando **todos** os dados sem perda. É o primeiro passo para o Postgres virar o banco único do sistema: numa feature seguinte, o PullRequest (hoje MySQL) também vai para lá.

Motivação:

1. O SQL Server da auth foi um erro de processo que acabou ficando.
2. **Um banco só no fim do caminho.** Hoje são dois SGBDs (MySQL + SQL Server). Levar a auth para Postgres agora e o PR depois deixa um SGBD, um provider EF e schemas separados por responsabilidade (`auth`, depois o do PR) no mesmo database. A auth migra uma vez só.
3. **.NET 10.** O suporte ao .NET 8 acaba em 10/11/2026. O provider MySQL usado (Pomelo) ainda não tem versão para o EF Core 10. O provider do Postgres (Npgsql) acompanha cada versão do .NET.
4. Se um dia o banco sair do MonsterASP, Postgres tem mais opções gerenciadas baratas. SQL Server é o mais caro (sem opção compartilhada, licença Microsoft).

Requisitos:

1. A Cime.Auth e o `AuthenticationContext` da API de PR passam a usar Postgres (Npgsql).
2. Tabelas da auth no schema **`auth`**, num database Postgres próprio (criado pelo usuário no MonsterASP), pronto para receber o schema do PR depois.
3. Migrar todas as tabelas (usuários, papéis, vínculos, claims, logins, tokens, serviços, códigos de acesso) preservando:
   - ids;
   - `ExternalId` (usado em toda a API de PR, na timeline e nas api-keys);
   - hashes de senha e security stamps (ninguém troca a senha);
   - datas e flags.
4. Verificação objetiva de "nenhum dado perdido": contagem e hash por linha, comparando origem e destino.
5. O SQL Server fica intacto e é mantido por um período como fallback. Rollback sem perda.
6. Eliminar a armadilha dos seeds não determinísticos (`HasData` com `Guid.NewGuid()`/`DateTime.Now`/hash aleatório) e a senha fixa do admin no código.
