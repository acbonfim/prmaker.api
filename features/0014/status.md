# Status — Feature 0014 (Autenticação do SQL Server para o PostgreSQL 18)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0014` no backend (`../prform.api-0014`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Cime.Auth no Postgres (Npgsql, schema `auth`, seeder idempotente, `InitialPostgres`, advisory lock) | 1 | — | ✅ concluída | Claude | `c15e31a` |
| M1 | Migrador `tools/Cime.Auth.DataMigrator` (`check`/`schema`/`copy`/`verify`/`reset`) | 1 | — | ✅ concluída | Claude | `52c07fc` |
| B2 | `AuthenticationContext` da API de PR no Postgres | 2 | B1 | ✅ concluída | Claude | `bbd032d` |
| T1 | Ensaio local (Docker: SQL Server → Postgres 18; APIs contra o Postgres) | 2 | B1, M1 | ✅ concluída | Claude | — (testes no scratchpad) |
| I1 | Terraform (`postgres-auth-connection`) e runbook | 3 | B1, B2 | ✅ concluída | Claude | `993dfe8` |
| Q1 | Ensaio com os dados reais no database novo, sem virar | 4 | T1, I1 | 🟨 em andamento | usuário + Claude | — |
| Q2 | Virada (janela curta) | 4 | Q1 | ⬜ pendente | usuário + Claude | — |
| Q3 | Limpeza (SQL Server; opcional: tabelas legadas do `db31021`) | 5 | Q2 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Postgres 18 no MonsterASP em vez de MySQL (usuário, 2026-09-25): banco único no fim do caminho (o PR vai depois) e caminho livre para o .NET 10 (Npgsql acompanha o EF Core; o Pomelo ainda não tem EF Core 10).
- D1: database próprio, tabelas da auth no schema `auth`; o PR entra depois no mesmo database, com schema próprio. Nomes no padrão do EF.
- D2: chave nova `ConnectionStrings:AuthDatabase`, um secret para as duas APIs.
- D3: `datetime2` → `timestamp without time zone` (datas são `DateTime.Now`); `datetimeoffset` → `timestamptz` em UTC; `setval` das sequências após o `copy`.
- D4: migrações do zero, sem `HasData`; seeder idempotente; senha do admin por secret; `pg_advisory_lock`.
- D5: migrador dedicado com verificação por hash; "nenhum dado perdido" = `verify` com 0 diferenças; quem roda contra produção é o usuário.
- Descartadas: MySQL no `db31021` (B) e database MySQL separado (A), substituídas pelo Postgres.

## Q1 — andamento
- ✅ Database `db70140` (PostgreSQL 18.6 em **Windows**, collation `English_United States.1252`, fuso `Europe/Berlin`), acesso externo ok.
- ✅ **Criptografia**: o servidor diz `ssl=off`, mas há um proxy do MonsterASP na frente (o backend vê `127.0.0.1`). Do cliente até o proxy, a conexão é **TLS 1.3** (confirmado com `\conninfo`, inclusive com `sslmode=require`). Em produção usar `Ssl Mode=Require` (nunca cai para texto puro).
- ✅ `lower()` com acentos ok (busca de usuários); ordenação natural.
- ✅ `schema` aplicado (Claude). `copy` + `verify` rodados **pelo usuário** do SQL Server de produção (`db30567`, SQL Server 17): 4 papéis, 11 usuários, 2 serviços, 12 vínculos usuário-papel, 22 usuário-serviço, 14 códigos (67 linhas, demais tabelas vazias). `check` sem erros nem avisos; **verify = 0 diferenças**.
- 🟨 Auth nova local (Development, sem migrar/semear, SMTP desligado) em `http://localhost:52050` contra o `db70140`, para o usuário testar o login pelo Swagger.
- ⬜ Depois: `reset` do schema para a virada.

## Pendências do usuário
- Testar o login pelo Swagger local.
- Trocar a senha do `db70140` no painel (ficou registrada na conversa) antes da virada.
- Combinar a janela da virada (Q2).

