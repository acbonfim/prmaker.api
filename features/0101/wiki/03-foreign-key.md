[[_TOC_]]

# Foreign key naming / Nomenclatura de chave estrangeira

**Pattern / Padrão:** `FK_[schema]_[Table]_[Column]`  **Example / Exemplo:** `FK_language_StandardLanguage_LanguageId`

## Naming rules / Regras de nomenclatura

| # | English | Português |
|---|---|---|
| FK1 | The FK name uses PascalCase, with no spaces or special characters. | O nome da chave estrangeira utiliza PascalCase, sem espaços ou caracteres especiais. |
| FK2 | The name starts with `FK` (uppercase), and the parts are separated by underscore. | O nome inicia com a sigla `FK` (maiúscula), separando as partes por subscrito. |
| FK3 | The name is `FK` + schema + table + column, using the **origin (child) table only**. The referenced table is not part of the name. | O nome é composto por `FK` + schema + tabela + coluna, usando **somente a tabela de origem (filha)**. A tabela de referência não entra no nome. |
| FK4 **(NEW)** | Composite FK with `SiteId` (multi-site), e.g. `(AreaId, SiteId)`: use only the main column, without `SiteId` → `FK_actionPlan_ActionPlan_AreaId`. Other composite FKs: use the first column. | FK composta com `SiteId` (multi-site), ex.: `(AreaId, SiteId)`: usar apenas a coluna principal, sem o `SiteId` → `FK_actionPlan_ActionPlan_AreaId`. Demais FKs compostas: usar a primeira coluna. |

## Design rules / Regras de modelagem

| # | English | Português |
|---|---|---|
| FK5 | Do not use `ON DELETE CASCADE`. | Não utilizar `ON DELETE CASCADE`. |
| FK6 **(NEW)** | Do not use `ON UPDATE CASCADE` either. Deletes and updates of child rows are done explicitly by the application or the procedure. | Também não utilizar `ON UPDATE CASCADE`. Exclusões e alterações nos registros filhos são feitas explicitamente pela aplicação ou pela procedure. |
| FK7 **(NEW)** | Every FK must have a supporting index whose **leading key columns** are the FK columns. Without it, every DELETE/UPDATE on the parent table scans the child table and holds locks. | Toda FK deve ter um índice de suporte cujas **colunas iniciais da chave** sejam as colunas da FK. Sem ele, todo DELETE/UPDATE na tabela pai varre a tabela filha e mantém bloqueios. |
| FK8 **(NEW)** | Exceptions to FK7: the FK columns are already the leading columns of another index (e.g. of a composite PK), or the referenced table is a small domain table whose rows are never deleted. Document the exception. | Exceções à FK7: as colunas da FK já são as colunas iniciais de outro índice (ex.: de uma PK composta), ou a tabela referenciada é uma tabela de domínio pequena cujos registros nunca são excluídos. Documentar a exceção. |
| FK9 **(NEW)** | FKs are created `WITH CHECK` and must remain enabled and trusted. After bulk loads with `NOCHECK`, re-validate them. | As FKs são criadas `WITH CHECK` e devem permanecer habilitadas e confiáveis. Após cargas com `NOCHECK`, revalidar. |
| FK10 **(NEW)** | The FK column has exactly the same data type as the referenced column. | A coluna da FK tem exatamente o mesmo tipo de dado da coluna referenciada. |

## DDL example / Exemplo de DDL **(NEW)**

