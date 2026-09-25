# Feature 0014 — Autenticação do SQL Server para o MySQL

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0014` (worktree `../prform.api-0014`), a partir de `master`. Só backend.

## 1. Análise

### Cime.Auth (`CIME/modules/Cime.Auth/src/cime.auth.api`)
- `DefaultContext : IdentityDbContext<User, Role, int, …>` com `UserForgetCodes`, `UserServices` e `Services`. Dez tabelas: `AspNetRoles`, `AspNetUsers`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`, `Services`, `UserServices`, `UserForgetCodes`.
- Duas migrações SQL Server (`Init`, `AddImagemUrlUser`).
- Tipos usados: `nvarchar(max|256|450)`, `int`/`bigint` identity, `bit`, `datetime2`, `datetimeoffset` (`LockoutEnd`), `uniqueidentifier` (`ExternalId`…). Índices únicos filtrados (`[NormalizedName] IS NOT NULL`), sintaxe só do SQL Server.
- **SQL específico do SQL Server só no `MigrationService`** (`sp_getapplock`). Não há `FromSql`/`ExecuteSql` no código.
- **Seeds não determinísticos**: `HasData` de papéis (1–4) e do admin (Id 1) com `Guid.NewGuid()`, `DateTime.Now` e `PasswordHasher` (salt aleatório). Um `migrations add` sempre gera `UpdateData`, que resetaria o admin de produção (ver memória "Provider EF"). A senha `Copa#2026` está no código.

### API de PR
- `AuthenticationContext` (SQL Server, `ConnectionStrings:AuthenticationConnection`) mapeia só `AspNetUsers` (`ExternalId`, `FullName`, `Departamento`), **somente leitura**, em `UserRepository`/`TimelineUserRepository`. Não há join com as tabelas do PR (a busca é sempre por um contexto só).
- Os 3 contextos MySQL (Default, Vacation, Timeline) já dividem o `db31021` e o `__EFMigrationsHistory`. O lock de migração é `GET_LOCK` (`StartupMigrator`).
- Não há colisão de nomes: o PR tem `Users`, o auth tem `AspNetUsers`.

### Infra
- Os bancos do MonsterASP (MySQL `db31021` e SQL Server `db30567`) estão em `148.251.141.66` (Hetzner, Falkenstein). O Cloud Run está em us-central1.
- Terraform: o secret `sqlserver-auth-connection` é usado por `cime-auth` (`ConnectionStrings__DefaultConnection`) e por `cime-pullrequest` (`ConnectionStrings__AuthenticationConnection`).
- Local: MySQL 8.0 via Homebrew (usado na 0011). Docker instalado, mas desligado: é necessário para subir um SQL Server de teste.

## 2. Decisões de arquitetura

### D1 — Database próprio no mesmo servidor MySQL (recomendado)
No MySQL, *schema* e *database* são a mesma coisa. Não existe "mesmo database com schema diferente" como no SQL Server/Postgres. As opções reais:

| | A) Database separado (novo `dbXXXXX` no MonsterASP) | B) Mesmo database `db31021` |
|---|---|---|
| Responsabilidade | Isolada: dono, backup e restore independentes | Misturada com os dados do PR |
| Credenciais | Usuário próprio; o PR só **lê** (idealmente usuário read-only) | Uma credencial com acesso a tudo |
| Migrações | Histórico próprio, sem risco de colidir | Precisa de `MigrationsHistoryTable` separado |
| Custo/limite | Depende do plano do MonsterASP permitir mais um MySQL | Nenhum |
| Mover no futuro | Muda uma connection string | Precisa separar tabelas |

**Escolha: A.** Não há joins entre auth e PR (o PR lê usuários por um contexto próprio), então separar não custa nada em código. Se o plano do MonsterASP não permitir outro database, cair para B com `MigrationsHistoryTable("__EFMigrationsHistory_Auth")`.

