[[_TOC_]]

> **(NEW)** New page in v2.0, pending DBA validation. / Página nova da v2.0, pendente de validação do DBA.

# Check constraint naming / Nomenclatura de check constraint

**Pattern / Padrão:** `CK_[schema]_[Table]_[Column]`  **Example / Exemplo:** `CK_language_Language_Status`

## Rules / Regras

| # | English | Português |
|---|---|---|
| CK1 | The name is `CK` (uppercase) + schema + table name + column name, separated by underscore. | O nome é composto por `CK` (maiúscula) + schema + nome da tabela + nome da coluna, separados por subscrito. |
| CK2 | For a rule that involves more than one column, replace the column with a short rule name in PascalCase (e.g. `CK_rca_Analysis_EndDateAfterStartDate`). | Para regra que envolve mais de uma coluna, substituir a coluna por um nome curto da regra em PascalCase (ex.: `CK_rca_Analysis_EndDateAfterStartDate`). |
| CK3 | Always name the constraint explicitly: no system-generated names. | Sempre nomear a constraint explicitamente: sem nomes gerados pelo sistema. |
| CK4 | Keep the expressions simple. Do not call user-defined functions in a CHECK: they run for every row and hurt performance and concurrency. | Manter expressões simples. Não chamar funções definidas pelo usuário dentro do CHECK: elas executam para cada linha e prejudicam performance e concorrência. |
| CK5 | CHECK constraints must be enabled and trusted (`WITH CHECK`). | As constraints CHECK devem estar habilitadas e confiáveis (`WITH CHECK`). |

## DDL example / Exemplo de DDL

```sql
ALTER TABLE language.Language WITH CHECK
  ADD CONSTRAINT CK_language_Language_Status CHECK (Status IN ('A', 'I'));

ALTER TABLE rca.Analysis WITH CHECK
  ADD CONSTRAINT CK_rca_Analysis_EndDateAfterStartDate CHECK (EndDate IS NULL OR EndDate >= StartDate);

-- Re-validate an untrusted constraint / Revalidar constraint não confiável
ALTER TABLE language.Language WITH CHECK CHECK CONSTRAINT CK_language_Language_Status;
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List check constraints / Listar check constraints
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       COL_NAME(cc.parent_object_id, cc.parent_column_id) AS ColumnName,   -- NULL = table-level rule / regra de tabela
       cc.name AS CheckName, cc.definition AS Definition,
       cc.is_disabled AS IsDisabled, cc.is_not_trusted AS IsNotTrusted, cc.is_system_named AS IsSystemNamed
  FROM sys.check_constraints AS cc
  JOIN sys.tables            AS t ON t.object_id = cc.parent_object_id
 ORDER BY SchemaName, TableName, CheckName;
```

## Validation scripts / Scripts de validação

```sql
-- 2. Check constraints outside the standard (name, disabled or untrusted)
--    Check constraints fora do padrão (nome, desabilitada ou não confiável)
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, cc.name AS CheckName,
       COL_NAME(cc.parent_object_id, cc.parent_column_id) AS ColumnName,
       cc.is_disabled AS IsDisabled, cc.is_not_trusted AS IsNotTrusted, cc.is_system_named AS IsSystemNamed
  FROM sys.check_constraints AS cc
  JOIN sys.tables            AS t ON t.object_id = cc.parent_object_id
 WHERE SCHEMA_NAME(t.schema_id) <> 'dbo'
   AND (   cc.name COLLATE Latin1_General_BIN NOT LIKE CONCAT('CK[_]', SCHEMA_NAME(t.schema_id), '[_]', t.name, '[_]%')
        OR cc.is_system_named = 1
        OR cc.is_disabled = 1
        OR cc.is_not_trusted = 1)
 ORDER BY SchemaName, TableName, CheckName;
```
