# Feature 0015 — API principal do MySQL para o PostgreSQL

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0015` (worktree `../prform.api-0015`), a partir de `master`. Só backend.
> Base: a 0014 (auth no Postgres `db70140`, migrador `tools/Cime.Auth.DataMigrator`, runbook em `deploy/README.md`).

## 1. Análise

### Contextos e tabelas (hoje no MySQL `db31021`)
| Contexto | Projeto | Tabelas | Migrações |
|---|---|---|---|
| `DefaultContext` | `solvace.prform.infra` | `Forms`, `PullRequests`, `PluginConfigurations`, `Plugins`, `Handovers`, `PullRequestsGithub`, `UserPluginConfigurations` | 19 |
| `VacationContext` | `solvace.vacations.infra` | `VacationRequests`, `UserVacationBalances` | 3 |
| `TimelineContext` | `solvace.timeline.infra` | `TimelineEntries` | 4 |

- **Sem FKs entre contextos** (a timeline liga por `CardNumber`, sem FK). Dentro do `DefaultContext`: `PullRequests` → `PullRequestsGithub`, `Plugins` → `PluginConfigurations`/`UserPluginConfigurations`.
- Índices únicos: `PullRequests.CardNumber`; `PullRequestsGithub (RepositoryId, GithubPrNumber)`; `UserPluginConfigurations (PluginId, UserExternalId)`; `UserVacationBalances (UserId, AcquisitionPeriodStart, AcquisitionPeriodEnd)`; `TimelineEntries.SourceMessageId`.
- Os 3 contextos compartilham o `__EFMigrationsHistory` do `db31021`. As migrações rodam no startup com `GET_LOCK` (`StartupMigrator`), fora de Development.

### SQL específico do MySQL (pouco)
- `UseMySql(…, ServerVersion.AutoDetect(…))` em `Program.cs`, `VacationModuleExtensions` e `TimelineModuleExtensions`.
- `HasColumnType("longtext")` em 3 colunas (`DefaultContext` ×2 e `TimelineContext`) → no Postgres, `text`.
- `GET_LOCK`/`RELEASE_LOCK` no `StartupMigrator`.
- **Migrações com dados** (`Sql`/`InsertData`/`UpdateData`): `ConsolidatePullRequestPerCard`, `AddPullRequestGithub`, `AddTeamsPlugin`, `AddDevOpsActionsToAIConfigurations`, `AdjustPullRequestSummary`. Em produção os dados vêm da cópia. Um banco novo vazio não teria o plugin do Teams nem os prompts padrão (os outros plugins já são criados pela tela).

### Datas — o ponto delicado (resolvido na auditoria 1.1)
O Npgsql é rígido: `timestamp with time zone` exige `DateTime.Kind = Utc`, e `timestamp without time zone` recusa `Utc`. No código há 27 `DateTime.UtcNow`, 16 `DateTimeOffset.UtcNow` e 5 `DateTime.Today`.

| Grupo | Colunas | Como são gravadas | Mapeamento proposto |
|---|---|---|---|
| Auditoria/instantes | `CreatedAt`, `UpdatedAt` (17/16 entidades), `SummaryUpdatedAt`, `SummaryPublishedAt`, `StatusSyncedAt` | `DateTimeOffset.UtcNow` | `timestamp with time zone` (padrão do Npgsql) |
| Instantes em `DateTime` | `ApprovedByManagerAt`, `AuthorizedByHRAt`, `DeletedAt`, `TimelineEntry.Date` (a confirmar) | `DateTime.UtcNow` (a confirmar coluna a coluna) | `timestamp with time zone` |
| Datas de calendário | `StartDate`, `EndDate`, `AcquisitionPeriodStart/End`, `UsagePeriodStart/End` | entrada do usuário, comparadas com `DateTime.Today` | **`date`** |

- **Risco de mudança visível**: hoje o MySQL devolve `DateTime` com `Kind=Unspecified`, e o JSON sai **sem `Z`**. Com `timestamptz`, volta `Kind=Utc` e o JSON sai **com `Z`**, e o navegador converte para o fuso local (a tela mostraria outra hora). A B0 decide coluna a coluna e a T1 compara o JSON das respostas antes e depois. Se houver diferença, normaliza-se na leitura (value converter para `Unspecified`) para manter o contrato.
- Se a auditoria achar mistura que não dá para separar, o plano B é o switch `Npgsql.EnableLegacyTimestampBehavior` (só no processo da API principal; a auth é outro serviço). Funciona, mas é desaconselhado a longo prazo.

### Texto (maiúsculas e espaços)
- O MySQL compara sem diferenciar maiúsculas (collation `_ci`); o Postgres diferencia. As consultas encontradas comparam `CardNumber` (números) por igualdade; a busca por nome de branch roda em memória (`StringComparison.OrdinalIgnoreCase`). **Risco baixo**, mas a B0 revisa todas as consultas LINQ com texto.
- As collations antigas `_general_ci` do MySQL ignoram espaço no fim (`'123' = '123 '`); o Postgres não. O `check` do migrador avisa espaços no início ou no fim em colunas usadas em busca (`CardNumber`, nomes de plugin, etc.).

### Infra
- O `db70140` (Postgres 18.6 em Windows, atrás de proxy TLS 1.3) já tem o schema `auth`. Os módulos novos entram no mesmo database.
- A API principal já tem Npgsql 8.0.2 (0014, `AuthenticationContext`).
- Terraform: `mysql-default-connection` → `ConnectionStrings__DefaultConnection` na `cime-pullrequest`.

## 1.1 Auditoria (B0, 2026-09-26)

### Datas: 22 colunas, todas `datetime(6)` no MySQL
| Tipo .NET | Colunas | Como são gravadas | Mapeamento |
|---|---|---|---|
| `DateTimeOffset` | `CreatedAt`/`UpdatedAt` (todas as entidades com auditoria), `SummaryUpdatedAt`, `SummaryPublishedAt`, `StatusSyncedAt` | `DateTimeOffset.UtcNow` (offset 0) | **`timestamptz`** (padrão do Npgsql). Lê com offset 0, e o JSON continua `…+00:00` |
| `DateTime` (instantes) | `VacationRequests.ApprovedByManagerAt`/`AuthorizedByHRAt`, `Plugins.DeletedAt` | `DateTime.UtcNow` | **`timestamp without time zone`** + conversor que grava com `Kind=Unspecified` |
| `DateTime` (calendário) | `VacationRequests.StartDate`/`EndDate`, `UserVacationBalances.AcquisitionPeriodStart/End`, `UsagePeriodStart/End` | Vêm do front com `toISOString()` (ex.: `2026-10-01T03:00:00Z`, `Kind=Utc`). Os períodos passam por `.Date`; `StartDate`/`EndDate` **guardam a hora** (03:00 no Brasil) | idem: **`timestamp without time zone`** + conversor `Unspecified` |

**Por quê**: o MySQL/Pomelo ignora o `Kind` (grava o relógio como veio) e devolve `Unspecified`, então o JSON sai **sem `Z`**. O Npgsql recusa `Kind=Utc` em `timestamp` e exige `Utc` em `timestamptz`. Com o conversor, o comportamento é **idêntico ao do MySQL**:
- mesmos valores;
- mesmo JSON;
- as consultas com parâmetro `DateTime` (`AcquisitionPeriodStart == periodStart.Date`, faixas de férias) funcionam, porque o conversor também se aplica aos parâmetros.

Converter as datas de calendário para `date`/`DateOnly` seria mais correto, mas muda valor (a hora 03:00) e contrato. Fica como melhoria futura das férias.

`DateTime.Today` só aparece em comparações em memória (validação de férias), sem efeito no banco.

### Texto
| Consulta | Coluna | Risco no Postgres (diferencia maiúsculas, não ignora espaço no fim) | Ação |
|---|---|---|---|
| PullRequest/Handover/Timeline/GitHub por `CardNumber` (8 consultas) | `CardNumber` | dígitos: maiúsculas não importam; espaço no fim, sim (a entrada já faz `Trim`) | nenhuma no código; o `check` avisa valores com espaço |
| `PullRequestGithubApplication` (upsert por `RepositoryId`+`GithubPrNumber`; legado por `RepositoryId`+`BranchPrefix`+`BranchName`) | `RepositoryId`, `BranchPrefix`, `BranchName` | hoje os dois lados vêm da mesma seleção, mas se a caixa divergir o MySQL acha o registro e o Postgres criaria um duplicado | **comparação explícita sem diferenciar maiúsculas** (`ToLower()` nos dois lados) |
| `FormApplication` por `EnvironmentName` | `EnvironmentName` | idem | **comparação explícita sem diferenciar maiúsculas** |
| Timeline por `SourceMessageId` | id de mensagem do Teams | valor exato, gerado pelo Teams | nenhuma |
| Integrações pessoais por `UserExternalId` | `Guid` | — | nenhuma |

- Nenhum `OrderBy` por texto no banco (as ordenações são por data).
- O `check` do perfil `prform` imprime a collation das colunas de texto da origem (para saber se o MySQL de produção ignora espaço no fim, `PAD SPACE`) e avisa espaços no início/fim nas colunas acima.

### Contrato do JSON
Com o mapeamento acima, **nenhuma mudança esperada**. A T1 confirma com diff das respostas (MySQL × Postgres).

## 2. Decisões

### D1 — Mesmo database, um schema por módulo
`prform` (`DefaultContext`), `vacations` (`VacationContext`) e `timeline` (`TimelineContext`), ao lado de `auth`. Cada contexto com `HasDefaultSchema(...)` e `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`. Nomes no padrão do EF (como na 0014).

### D2 — Chave nova `ConnectionStrings:PrformDatabase` para os 3 contextos
Mesmo racional da 0014: a env entra antes do deploy e o rollback (revisão anterior) continua lendo a `DefaultConnection` (MySQL). Secret `postgres-prform-connection`. Hoje tem o mesmo valor do `postgres-auth-connection`; ficam separados para poder usar usuários diferentes depois.

### D3 — Migrações do zero por contexto
`InitialPostgres` em cada um dos 3 projetos. As 26 migrações MySQL saem (ficam no histórico do git). As migrações que só mexiam em dados não precisam de equivalente: os dados vêm da cópia. Seeder idempotente só para o que um banco novo precisa para funcionar (plugin do Teams e prompts padrão do "AI Configurations"), avaliado na B1.

### D4 — Lock de migração
`pg_try_advisory_lock` com prazo, cobrindo os 3 contextos (substitui o `GET_LOCK`), com chave diferente da auth.

### D5 — Migrador generalizado: `tools/Cime.DataMigrator`
Evolução do `Cime.Auth.DataMigrator`:
- **Perfis**: `auth` (origem SQL Server, já usado na 0014) e `prform` (origem MySQL via MySqlConnector).
- **Tabelas, colunas, chave primária e ordem por FKs lidas dos modelos EF de destino**, sem lista escrita à mão. Falha se a origem tiver coluna que o modelo não tem.
- **Conversões da origem MySQL**:
  - `char(36)` → `uuid`;
  - `tinyint(1)` → `boolean`;
  - `datetime(6)` → `timestamptz` (a origem é tratada como UTC; a B0 confirma) ou `date` (confere que a hora é 00:00; senão, erro);
  - `longtext` → `text`;
  - datas zeradas do MySQL (`0000-00-00`) → erro no `check`.
- `reset` passa a ser **por schema** (`--schema prform|vacations|timeline`), e nunca toca no `auth`.
- Mantém: transação única, ids originais, `setval` das sequências, `verify` por hash, nenhum valor impresso.

### D6 — Desenvolvimento local fora da produção
`appsettings.Development.json` aponta para um Postgres local (Docker), e não mais para o banco de produção. Resolve a armadilha "banco de dev = produção" (memória `dev-db-is-prod`). Vem com um `docker-compose.yml` para o dev subir o Postgres.

## 3. Fases

| Onda | Fases |
|---|---|
| 1 | B0 (auditoria), M1 (migrador generalizado) |
| 2 | B1 (contextos no Postgres) |
| 3 | T1 (ensaio local) |
| 4 | I1 (Terraform + runbook) |
| 5 | Q1 (ensaio com os dados reais) → Q2 (virada) |

### B0 — Auditoria de datas e textos (sem mudar comportamento)
- Coluna a coluna: tipo .NET, como é gravada (`UtcNow`/`Today`/entrada do usuário), como é lida e serializada. Resultado: tabela de mapeamento final (`timestamptz`/`date`) e lista de pontos que precisam de value converter para manter o JSON igual.
- Todas as consultas LINQ com texto (igualdade, `Contains`, `StartsWith`, `OrderBy` em texto) nos 3 módulos e no GitHub/Azure.
- Entregável: seção "Auditoria" neste plano, revisada antes da B1.

### M1 — `tools/Cime.DataMigrator` (perfis `auth` e `prform`)
Refatoração do migrador da 0014 conforme D5. Os testes da 0014 continuam passando no perfil `auth`.

### B1 — Os 3 contextos no Postgres
- `UseNpgsql(PrformDatabase)` com schema e histórico próprios; `text` no lugar de `longtext`; mapeamento de datas da B0 (convenções por contexto + value converters onde preciso).
- `InitialPostgres` × 3 (factory de design-time temporária); `StartupMigrator` com `pg_try_advisory_lock`; seeder idempotente (D3).
- `appsettings.Development.json` + `docker-compose.yml` (D6).
- Validação: build; migrações sobem, descem e sobem num Postgres local que já tem o schema `auth` (nada fora dos 3 schemas é tocado).

### T1 — Ensaio local completo (Docker: MySQL 8 → Postgres 18)
1. MySQL local com o schema das migrações atuais (tag anterior à B1), criado pela própria API antiga. Dados sintéticos com casos de borda:
   - JSON grandes em `Options`/`FieldSettings`;
   - prompts com acentos, emoji e quebras de linha;
   - resumos e descrições longos;
   - card com vários PRs do GitHub;
   - férias com datas de calendário;
   - entradas de timeline apagadas (`DeletedAt`);
   - plugins pessoais;
   - ids salteados;
   - espaço no fim de `CardNumber`.
2. `schema` → `check` → `copy` → `verify` = 0 diferenças; conferência independente de valores (datas, JSON, Guids) e sequências.
3. **Comparação de contrato**: a mesma bateria de chamadas à API contra MySQL e contra Postgres, com diff do JSON (card, PRs, plugins, integrações pessoais, férias, saldos, timeline, handovers).
4. Casos negativos (destino cheio, divergência depois da cópia, `reset` por schema sem tocar no `auth`).
5. Cuidado registrado na 0014: rodar a API local com o SMTP/integrações externas desligados (DevOps, GitHub, Teams, IA), para nada sair para fora.

### I1 — Infra
- Secret `postgres-prform-connection` → `ConnectionStrings__PrformDatabase` na `cime-pullrequest`. A `DefaultConnection` (MySQL) fica até a limpeza (0016).
- `deploy/README.md`: runbook da virada e do rollback do PR (mesmo formato da 0014).

### Q1 — Ensaio com os dados reais, sem virar
- `copy` + `verify` do MySQL de produção para os 3 schemas do `db70140`.
- API local contra esse Postgres, com integrações desligadas; o usuário navega pelo app local (card, férias, timeline).
- `reset` por schema no fim.

### Q2 — Virada (janela curta)
1. `terraform apply` (env nova; o código antigo ignora).
2. `schema` → `check` → `copy` → `verify` = 0.
3. Merge → deploy da `cime-pullrequest`.
4. `verify` de novo. Escritas no MySQL durante o deploy aparecem aqui e são reconciliadas.
5. Teste no app.

**Rollback**: tráfego da `cime-pullrequest` para a revisão anterior (lê o MySQL). Escritas feitas no Postgres depois da virada são listadas pelo `verify` e reaplicadas à mão.

## 4. Riscos
- **Datas no JSON** (o `Z` a mais): o maior risco de mudança visível. Mitigação: B0 + comparação de contrato na T1.
- **Texto**: consultas que dependiam do `_ci` do MySQL passam a diferenciar maiúsculas. Mitigação: auditoria na B0 e avisos do `check`.
- **Volume**: maior que a auth (uso diário). A janela precisa ser fora do horário; o `verify` pós-deploy pega escritas no meio.
- **Integrações externas no ensaio local**: DevOps, GitHub, Teams e IA com credenciais reais no `appsettings`. Desligar ao rodar local (lição da 0014 com o SMTP).

## 5. Depois desta feature
- **0016 — Limpeza** do MySQL, do SQL Server e do código antigo.
- **.NET 10**: sem o Pomelo, o upgrade (SDK, EF Core 10, Npgsql 10, pacotes) vira uma feature própria. O prazo do suporte ao .NET 8 é 10/11/2026.
