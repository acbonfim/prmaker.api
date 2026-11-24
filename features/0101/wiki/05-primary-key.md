[[_TOC_]]

# Primary key naming / Nomenclatura de chave primária

**Pattern / Padrão:** `PK_[schema]_[Table]`  **Example / Exemplo:** `PK_language_Language`

## Naming rules / Regras de nomenclatura

| # | English | Português |
|---|---|---|
| PK1 | The PK name uses PascalCase, with no spaces or special characters. | O nome da primary key utiliza PascalCase, sem espaços ou caracteres especiais. |
| PK2 | The name starts with `PK` (uppercase), and the parts are separated by underscore. | O nome inicia com a sigla `PK` (maiúscula), separando as partes por subscrito. |
| PK3 **(CHANGED)** | The name is `PK` + schema + table name. The key column is **not** part of the name: a table has only one PK, and the name stays short for composite keys (e.g. `PK_rca_AnalysisItem` for `(AnalysisId, ItemId)`). | O nome é composto por `PK` + schema + nome da tabela. A coluna chave **não** entra no nome: a tabela tem só uma PK, e o nome continua curto em chaves compostas (ex.: `PK_rca_AnalysisItem` para `(AnalysisId, ItemId)`). |
| PK4 | v1.0 used `PK_[schema]_[Table]_[Column]`. v2.0 aligns the standard with the existing Global databases, where most PKs already follow `PK_[schema]_[Table]`. | A v1.0 usava `PK_[schema]_[Tabela]_[Coluna]`. A v2.0 alinha o padrão às bases Global existentes, onde a maioria das PKs já segue `PK_[schema]_[Tabela]`. |

## Design rules / Regras de modelagem **(NEW)**

| # | English | Português |
|---|---|---|
| PK5 | Every table must have a primary key. | Toda tabela deve ter chave primária. |
| PK6 | The PK is **CLUSTERED** by default. The ideal clustered key is narrow, unique, stable and ever-increasing (`INT`/`BIGINT IDENTITY`). | A PK é **CLUSTERED** por padrão. A chave clusterizada ideal é estreita, única, estável e crescente (`INT`/`BIGINT IDENTITY`). |
| PK7 | `PRIMARY KEY NONCLUSTERED` is allowed only as a documented exception, for example a random `UNIQUEIDENTIFIER` PK (`NEWID()`), or when another column is a better clustered key (e.g. a date used in range queries). | `PRIMARY KEY NONCLUSTERED` é permitida somente como exceção documentada, por exemplo uma PK `UNIQUEIDENTIFIER` aleatória (`NEWID()`), ou quando outra coluna é melhor chave clusterizada (ex.: data usada em consultas por intervalo). |
| PK8 | If the PK is NONCLUSTERED, the table **must** have another clustered index. Heaps are not allowed. | Se a PK for NONCLUSTERED, a tabela **deve** ter outro índice clusterizado. Heaps não são permitidos. |
| PK9 | Never create an additional index on the same columns as the PK: the PK already creates its own (unique) index. | Nunca criar índice adicional nas mesmas colunas da PK: a PK já cria o próprio índice (único). |
| PK10 | Always declare the PK with an explicit name and type (`CLUSTERED` / `NONCLUSTERED`). | Sempre declarar a PK com nome e tipo explícitos (`CLUSTERED` / `NONCLUSTERED`). |

## DDL example / Exemplo de DDL **(NEW)**

```sql
-- Default (recommended) / Padrão (recomendado)
CONSTRAINT PK_language_Language PRIMARY KEY CLUSTERED (LanguageId)

-- Documented exception / Exceção documentada: GUID PK + clustered index on another column
CONSTRAINT PK_rca_Analysis PRIMARY KEY NONCLUSTERED (AnalysisId)
CREATE CLUSTERED INDEX IX_rca_Analysis_01 ON rca.Analysis (CreatedDate);
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List primary keys / Listar chaves primárias
SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS TableName, i.name AS PkName
  FROM sys.indexes AS i
 WHERE i.is_primary_key = 1
 ORDER BY SchemaName, TableName;

-- 2. List primary keys in a specific schema / Listar chaves primárias em um schema específico
SELECT t.name AS TableName, i.name AS PkName
  FROM sys.indexes AS i
  JOIN sys.tables  AS t ON t.object_id = i.object_id
 WHERE i.is_primary_key = 1
   AND t.schema_id = SCHEMA_ID(N'language');

-- 3. Primary keys with schema, table, type and column(s)
--    Chaves primárias com schema, tabela, tipo e coluna(s)
SELECT sch.name      AS SchemaName,
       tab.name      AS TableName,
       idx.name      AS PkName,
       idx.type_desc AS IndexType,          -- CLUSTERED / NONCLUSTERED
       STRING_AGG(col.name, ', ') WITHIN GROUP (ORDER BY ics.key_ordinal) AS PkColumns
  FROM sys.indexes       AS idx
  JOIN sys.tables        AS tab ON tab.object_id = idx.object_id
  JOIN sys.schemas       AS sch ON sch.schema_id = tab.schema_id
  JOIN sys.index_columns AS ics ON ics.object_id = idx.object_id AND ics.index_id  = idx.index_id
  JOIN sys.columns       AS col ON col.object_id = ics.object_id AND col.column_id = ics.column_id
 WHERE idx.is_primary_key = 1
 GROUP BY sch.name, tab.name, idx.name, idx.type_desc
 ORDER BY sch.name, tab.name;
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 4. PKs outside the standard (wrong name or NONCLUSTERED), with the expected name
--    PKs fora do padrão (nome incorreto ou NONCLUSTERED), com o nome esperado
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, i.name AS PkName, i.type_desc AS IndexType,
       CONCAT('PK_', SCHEMA_NAME(t.schema_id), '_', t.name) AS ExpectedName
  FROM sys.indexes AS i
  JOIN sys.tables  AS t ON t.object_id = i.object_id
 WHERE i.is_primary_key = 1
   AND SCHEMA_NAME(t.schema_id) <> 'dbo'
   AND t.name <> '__EFMigrationHistory'
   AND (   i.name COLLATE Latin1_General_BIN <> CONCAT('PK_', SCHEMA_NAME(t.schema_id), '_', t.name)
        OR i.type_desc <> 'CLUSTERED')
 ORDER BY SchemaName, TableName;

-- 5. Indexes that duplicate the PK columns / Índices que duplicam as colunas da PK
WITH k AS
(
    SELECT i.object_id, i.index_id, i.name, i.is_primary_key,
           STRING_AGG(CAST(ic.column_id AS VARCHAR(10)), ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyCols
      FROM sys.indexes       AS i
      JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
     WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1
     GROUP BY i.object_id, i.index_id, i.name, i.is_primary_key
)
SELECT OBJECT_SCHEMA_NAME(ix.object_id) AS SchemaName, OBJECT_NAME(ix.object_id) AS TableName,
       pk.name AS PkName, ix.name AS DuplicateIndex
  FROM k AS pk
  JOIN k AS ix ON ix.object_id = pk.object_id AND ix.KeyCols = pk.KeyCols AND ix.index_id <> pk.index_id
 WHERE pk.is_primary_key = 1
 ORDER BY SchemaName, TableName;
```
