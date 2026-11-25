[[_TOC_]]

# Index naming / Nomenclatura de índice

**Pattern / Padrão:** `IX_[schema]_[Table]_[NN]`  **Example / Exemplo:** `IX_alert_Alert_01`, `IX_alert_Alert_02`, `IX_alert_Alert_03`

## Naming rules / Regras de nomenclatura

| # | English | Português |
|---|---|---|
| IX1 | Index names use PascalCase, with no spaces or special characters. | O nome do índice utiliza PascalCase, sem espaços ou caracteres especiais. |
| IX2 | The name starts with `IX` (uppercase), and the parts are separated by underscore. | O nome inicia com a sigla `IX` (maiúscula), separando as partes por subscrito. |
| IX3 | The name is `IX` + schema + table name + a two-digit sequence number, starting at `01` and adding 1 for each new index on the table. | O nome é composto por `IX` + schema + nome da tabela + numeral sequencial de dois dígitos, iniciando em `01` e somando 1 a cada novo índice da tabela. |
| IX4 | Do not add column names to the index name. | Não acrescentar o nome da(s) coluna(s) no nome do índice. |
| IX5 **(NEW)** | Unique indexes use the `IXU` prefix (see [Unique Index (Índice Único)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/1215)). | Índices únicos usam o prefixo `IXU` (ver [Unique Index (Índice Único)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/1215)). |
| IX6 **(NEW)** | A clustered index that is not the PK (exception PK7) follows the same `IX_` pattern. | Um índice clusterizado que não é a PK (exceção PK7) segue o mesmo padrão `IX_`. |

## Design rules / Regras de modelagem **(NEW)**

| # | English | Português |
|---|---|---|
| IX7 | Index every FK (see FK7). | Indexar toda FK (ver FK7). |
| IX8 | Create indexes for columns frequently used in `WHERE`, `JOIN` and `ORDER BY`, prioritizing selective columns. | Criar índices para colunas usadas com frequência em `WHERE`, `JOIN` e `ORDER BY`, priorizando as colunas seletivas. |
| IX9 | No duplicate indexes (same key columns, same order). An index whose key is a left prefix of another index is redundant: review it. | Sem índices duplicados (mesmas colunas da chave, mesma ordem). Um índice cuja chave é prefixo à esquerda de outro é redundante: revisar. |
| IX10 | Use `INCLUDE` for covering indexes on critical queries instead of adding columns to the key. | Usar `INCLUDE` para índices de cobertura em consultas críticas, em vez de adicionar colunas à chave. |
| IX11 | Before dropping an index, check usage (`sys.dm_db_index_usage_stats`). The statistics reset when the instance restarts. | Antes de remover um índice, verificar o uso (`sys.dm_db_index_usage_stats`). As estatísticas são zeradas quando a instância reinicia. |

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List all indexes / Listar todos os índices
SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS TableName,
       i.name AS IndexName, i.type_desc AS IndexType
  FROM sys.indexes AS i
 WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
   AND i.index_id > 0
 ORDER BY SchemaName, TableName, IndexName;

-- 2. List indexes in a specific schema / Listar índices em um schema específico
SELECT t.name AS TableName, i.name AS IndexName, i.type_desc AS IndexType
  FROM sys.indexes AS i
  JOIN sys.tables  AS t ON t.object_id = i.object_id
 WHERE t.schema_id = SCHEMA_ID(N'alert')
   AND i.index_id > 0
 ORDER BY TableName, IndexName;

