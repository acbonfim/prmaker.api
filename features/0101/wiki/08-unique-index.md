[[_TOC_]]

> **(NEW)** New page in v2.0, pending DBA validation. / Página nova da v2.0, pendente de validação do DBA.

# Unique index naming / Nomenclatura de índice único

**Pattern / Padrão:** `IXU_[schema]_[Table]_[NN]`  **Example / Exemplo:** `IXU_language_Language_01`

## Rules / Regras

| # | English | Português |
|---|---|---|
| IXU1 | The name is `IXU` (uppercase) + schema + table name + a two-digit sequence number (`01`, `02`...), separated by underscore. No column names. | O nome é composto por `IXU` (maiúscula) + schema + nome da tabela + numeral sequencial de dois dígitos (`01`, `02`...), separados por subscrito. Sem nome de coluna. |
| IXU2 | Use a unique index to enforce **business uniqueness** (natural keys such as `Code`, `Email`), separate from the PK. | Utilizar índice único para garantir **unicidade de negócio** (chaves naturais como `Code`, `Email`), separada da PK. |
| IXU3 | Create it with `CREATE UNIQUE NONCLUSTERED INDEX` instead of a `UNIQUE` constraint. The index allows `INCLUDE` and filters. | Criar com `CREATE UNIQUE NONCLUSTERED INDEX` em vez de constraint `UNIQUE`. O índice permite `INCLUDE` e filtros. |
| IXU4 | SQL Server allows only **one NULL** in a unique index. For nullable columns, use a filtered index (`WHERE Column IS NOT NULL`). | O SQL Server permite apenas **um NULL** em índice único. Para colunas que aceitam NULL, usar índice filtrado (`WHERE Coluna IS NOT NULL`). |
| IXU5 | IXU and IX use **separate** sequence numbers. | IXU e IX usam sequências numéricas **independentes**. |
| IXU6 | Existing unique indexes named `IX_..._NN` are renamed to `IXU_..._NN` during each module review (`sp_rename ... N'INDEX'`). | Índices únicos existentes nomeados `IX_..._NN` são renomeados para `IXU_..._NN` na revisão de cada módulo (`sp_rename ... N'INDEX'`). |

## DDL example / Exemplo de DDL

```sql
CREATE UNIQUE NONCLUSTERED INDEX IXU_language_Language_01
    ON language.Language (Code);

-- Nullable column / Coluna que aceita NULL
CREATE UNIQUE NONCLUSTERED INDEX IXU_security_SystemUser_01
    ON security.SystemUser (Email)
    WHERE Email IS NOT NULL;
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List unique indexes and UNIQUE constraints (non-PK) / Listar índices únicos e constraints UNIQUE (exceto PK)
SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS TableName,
       i.name AS IndexName, i.type_desc AS IndexType, i.is_unique_constraint AS IsUniqueConstraint,
       STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns,
       i.filter_definition AS FilterDefinition
  FROM sys.indexes       AS i
  JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
  JOIN sys.columns       AS c  ON c.object_id = ic.object_id AND c.column_id = ic.column_id
 WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
   AND i.is_unique = 1 AND i.is_primary_key = 0
 GROUP BY i.object_id, i.name, i.type_desc, i.is_unique_constraint, i.filter_definition
 ORDER BY SchemaName, TableName, IndexName;
```

## Validation scripts / Scripts de validação

```sql
-- 2. Unique indexes outside the standard IXU_[schema]_[Table]_[NN]
--    Índices únicos fora do padrão IXU_[schema]_[Tabela]_[NN]
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, i.name AS IndexName,
       i.is_unique_constraint AS IsUniqueConstraint
  FROM sys.indexes AS i
  JOIN sys.tables  AS t ON t.object_id = i.object_id
 WHERE i.is_unique = 1 AND i.is_primary_key = 0
   AND SCHEMA_NAME(t.schema_id) <> 'dbo'
   AND i.name COLLATE Latin1_General_BIN NOT LIKE
       CONCAT('IXU[_]', SCHEMA_NAME(t.schema_id), '[_]', t.name, '[_][0-9][0-9]')
 ORDER BY SchemaName, TableName, IndexName;
```