```sql
ALTER TABLE language.StandardLanguage WITH CHECK
  ADD CONSTRAINT FK_language_StandardLanguage_LanguageId
      FOREIGN KEY (LanguageId) REFERENCES language.Language (LanguageId);

-- Supporting index / Índice de suporte
CREATE NONCLUSTERED INDEX IX_language_StandardLanguage_01
    ON language.StandardLanguage (LanguageId);

-- Re-validate an untrusted FK / Revalidar uma FK não confiável
ALTER TABLE language.StandardLanguage WITH CHECK CHECK CONSTRAINT FK_language_StandardLanguage_LanguageId;
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List foreign keys with columns, actions and status
--    Listar chaves estrangeiras com colunas, ações e status
SELECT fky.name                                  AS ForeignKeyName,
       SCHEMA_NAME(par.schema_id)                AS ParentSchemaName,
       par.name                                  AS ParentTableName,
       STRING_AGG(COL_NAME(fkc.parent_object_id, fkc.parent_column_id), ', ')
           WITHIN GROUP (ORDER BY fkc.constraint_column_id)          AS ParentColumns,
       SCHEMA_NAME(ref.schema_id)                AS ReferencedSchemaName,
       ref.name                                  AS ReferencedTableName,
       STRING_AGG(COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id), ', ')
           WITHIN GROUP (ORDER BY fkc.constraint_column_id)          AS ReferencedColumns,
       fky.delete_referential_action_desc        AS OnDelete,
       fky.update_referential_action_desc        AS OnUpdate,
       fky.is_disabled                           AS IsDisabled,
       fky.is_not_trusted                        AS IsNotTrusted
  FROM sys.foreign_keys        AS fky
  JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fky.object_id
  JOIN sys.tables              AS par ON par.object_id = fky.parent_object_id
  JOIN sys.tables              AS ref ON ref.object_id = fky.referenced_object_id
 GROUP BY fky.name, par.schema_id, par.name, ref.schema_id, ref.name,
          fky.delete_referential_action_desc, fky.update_referential_action_desc,
          fky.is_disabled, fky.is_not_trusted
 ORDER BY ParentSchemaName, ParentTableName, ForeignKeyName;
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 2. FKs outside the standard (name, CASCADE, disabled or untrusted), with the expected name
--    FKs fora do padrão (nome, CASCADE, desabilitada ou não confiável), com o nome esperado
--    Main column = first FK column that is not SiteId / Coluna principal = primeira coluna da FK que não é SiteId
WITH fk AS
(
    SELECT fky.name AS FkName, SCHEMA_NAME(par.schema_id) AS SchemaName, par.name AS TableName,
           (SELECT TOP (1) COL_NAME(x.parent_object_id, x.parent_column_id)
              FROM sys.foreign_key_columns AS x
             WHERE x.constraint_object_id = fky.object_id
             ORDER BY IIF(COL_NAME(x.parent_object_id, x.parent_column_id) = 'SiteId', 1, 0), x.constraint_column_id) AS MainColumn,
           fky.delete_referential_action_desc AS OnDelete, fky.update_referential_action_desc AS OnUpdate,
           fky.is_disabled AS IsDisabled, fky.is_not_trusted AS IsNotTrusted
      FROM sys.foreign_keys AS fky
      JOIN sys.tables       AS par ON par.object_id = fky.parent_object_id
     WHERE SCHEMA_NAME(par.schema_id) <> 'dbo'
)
SELECT SchemaName, TableName, FkName,
       CONCAT('FK_', SchemaName, '_', TableName, '_', MainColumn) AS ExpectedName,
       OnDelete, OnUpdate, IsDisabled, IsNotTrusted
  FROM fk
 WHERE FkName COLLATE Latin1_General_BIN <> CONCAT('FK_', SchemaName, '_', TableName, '_', MainColumn)
    OR OnDelete <> 'NO_ACTION' OR OnUpdate <> 'NO_ACTION'
    OR IsDisabled = 1 OR IsNotTrusted = 1
 ORDER BY SchemaName, TableName, FkName;

-- 3. FKs without a supporting index (FK columns must be the leading key columns of some index)
--    FKs sem índice de suporte (as colunas da FK devem ser as colunas iniciais de algum índice)
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, fk.name AS FkName,
       STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS FkColumns
  FROM sys.foreign_keys        AS fk
  JOIN sys.tables              AS t   ON t.object_id = fk.parent_object_id
  JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
  JOIN sys.columns             AS c   ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
 WHERE NOT EXISTS
       (   -- an index where every FK column is within the first N key positions (N = number of FK columns)
           -- um índice em que toda coluna da FK está entre as N primeiras posições da chave
           SELECT 1
             FROM sys.indexes AS i
            WHERE i.object_id = fk.parent_object_id
              AND NOT EXISTS
                  (   SELECT 1
                        FROM sys.foreign_key_columns AS fkc2
                       WHERE fkc2.constraint_object_id = fk.object_id
                         AND NOT EXISTS
                             (   SELECT 1
                                   FROM sys.index_columns AS ic
                                  WHERE ic.object_id = i.object_id
                                    AND ic.index_id  = i.index_id
                                    AND ic.column_id = fkc2.parent_column_id
                                    AND ic.key_ordinal BETWEEN 1 AND
                                        (SELECT COUNT(*) FROM sys.foreign_key_columns AS x
                                          WHERE x.constraint_object_id = fk.object_id)
                             )
                  )
       )
 GROUP BY t.schema_id, t.name, fk.name
 ORDER BY SchemaName, TableName, FkName;
```