-- 3. Indexes (non-PK) with schema, table, type, key and included columns
--    Índices (exceto PK) com schema, tabela, tipo, colunas da chave e colunas incluídas
SELECT sch.name      AS SchemaName,
       tab.name      AS TableName,
       idx.name      AS IndexName,
       idx.type_desc AS IndexType,
       idx.is_unique AS IsUnique,
       STRING_AGG(CASE WHEN ics.is_included_column = 0 THEN col.name END, ', ')
           WITHIN GROUP (ORDER BY ics.key_ordinal)    AS KeyColumns,
       STRING_AGG(CASE WHEN ics.is_included_column = 1 THEN col.name END, ', ')
           WITHIN GROUP (ORDER BY ics.index_column_id) AS IncludedColumns,
       idx.filter_definition AS FilterDefinition
  FROM sys.indexes       AS idx
  JOIN sys.tables        AS tab ON tab.object_id = idx.object_id
  JOIN sys.schemas       AS sch ON sch.schema_id = tab.schema_id
  JOIN sys.index_columns AS ics ON ics.object_id = idx.object_id AND ics.index_id  = idx.index_id
  JOIN sys.columns       AS col ON col.object_id = ics.object_id AND col.column_id = ics.column_id
 WHERE idx.is_primary_key = 0
   AND idx.type IN (1, 2)   -- Clustered and Non-Clustered indexes
 GROUP BY sch.name, tab.name, idx.name, idx.type_desc, idx.is_unique, idx.filter_definition
 ORDER BY sch.name, tab.name, idx.name;
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 4. Non-unique indexes outside the standard IX_[schema]_[Table]_[NN]
--    Índices não únicos fora do padrão IX_[schema]_[Tabela]_[NN]
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, i.name AS IndexName, i.type_desc AS IndexType
  FROM sys.indexes AS i
  JOIN sys.tables  AS t ON t.object_id = i.object_id
 WHERE i.index_id > 0
   AND SCHEMA_NAME(t.schema_id) <> 'dbo'
   AND i.is_primary_key = 0
   AND i.is_unique = 0
   AND i.name COLLATE Latin1_General_BIN NOT LIKE
       CONCAT('IX[_]', SCHEMA_NAME(t.schema_id), '[_]', t.name, '[_][0-9][0-9]')
 ORDER BY SchemaName, TableName, IndexName;

-- 5. Duplicate and redundant (left-prefix) indexes / Índices duplicados e redundantes (prefixo à esquerda)
--    Review before dropping: unique or filtered indexes may still be required.
--    Revisar antes de remover: índices únicos ou filtrados podem ser necessários.
WITH k AS
(
    SELECT i.object_id, i.index_id, i.name, i.is_unique, i.is_primary_key, i.has_filter,
           STRING_AGG(CAST(ic.column_id AS VARCHAR(10)) + IIF(ic.is_descending_key = 1, 'D', 'A'), ',')
               WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyCols
      FROM sys.indexes       AS i
      JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
     WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
     GROUP BY i.object_id, i.index_id, i.name, i.is_unique, i.is_primary_key, i.has_filter
)
SELECT OBJECT_SCHEMA_NAME(a.object_id) AS SchemaName, OBJECT_NAME(a.object_id) AS TableName,
       a.name AS IndexName, b.name AS CoveredBy,
       IIF(a.KeyCols = b.KeyCols, 'DUPLICATE', 'REDUNDANT (prefix)') AS Finding,
       a.is_unique AS IsUnique, a.has_filter AS HasFilter
  FROM k AS a
  JOIN k AS b ON  b.object_id = a.object_id
              AND b.index_id <> a.index_id
              AND (   (a.KeyCols = b.KeyCols AND a.is_primary_key = 0 AND (b.is_primary_key = 1 OR a.index_id > b.index_id))
                   OR b.KeyCols LIKE a.KeyCols + ',%')
 WHERE a.is_primary_key = 0
 ORDER BY SchemaName, TableName, IndexName;

-- 6. Unused non-clustered indexes since the last restart (requires VIEW SERVER STATE / VIEW DATABASE STATE)
--    Índices não clusterizados sem uso desde o último restart (requer VIEW SERVER STATE / VIEW DATABASE STATE)
SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS TableName, i.name AS IndexName,
       ISNULL(us.user_seeks, 0) AS Seeks, ISNULL(us.user_scans, 0) AS Scans,
       ISNULL(us.user_lookups, 0) AS Lookups, ISNULL(us.user_updates, 0) AS Updates
  FROM sys.indexes AS i
  LEFT JOIN sys.dm_db_index_usage_stats AS us
         ON us.object_id = i.object_id AND us.index_id = i.index_id AND us.database_id = DB_ID()
 WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
   AND i.type_desc = 'NONCLUSTERED'
   AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
   AND ISNULL(us.user_seeks, 0) + ISNULL(us.user_scans, 0) + ISNULL(us.user_lookups, 0) = 0
 ORDER BY Updates DESC;
```