### D2 — Nova connection string com nome novo: `ConnectionStrings:AuthDatabase`
O código novo lê a chave nova. Assim o Terraform pode adicionar a env **antes** do deploy (o código antigo a ignora), o deploy troca o provider, e o rollback (revisão anterior do Cloud Run) continua achando a chave SQL Server antiga. Sem janela em que código e configuração não batem.

### D3 — Migrações MySQL do zero, sem `HasData`
- Novo conjunto de migrações MySQL (`InitialMySql`) gerado do modelo atual. As migrações SQL Server saem (ficam no histórico do git).
- `HasData` removido. Papéis padrão e admin passam a ser criados por um **seeder idempotente no startup**, só se não existirem. A senha do admin inicial vem de configuração/secret e nunca do código. Em produção os dados vêm migrados, então o seeder não faz nada.
- Collation do database do auth: **`utf8mb4_0900_as_ci`** (case-insensitive e accent-sensitive, como o `SQL_Latin1_General_CP1_CI_AS` do SQL Server). O padrão do MySQL 8 (`_ai_ci`) ignora acento e poderia fazer "JOAO" e "JOÃO" colidirem no índice único de `NormalizedUserName`.

### D4 — Migrador de dados dedicado (console), com verificação por hash
Ferramenta `tools/Cime.Auth.DataMigrator` (fora da solução da API):
- **`check`** (só leitura): contagens, valores que não cabem no destino (tamanho, datas fora de faixa), duplicidades que o MySQL rejeitaria (unicidade com collation diferente, espaços no fim — SQL Server ignora na comparação, o MySQL 8 não), e o destino precisa estar vazio.
- **`copy`**: numa transação no MySQL, tabela a tabela na ordem das FKs, **com os ids originais**. Conversões:
  - `uniqueidentifier` → `char(36)` minúsculo;
  - `datetime2(7)` → `datetime(6)` (perde só os 100 ns);
  - `datetimeoffset` → UTC;
  - `bit` → `tinyint(1)`.
  Ajusta `AUTO_INCREMENT` para max+1. Aborta se o destino tiver dados.
- **`verify`**: contagem e hash por linha (valores canônicos), origem × destino. Lista o que diferir (faltando, sobrando, divergente). **Critério de "nenhum dado perdido": `verify` com zero diferenças.**
- Quem roda contra produção é o **usuário** (o Claude não lê o banco de produção). Credenciais por variável de ambiente, nunca em arquivo versionado.

## 3. Fases

| Onda | Fases |
|---|---|
| 1 | B1, M1 (independentes) |
| 2 | B2, T1 |
| 3 | I1 |
| 4 | Q1 (ensaio) → Q2 (virada) → Q3 (limpeza, após o período de segurança) |

### B1 — Cime.Auth no MySQL (backend)
- Pomelo 8.0.1 no lugar de `EntityFrameworkCore.SqlServer`; `UseMySql(AuthDatabase, MySqlServerVersion(8.0))` (versão fixa, sem `AutoDetect`, que conecta no design-time e no cold start).
- Collation `utf8mb4_0900_as_ci` no modelo.
- Remove `HasData`; `AuthSeeder` idempotente (papéis 1–4 e admin inicial por config `Seed:AdminPassword`, só se a tabela estiver vazia).
- Apaga as migrações SQL Server; gera `InitialMySql` (factory de design-time com connection string fictícia, como na 0001/0011).
- `MigrationService` com `GET_LOCK`/`RELEASE_LOCK`.
- Build ok; migração aplicada num MySQL local descartável (subir → descer → subir).

### B2 — `AuthenticationContext` da API de PR no MySQL (backend)
`UseMySql(AuthDatabase)` com versão fixa. Mapeamento de `ExternalId` como `char(36)`, igual ao do auth.

### M1 — Migrador de dados (ferramenta)
`tools/Cime.Auth.DataMigrator` com `check`, `copy` e `verify`, relatório em texto. Lista explícita de tabelas e colunas, gerada a partir do modelo; falha se a origem tiver coluna desconhecida (nada é ignorado em silêncio).

