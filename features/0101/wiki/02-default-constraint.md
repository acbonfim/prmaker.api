[[_TOC_]]

> **(NEW)** New page in v2.0, pending DBA validation. / Página nova da v2.0, pendente de validação do DBA.

# Default constraint naming / Nomenclatura de default constraint

**Pattern / Padrão:** `DF_[schema]_[Table]_[Column]`  **Example / Exemplo:** `DF_language_Language_IsActive`

## Rules / Regras

| # | English | Português |
|---|---|---|
| DF1 | The name is `DF` (uppercase) + schema + table name + column name, separated by underscore. | O nome é composto por `DF` (maiúscula) + schema + nome da tabela + nome da coluna, separados por subscrito. |
| DF2 | **Always name the default explicitly.** Without a name, SQL Server generates one (e.g. `DF__Language__IsAct__3A81B327`) that is different in each environment, so `DROP CONSTRAINT` scripts fail. | **Sempre nomear o default explicitamente.** Sem nome, o SQL Server gera um (ex.: `DF__Language__IsAct__3A81B327`) diferente em cada ambiente, e os scripts de `DROP CONSTRAINT` falham. |
| DF3 | When adding a `NOT NULL` column to an existing table, declare the named default together with the column. | Ao adicionar uma coluna `NOT NULL` em tabela existente, declarar o default nomeado junto com a coluna. |
| DF4 | Before altering or dropping a column with a default, drop the default by its name. | Antes de alterar ou remover uma coluna com default, remover o default pelo nome. |
| DF5 | EF Core migrations (`HasDefaultValue` / `HasDefaultValueSql`) create defaults **without a name**. Name them in the migration (custom SQL) or rename them afterwards with the script below. | Migrations do EF Core (`HasDefaultValue` / `HasDefaultValueSql`) criam defaults **sem nome**. Nomear na migration (SQL customizado) ou renomear depois com o script abaixo. |

## DDL example / Exemplo de DDL

```sql
-- In CREATE TABLE / No CREATE TABLE
IsActive BIT NOT NULL CONSTRAINT DF_language_Language_IsActive DEFAULT (1),

-- New column on an existing table / Nova coluna em tabela existente
ALTER TABLE language.Language
  ADD CreatedDate DATETIME2(0) NOT NULL
      CONSTRAINT DF_language_Language_CreatedDate DEFAULT (SYSDATETIME());

-- Drop / Remover
ALTER TABLE language.Language DROP CONSTRAINT DF_language_Language_CreatedDate;
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List default constraints / Listar default constraints
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, c.name AS ColumnName,
       dc.name AS DefaultName, dc.definition AS Definition, dc.is_system_named AS IsSystemNamed
  FROM sys.default_constraints AS dc
  JOIN sys.tables              AS t ON t.object_id = dc.parent_object_id
  JOIN sys.columns             AS c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 ORDER BY SchemaName, TableName, ColumnName;
```

## Validation scripts / Scripts de validação

```sql
-- 2. Defaults outside the standard (including system-named), with a rename script
--    Defaults fora do padrão (incluindo nomes gerados pelo sistema), com script de renomeação
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, c.name AS ColumnName,
       dc.name AS DefaultName, dc.is_system_named AS IsSystemNamed,
       CONCAT('DF_', SCHEMA_NAME(t.schema_id), '_', t.name, '_', c.name) AS ExpectedName,
       CONCAT('EXEC sp_rename N''', QUOTENAME(SCHEMA_NAME(t.schema_id)), '.', QUOTENAME(dc.name),
              ''', N''DF_', SCHEMA_NAME(t.schema_id), '_', t.name, '_', c.name, ''', N''OBJECT'';') AS RenameScript
  FROM sys.default_constraints AS dc
  JOIN sys.tables              AS t ON t.object_id = dc.parent_object_id
  JOIN sys.columns             AS c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 WHERE SCHEMA_NAME(t.schema_id) <> 'dbo'
   AND dc.name COLLATE Latin1_General_BIN <> CONCAT('DF_', SCHEMA_NAME(t.schema_id), '_', t.name, '_', c.name)
 ORDER BY SchemaName, TableName, ColumnName;
```
