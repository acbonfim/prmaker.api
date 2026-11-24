[[_TOC_]]

# View naming / Nomenclatura de view

**Pattern / Padrão:** `[schema].vw[Name]`  **Example / Exemplo:** `language.vwStandardLanguage`

## Rules / Regras

| # | English | Português |
|---|---|---|
| V1 | View names use **PascalCase**, with no spaces or special characters. | O nome da view utiliza **PascalCase**, sem espaços ou caracteres especiais. |
| V2 | The name starts with the prefix `vw` (lowercase), followed by the name in PascalCase. | O nome inicia com a sigla `vw` (minúscula), seguida do nome em PascalCase. |
| V3 | Do not use underscore (`_`) in view names. | Não utilizar subscrito (`_`) no nome da view. |
| V4 | New or migrated projects (Global database) use the module schema. | Projetos novos ou migrados para a base Global utilizam o schema do módulo. |
| V5 | Legacy modules and views not yet migrated keep `dbo` as the default schema. | Módulos antigos e views não migradas continuam utilizando `dbo` como schema padrão. |
| V6 **(NEW)** | Do not use `SELECT *` in views: list the columns explicitly. | Não utilizar `SELECT *` em views: listar as colunas explicitamente. |
| V7 **(NEW)** | Always reference tables with the schema inside the view. | Sempre referenciar as tabelas com o schema dentro da view. |

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List all views / Listar todas as views
SELECT SCHEMA_NAME(v.schema_id) AS SchemaName, v.name AS ViewName
  FROM sys.views AS v
 ORDER BY SchemaName, ViewName;

-- 2. List views in a specific schema / Listar views em um schema específico
SELECT v.name AS ViewName
  FROM sys.views AS v
 WHERE v.schema_id = SCHEMA_ID(N'language');

-- 3. List views (INFORMATION_SCHEMA) / Listar views (INFORMATION_SCHEMA)
SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS ViewName
  FROM INFORMATION_SCHEMA.VIEWS
 ORDER BY TABLE_SCHEMA, TABLE_NAME;

-- 4. Show the definition text of a view / Exibir o texto de definição de uma view
EXEC sp_helptext N'language.vwStandardLanguage';

-- 5. View definition (full text, no 4000-char limit)
--    Definição da view (texto completo, sem o limite de 4000 caracteres)
SELECT OBJECT_SCHEMA_NAME(m.object_id) AS SchemaName,
       OBJECT_NAME(m.object_id)        AS ViewName,
       m.definition                    AS Definition
  FROM sys.sql_modules AS m
 WHERE m.object_id = OBJECT_ID(N'language.vwStandardLanguage');
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 6. Views outside the standard (non-dbo schemas) / Views fora do padrão (schemas diferentes de dbo)
SELECT SCHEMA_NAME(v.schema_id) AS SchemaName, v.name AS ViewName
  FROM sys.views AS v
 WHERE SCHEMA_NAME(v.schema_id) <> 'dbo'
   AND (   v.name NOT LIKE 'vw[A-Z]%'   COLLATE Latin1_General_BIN
        OR v.name LIKE '%[^A-Za-z0-9]%' COLLATE Latin1_General_BIN)
 ORDER BY SchemaName, ViewName;

-- 7. Views using SELECT * / Views que usam SELECT *
SELECT OBJECT_SCHEMA_NAME(m.object_id) AS SchemaName, OBJECT_NAME(m.object_id) AS ViewName
  FROM sys.sql_modules AS m
  JOIN sys.views       AS v ON v.object_id = m.object_id
 WHERE m.definition LIKE '%SELECT%*%FROM%'
 ORDER BY SchemaName, ViewName;   -- review manually: may include COUNT(*) / revisar manualmente: pode incluir COUNT(*)
```
