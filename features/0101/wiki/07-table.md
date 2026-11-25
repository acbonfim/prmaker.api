[[_TOC_]]

# Table naming / Nomenclatura de tabela

**Pattern / Padrão:** `[schema].[TableName]`  **Example / Exemplo:** `language.StandardLanguage`

## Rules / Regras

| # | English | Português |
|---|---|---|
| T1 | Table names use **PascalCase**: each word starts with an uppercase letter, with no spaces or special characters. | O nome da tabela utiliza **PascalCase**: cada palavra começa com letra maiúscula, sem espaços ou caracteres especiais. |
| T2 | Tables use the module schema (camelCase) for new projects and for tables migrated from the Local databases to the Global database. | Tabelas usam o schema do módulo (camelCase) em projetos novos e nas tabelas migradas dos bancos Local para o Global. |
| T3 | Legacy modules and tables not yet migrated keep the `dbo` schema. | Módulos antigos e tabelas não migradas continuam no schema `dbo`. |
| T4 **(NEW)** | No prefixes such as `TB_`, `TB_MST_` or `tbl` in new tables. | Não usar prefixos como `TB_`, `TB_MST_` ou `tbl` em tabelas novas. |
| T5 **(NEW)** | Every table must have a primary key, CLUSTERED by default (see [Primary Key (Chave Primária)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/261)). | Toda tabela deve ter chave primária, CLUSTERED por padrão (ver [Primary Key (Chave Primária)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/261)). |
| T6 **(NEW)** | Column names also use PascalCase. The PK column is `[TableName]Id` (e.g. `LanguageId`). | Os nomes de colunas também usam PascalCase. A coluna da PK é `[NomeTabela]Id` (ex.: `LanguageId`). |
| T7 **(NEW)** | All constraints are declared with explicit names (PK, FK, DF, CK). | Todas as constraints são declaradas com nome explícito (PK, FK, DF, CK). |

## DDL example / Exemplo de DDL **(NEW)**

```sql
CREATE TABLE language.Language
(
    LanguageId  INT IDENTITY(1,1) NOT NULL,
    Code        VARCHAR(10)       NOT NULL,
    Name        NVARCHAR(100)     NOT NULL,
    Status      CHAR(1)           NOT NULL CONSTRAINT DF_language_Language_Status   DEFAULT ('A'),
    IsActive    BIT               NOT NULL CONSTRAINT DF_language_Language_IsActive DEFAULT (1),
    CONSTRAINT PK_language_Language PRIMARY KEY CLUSTERED (LanguageId),
    CONSTRAINT CK_language_Language_Status     CHECK (Status IN ('A', 'I'))
);

CREATE UNIQUE NONCLUSTERED INDEX IXU_language_Language_01 ON language.Language (Code);
```

## SQL Server scripts / Scripts SQL Server

```sql
-- 1. List tables / Listar tabelas
--    (use TABLE_TYPE = 'VIEW' to list views / usar TABLE_TYPE = 'VIEW' para listar views)
SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName
  FROM INFORMATION_SCHEMA.TABLES
 WHERE TABLE_TYPE = 'BASE TABLE'
 ORDER BY TABLE_SCHEMA, TABLE_NAME;

-- 2. List tables with schema and dates / Listar tabelas com schema e datas
SELECT s.name AS SchemaName, t.name AS TableName, t.create_date AS CreateDate, t.modify_date AS ModifyDate
  FROM sys.tables  AS t
  JOIN sys.schemas AS s ON s.schema_id = t.schema_id
 ORDER BY s.name, t.name;

-- 3. Check whether a table exists in another database (NULL = does not exist)
--    Verificar se a tabela existe em outro banco de dados (NULL = não existe)
SELECT OBJECT_ID(N'DB_SOLV_DEV_LOCAL_CTB.dbo.TB_MST_CAUSA', N'U');

-- 4. Check whether a table exists in the current database
--    Verificar se a tabela existe no banco de dados corrente
SELECT OBJECT_ID(N'dbo.TB_MST_CAUSA', N'U');
```

## Validation scripts / Scripts de validação **(NEW)**

```sql
-- 5. Tables without primary key / Tabelas sem chave primária
SELECT s.name AS SchemaName, t.name AS TableName
  FROM sys.tables  AS t
  JOIN sys.schemas AS s ON s.schema_id = t.schema_id
 WHERE OBJECTPROPERTY(t.object_id, 'TableHasPrimaryKey') = 0
 ORDER BY s.name, t.name;

-- 6. Heaps (tables without clustered index) / Heaps (tabelas sem índice clusterizado)
SELECT s.name AS SchemaName, t.name AS TableName
  FROM sys.tables  AS t
  JOIN sys.schemas AS s ON s.schema_id = t.schema_id
  JOIN sys.indexes AS i ON i.object_id = t.object_id AND i.index_id = 0
 ORDER BY s.name, t.name;

-- 7. Table names outside the standard (non-dbo schemas)
--    Nomes de tabela fora do padrão (schemas diferentes de dbo)
SELECT s.name AS SchemaName, t.name AS TableName
  FROM sys.tables  AS t
  JOIN sys.schemas AS s ON s.schema_id = t.schema_id
 WHERE s.name <> 'dbo'
   AND t.name <> '__EFMigrationHistory'
   AND (   t.name LIKE '%[^A-Za-z0-9]%' COLLATE Latin1_General_BIN   -- special chars / caracteres especiais
        OR t.name NOT LIKE '[A-Z]%'    COLLATE Latin1_General_BIN   -- table must start uppercase / tabela deve iniciar com maiúscula
        OR s.name NOT LIKE '[a-z]%'    COLLATE Latin1_General_BIN   -- schema must start lowercase / schema deve iniciar com minúscula
        OR s.name LIKE '%[^A-Za-z0-9]%' COLLATE Latin1_General_BIN) -- schema without special chars / schema sem caracteres especiais
 ORDER BY s.name, t.name;
```