## Notas da implementação
- **B1**: migração `InitialPostgres` validada num Postgres 18.6 (Docker): subir → descer → subir; nada no schema `public`, nenhum dado de seed na migração. Duas instâncias subindo juntas com o banco vazio: uma migra e semeia dentro do lock, a outra espera e não duplica. Admin semeado faz login; papel novo recebe id 5 (sequência ajustada).
- **T1 — origem realista**: schema e admin criados pela **própria auth antiga** (código da `master`) num SQL Server 2022 (Docker/Rosetta), mais um usuário e um serviço criados pela API antiga, mais casos de borda por SQL:
  - ids salteados e papel `gestor` (id 7);
  - acentos, CJK e emoji (inclusive ZWJ);
  - `LockoutEnd` `-03:00` e `9999-12-31 …9999999`;
  - `datetime2` com 7 casas e `0001-01-01`;
  - `ImagemUrlUser` com 17.600 caracteres e token com 9.000;
  - claims/logins/tokens/role claims, `Description` vazia;
  - `\0` num claim e `"bloqueado "` com espaço no fim.
- **T1 — migrador**:
  - `check` antes do schema aponta as tabelas faltando; depois do `schema`, acusa o `\0` (erro) e o espaço no fim (aviso), e o `copy` recusa.
  - Com o `\0` corrigido: `copy` e `verify` = **0 diferenças**.
  - Conferência independente no Postgres: fuso convertido para UTC certo, 7→6 casas, texto/emoji intactos, `ExternalId` e hash preservados, sequências no próximo id.
  - Negativos: `copy` com destino cheio recusa; alteração e insert na origem depois da cópia são acusados (divergente e faltando, com a chave); `reset` sem/errado recusa; ciclo `reset → schema → copy → verify` = 0.
- **T1 — auth nova sobre os dados migrados** (modo Production, SMTP inválido):
  - migração no-op e seeder no-op;
  - login com a senha original (maria/admin); usuário desativado barrado;
  - busca com acento e maiúsculas; papéis (`user,gestor`); refresh token; api-key + `is-user-active` (a chamada que a API de PR faz a cada request);
  - código migrado válido e código errado recusado;
  - cadastro (id 16) com código criado; `UpdateRoles`, `AddUserToService`, `ActiveToggle`.
- **T1 — API de PR**: `UserRepository`/`TimelineUserRepository` reais contra o Postgres: nome por `ExternalId`, inexistente = null, lista de nomes, departamentos, ids por departamento.
- `DateTime.MinValue` ("nunca logou") vira `-infinity` no Postgres (padrão do Npgsql) e volta `MinValue` na leitura. A auth e o migrador usam o mesmo Npgsql, então o comportamento é consistente.
- As datas continuam no horário local de quem roda a API (como no SQL Server); no Cloud Run é UTC.
- `Seed__AdminPassword` **não** foi para o Terraform: em produção os dados vêm migrados e o seeder não roda. Sem a senha, ele só avisa no log. Configurar só se um dia o banco for recriado vazio.
- `appsettings.QA.json` da auth aponta para o `db31021` (MySQL): é a origem das tabelas `AspNet*` legadas. Não foi alterado.
- **Incidente no ensaio**: ao criar um usuário de teste pela auth **antiga**, ela disparou o e-mail de boas-vindas pelo SMTP real (`noreply@softhouse.app.br`) para o endereço fictício `joao.silva@teste.com`, com um código que só vale no banco de teste local. Depois disso, a auth passou a rodar localmente com `Email__Host=127.0.0.1 Email__Port=1`.

## Notas de handoff
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Banco de dev = produção: nunca rodar a Cime.Auth/API local contra os bancos de produção com auto-migrate. B1/T1 usam bancos locais descartáveis (Docker, dados no scratchpad).
- Migrações: factory de design-time temporária com connection string fictícia, removida depois (padrão 0001/0011).

## Log
- 2026-09-25 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0014`.
- 2026-09-25 — Replanejada para PostgreSQL 18 (em vez de MySQL); o PR vai para o Postgres numa feature seguinte.
- 2026-09-25 — B1, M1, B2, T1 e I1 concluídas (ensaio local completo com Docker). Falta o Q1 (database Postgres no MonsterASP + ensaio com os dados reais).
