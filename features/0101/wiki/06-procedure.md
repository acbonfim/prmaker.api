[[_TOC_]]

# Stored procedure naming / Nomenclatura de procedure

**Pattern / Padrão:** `[schema].pr[Name]`  **Example / Exemplo:** `language.prStandardLanguage`

## Rules / Regras

| # | English | Português |
|---|---|---|
| P1 | Procedure names use **PascalCase**, with no spaces or special characters. | O nome da procedure utiliza **PascalCase**, sem espaços ou caracteres especiais. |
| P2 | The name starts with the prefix `pr` (lowercase), followed by the name in PascalCase. | O nome inicia com a sigla `pr` (minúscula), seguida do nome em PascalCase. |
| P3 | Procedures migrated from the Local databases to the Global database, and new projects, use the module schema. | Procedures migradas dos bancos Local para o Global e projetos novos utilizam o schema do módulo. |
| P4 | Legacy modules and procedures not yet migrated keep the `dbo` schema. | Módulos antigos e procedures não migradas continuam no schema `dbo`. |
| P5 **(NEW)** | Never use the `sp_` prefix: it is reserved for system procedures, and SQL Server looks for these procedures in `master` first. | Nunca utilizar o prefixo `sp_`: ele é reservado para procedures de sistema, e o SQL Server procura essas procedures primeiro no `master`. |
| P6 **(NEW)** | Start the body with `SET NOCOUNT ON;` and reference all objects with the schema. | Iniciar o corpo com `SET NOCOUNT ON;` e referenciar todos os objetos com o schema. |
| P7 **(NEW)** | Use `CREATE OR ALTER PROCEDURE` in deployment scripts. | Utilizar `CREATE OR ALTER PROCEDURE` nos scripts de deploy. |

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List all stored procedures / Listar todas as stored procedures
SELECT SCHEMA_NAME(p.schema_id) AS SchemaName, p.name AS ProcedureName
  FROM sys.procedures AS p
 ORDER BY SchemaName, ProcedureName;

-- 2. List procedures in a specific schema / Listar procedures em um schema específico
SELECT p.name AS ProcedureName
  FROM sys.procedures AS p
 WHERE p.schema_id = SCHEMA_ID(N'language');

-- 3. List procedures with create/alter dates / Listar procedures com datas de criação/alteração
SELECT ROUTINE_SCHEMA AS SchemaName, ROUTINE_NAME AS ProcedureName,
       CREATED AS CreateDate, LAST_ALTERED AS LastAltered
  FROM INFORMATION_SCHEMA.ROUTINES
 WHERE ROUTINE_TYPE = 'PROCEDURE'
 ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;

-- 4. Show the definition text of a procedure / Exibir o texto de definição de uma procedure
EXEC sp_helptext N'language.prStandardLanguage';

-- 5. Procedure definition (full text). Prefer this over INFORMATION_SCHEMA.ROUTINES.ROUTINE_DEFINITION,
--    which is truncated at 4000 characters.
--    Definição da procedure (texto completo). Preferir a ROUTINE_DEFINITION do INFORMATION_SCHEMA,
--    que é truncada em 4000 caracteres.
SELECT OBJECT_SCHEMA_NAME(m.object_id) AS SchemaName,
       OBJECT_NAME(m.object_id)        AS ProcedureName,
       m.definition                    AS Definition
  FROM sys.sql_modules AS m
 WHERE m.object_id = OBJECT_ID(N'language.prStandardLanguage');
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 6. Procedures outside the standard (non-dbo schemas) / Procedures fora do padrão (schemas diferentes de dbo)
SELECT SCHEMA_NAME(p.schema_id) AS SchemaName, p.name AS ProcedureName
  FROM sys.procedures AS p
 WHERE SCHEMA_NAME(p.schema_id) <> 'dbo'
   AND (   p.name NOT LIKE 'pr[A-Z]%'   COLLATE Latin1_General_BIN
        OR p.name LIKE '%[^A-Za-z0-9]%' COLLATE Latin1_General_BIN)
 ORDER BY SchemaName, ProcedureName;

-- 7. Procedures using the sp_ prefix (all schemas) / Procedures com prefixo sp_ (todos os schemas)
SELECT SCHEMA_NAME(p.schema_id) AS SchemaName, p.name AS ProcedureName
  FROM sys.procedures AS p
 WHERE p.name LIKE 'sp[_]%'
   AND p.is_ms_shipped = 0
 ORDER BY SchemaName, ProcedureName;
```