### T1 — Ensaio local completo
1. SQL Server 2022 em Docker, schema criado pelas migrações SQL Server atuais (tag anterior à B1), mais dados sintéticos com casos de borda:
   - acentos, emoji e nulos;
   - tamanhos máximos;
   - `LockoutEnd` com offset;
   - datas com 7 casas;
   - claims, tokens e logins;
   - ids com buracos;
   - papéis extras (`gestor`).
2. MySQL 8 local com o `InitialMySql`.
3. `check` → `copy` → `verify` = 0 diferenças.
4. Subir a Cime.Auth local contra o MySQL: login com um usuário migrado (senha original), refresh token, api-key, gestão de usuários.
5. Subir a API de PR contra o MySQL do auth (só o `AuthenticationContext`): nomes na timeline e departamentos.
6. Casos negativos: destino com dados → `copy` recusa; linha alterada depois da cópia → `verify` acusa.

### I1 — Infra (Terraform + docs)
- Secret `mysql-auth-connection`.
- Env `ConnectionStrings__AuthDatabase` em `cime-auth` e `cime-pullrequest`, com o secret de leitura para o PR se houver usuário read-only.
- Mantém `sqlserver-auth-connection` até a Q3.
- `deploy/README.md`: runbook da virada e do rollback.

### Q1 — Ensaio em produção, sem virar (usuário + Claude)
1. Criar o database MySQL do auth no MonsterASP (e, se possível, um usuário read-only para o PR).
2. Backup do SQL Server pelo painel.
3. Usuário roda `check` e `copy` do SQL Server de produção para o MySQL novo, e depois `verify`.
4. Claude sobe a Cime.Auth **local** apontando para o MySQL novo (somente leitura na prática: login de teste) para validar. Depois o database é **esvaziado** para a virada.

### Q2 — Virada (janela curta combinada, fora do horário de uso)
1. `terraform apply` (I1): envs e secret novos. O código antigo ignora.
2. Início da janela: `copy` (destino vazio) → `verify` = 0.
3. Merge do PR → deploy de `cime-auth` e `cime-pullrequest` (~2–3 min) já no MySQL.
4. `verify` de novo (SQL Server × MySQL). Qualquer escrita no SQL Server durante o deploy aparece aqui e é reconciliada (com poucos usuários, esperado: nada ou um `DataUltimoLogin`).
5. Teste: login, api-key, gestão de usuários, nomes na timeline.

**Rollback** (enquanto o SQL Server estiver guardado):
- Voltar o tráfego para a revisão anterior das duas APIs (`gcloud run services update-traffic … --to-revisions=<anterior>=100`), que lê a chave SQL Server antiga.
- Escritas feitas no MySQL depois da virada são listadas pelo `verify` e reaplicadas à mão, se houver.

### Q3 — Limpeza (depois de ~30 dias estável)
Remover `sqlserver-auth-connection`, a env `AuthenticationConnection`, o banco SQL Server do MonsterASP e o suporte a SQL Server da ferramenta.

## 4. Riscos e cuidados
- **Collation/unicidade**: tratada pela D3 e pelo `check` (duplicidades acusadas antes da cópia).
- **Espaços no fim de strings**: o SQL Server ignora na comparação e o MySQL 8 não. O `check` lista os casos; o `copy` preserva o valor exato.
- **Precisão de data**: `datetime2(7)` → `datetime(6)` perde os 100 ns. O `verify` compara em microssegundos.
- **Guid**: o Pomelo usa `char(36)`. `ExternalId` é consultado por igualdade pelo PR e pela timeline. Validado na T1.
- **Api-keys e JWT** não dependem do banco (usam `ExternalId` e a chave JWT), mas são validados na T1 e na Q2.
- **Banco de dev = produção**: não rodar a Cime.Auth local contra o SQL Server de produção com auto-migrate. A B1 usa MySQL local descartável.

## 5. Fora do escopo
Mover o banco para fora do MonsterASP, trocar a região do Cloud Run e unificar a Cime.Auth na API de PR.
