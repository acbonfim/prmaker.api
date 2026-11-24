[[_TOC_]]

| | |
|---|---|
| **Owner / Responsável** | Claudio Arruda |
| **Status** | v2.0 – Draft for DBA review / Rascunho para revisão do DBA |
| **Platform / Plataforma** | Microsoft SQL Server (Global & Local databases) |

> **Legend / Legenda:** items marked **(NEW)** are proposals added in v2.0 and must be validated by the DBA before becoming mandatory.
> Itens marcados com **(NOVO)** são propostas da v2.0 e devem ser validados pelo DBA antes de se tornarem obrigatórios.

# 1. Objective / Objetivo

| English | Português |
|---|---|
| Establish consistent guidelines for naming database objects and define data modeling rules. | Estabelecer diretrizes consistentes para a nomenclatura dos objetos de banco de dados e definir regras de modelagem de dados. |
| Improve the consistency, readability and maintainability of the databases. | Melhorar a consistência, a legibilidade e a manutenção dos bancos de dados. |
| Ensure every object follows the same naming standard, so anyone using the data understands it. | Garantir que todo objeto utilize o mesmo padrão de nomenclatura, auxiliando o entendimento de todos que utilizam os dados. |
| This document will be updated as new tools, routines and commands for maintenance and data validation become available. | Este documento será atualizado conforme surgirem novas ferramentas, rotinas e comandos de manutenção e validação dos dados. |
| This document is the basis for the data dictionary and for the coding specifications of database objects. | Este documento é a base para o dicionário de dados e para as especificações de codificação dos objetos de banco de dados. |

# 2. Scope / Escopo

| English | Português |
|---|---|
| Applies to **all new objects** and to **modules being migrated** from the Local databases to the Global database. | Aplica-se a **todos os objetos novos** e aos **módulos em migração** dos bancos Local para o Global. |
| Legacy modules and objects not yet migrated keep the `dbo` schema and their current names until they are migrated. | Módulos antigos e objetos não migrados continuam no schema `dbo` e com os nomes atuais até serem migrados. |
| Every module review (e.g. RCA) must validate tables, PK, FK, IX, IXU, DF and CK against this standard. | Toda revisão de módulo (ex.: RCA) deve validar tabelas, PK, FK, IX, IXU, DF e CK com base neste padrão. |

# 3. General rules / Regras gerais

| # | English | Português |
|---|---|---|
| G1 | Object names use **PascalCase** (each word starts with an uppercase letter) and follow the naming rules in section 4. | Nomes de objetos usam **PascalCase** (cada palavra começa com letra maiúscula) e seguem as regras de nomenclatura da seção 4. |
| G2 | Schema names follow the rules in section 5 (**camelCase**), one schema per module. | Nomes de schema seguem as regras da seção 5 (**camelCase**), um schema por módulo. |
| G3 | Constraint and index names use the prefix + `_` separator: `PREFIX_[schema]_[Table]_[...]`. | Nomes de constraints e índices usam prefixo + separador `_`: `PREFIXO_[schema]_[Tabela]_[...]`. |
| G4 **(NEW)** | **Always name constraints explicitly.** System-generated names (e.g. `DF__Language__IsAct__3A81B327`) are forbidden: they differ between environments and break migration scripts. | **Sempre nomear as constraints explicitamente.** Nomes gerados pelo sistema (ex.: `DF__Language__IsAct__3A81B327`) são proibidos: mudam entre ambientes e quebram os scripts de migração. |
| G5 **(NEW)** | Maximum name length is 128 characters (`sysname`). If a name would exceed it, abbreviate the column part and document it. | O tamanho máximo do nome é 128 caracteres (`sysname`). Se ultrapassar, abreviar a parte das colunas e documentar. |
| G6 **(NEW)** | Always reference objects with the schema (`language.Language`, never just `Language`). | Sempre referenciar os objetos com o schema (`language.Language`, nunca apenas `Language`). |
| G7 **(NEW)** | **EF Core migrations** must follow this standard: set constraint and index names explicitly (`HasName`, `HasDatabaseName`, `HasConstraintName`) and use `DeleteBehavior.NoAction`/`Restrict`. EF defaults generate names such as `IX_Table_Column`, unnamed defaults and `ON DELETE CASCADE`. | **Migrations do EF Core** devem seguir este padrão: definir explicitamente os nomes de constraints e índices (`HasName`, `HasDatabaseName`, `HasConstraintName`) e usar `DeleteBehavior.NoAction`/`Restrict`. Os padrões do EF geram nomes como `IX_Tabela_Coluna`, defaults sem nome e `ON DELETE CASCADE`. |
| G8 **(NEW)** | Validation scripts ignore the `dbo` schema (legacy) and the EF table `__EFMigrationHistory`. | Os scripts de validação ignoram o schema `dbo` (legado) e a tabela do EF `__EFMigrationHistory`. |

# 4. Naming rules for all objects / Regras de nomenclatura para todos os objetos

These rules apply to schemas, tables, columns, views, procedures, functions and constraints.
Estas regras valem para schemas, tabelas, colunas, views, procedures, funções e constraints.

| # | English | Português | Example / Exemplo |
|---|---|---|---|
| N1 | Do not use white space in the middle of the name. | Não utilizar espaço em branco no meio do nome. | `CreateDate`, not `Create Date` |
| N2 | Do not use characters with accents. | Não utilizar caracteres com acentos. | `Description`, not `Descrição` |
| N3 | Do not use special characters (`-`, `.`, `#`, `@`, `$`, `%`, `&`...). | Não utilizar caracteres especiais (`-`, `.`, `#`, `@`, `$`, `%`, `&`...). | `CostCenter`, not `Cost-Center` |
| N4 | Do not use underscore (`_`). **Exception:** the separator in constraint and index names (`PK_`, `FK_`, `IX_`, `IXU_`, `DF_`, `CK_`). | Não utilizar subscrito (`_`). **Exceção:** o separador nos nomes de constraints e índices (`PK_`, `FK_`, `IX_`, `IXU_`, `DF_`, `CK_`). | `ActionPlan`, not `Action_Plan` |
| N5 | Do not use SQL Server reserved words. If the word is needed, use a compound word. | Não utilizar palavras reservadas do SQL Server. Se precisar utilizar a palavra, utilizar palavra composta. | `SystemUser`, `PurchaseOrder`, `AccessKey`, not `User`, `Order`, `Key` |
| N6 | Do not use numbers (0-9), except when the number is part of an acronym. **Exception:** the sequence number of indexes (`IX_..._01`). | Não usar números (0-9), exceto quando o número faz parte de uma sigla. **Exceção:** o sequencial dos índices (`IX_..._01`). | `KeyS3`, `RootCauseAnalysis5W2HId` (acronyms / siglas) |
| N7 | When a number is needed, write it in words. | Quando precisar de número, escrever por extenso. | `ApproverOne`, `ApproverTwo`, not `Approver1`, `Approver2` |
| N8 | Use words in the singular. | Utilizar palavras no singular. | `Language`, `ActionPlan`, not `Languages`, `ActionPlans` |
| N9 | Use succinct and objective names. | Utilizar nomes sucintos e objetivos. | `DueDate`, not `DateWhenTheActionMustBeFinished` |
| N10 | Use the English language. | Utilizar o idioma inglês. | `Area`, `Department`, not `Setor`, `Departamento` |
| N11 | Acronyms always in uppercase. **Exceptions:** schema names (section 5) and `Id`, which is written `Id`. | Sigla sempre em maiúscula. **Exceções:** nomes de schema (seção 5) e `Id`, que é escrito `Id`. | `RCAType`, `ISOCode`, `KeyS3`, `LanguageId` |
| N12 | Do not use `Id` alone, without meaning: the column name says which entity it identifies. | Não utilizar `Id` sozinho, sem significado: o nome da coluna diz qual entidade ela identifica. | `LanguageId`, `ActionPlanId`, not `Id` |
| N13 | Do not use grammatical prepositions (list below). | Não usar preposição gramatical (lista abaixo). | `CreateDate`, not `DateOfCreation`; `DeleteUserId`, not `DeletedByUserId` |
| N14 | Do not use `SP` / `sp_` to identify procedures: this prefix is used by SQL Server system procedures. Use `pr` (see [Procedure (Procedure)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/265)). | Não usar `SP` / `sp_` para identificar procedures: esse prefixo é usado pelas procedures de sistema do SQL Server. Usar `pr` (ver [Procedure (Procedure)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/265)). | `language.prStandardLanguage`, not `spStandardLanguage` |

**Prepositions not allowed / Preposições não permitidas**

| English | Português |
|---|---|
| at | em, a |
| by | por, perto de |
| for | para, por |
| with | com |
| from | de, desde |
| in | em, dentro de |
| into | em, para dentro de |
| of | de, em |
| on | em, sobre |
| to | para, a |

## Coding and data type rules / Regras de codificação e tipos de dados

| # | English | Português | Example / Exemplo |
|---|---|---|---|
| C1 | In queries, do not use `SELECT * FROM ...`: list only the columns that will be used. | Em consultas, não utilizar `SELECT * FROM ...`: listar somente as colunas que serão utilizadas. | `SELECT LanguageId, Name FROM language.Language;` |
| C2 | Do not use the currency types `MONEY` / `SMALLMONEY` for columns. Use `DECIMAL(p, s)` with the precision the business needs. | Não utilizar os tipos monetários `MONEY` / `SMALLMONEY` em colunas. Utilizar `DECIMAL(p, s)` com a precisão que o negócio exige. | `Amount DECIMAL(19, 4)` |

## Validation script / Script de validação

```sql
-- Names and columns outside rules N1-N6, N12, N13 and C2 (non-dbo schemas)
-- Nomes e colunas fora das regras N1-N6, N12, N13 e C2 (schemas diferentes de dbo)
-- "Number" and "Preposition" findings need manual review / Os achados "Number" e "Preposition" precisam de revisão manual
WITH n AS
(
    SELECT SCHEMA_NAME(o.schema_id) AS SchemaName, o.name AS ObjectName, o.type_desc AS ObjectType,
           CAST(NULL AS sysname) AS ColumnName, CAST(NULL AS sysname) AS DataType, o.name AS CheckedName
      FROM sys.objects AS o
     WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF')
    UNION ALL
    SELECT SCHEMA_NAME(t.schema_id), t.name, 'COLUMN', c.name, TYPE_NAME(c.user_type_id), c.name
      FROM sys.columns AS c
      JOIN sys.tables  AS t ON t.object_id = c.object_id
)
SELECT n.SchemaName, n.ObjectName, n.ObjectType, n.ColumnName, n.DataType, f.Finding
  FROM n
 CROSS APPLY (VALUES
       (IIF(n.CheckedName = 'Id', 'N12 - Meaningless Id / Id sem significado', NULL)),
       (IIF(n.DataType IN ('money', 'smallmoney'), 'C2 - Money type / Tipo monetário', NULL)),
       (IIF(n.CheckedName LIKE '%[_]%', 'N4 - Underscore / Subscrito', NULL)),
       (IIF(n.CheckedName LIKE '%[^A-Za-z0-9_]%' COLLATE Latin1_General_BIN, 'N1-N3 - Space, accent or special character / Espaço, acento ou caractere especial', NULL)),
       (IIF(n.CheckedName LIKE '%[0-9]%', 'N6 - Number: check if part of an acronym / Número: verificar se faz parte de sigla', NULL)),
       (IIF(EXISTS (SELECT 1
                      FROM (VALUES ('At'), ('By'), ('For'), ('With'), ('From'), ('In'), ('Into'), ('Of'), ('On'), ('To')) AS p(w)
                     WHERE n.CheckedName COLLATE Latin1_General_BIN LIKE '%[a-z]' + p.w + '[A-Z]%'
                        OR n.CheckedName COLLATE Latin1_General_BIN LIKE '%[a-z]' + p.w),
            'N13 - Preposition: review / Preposição: revisar', NULL))
       ) AS f(Finding)
 WHERE f.Finding IS NOT NULL
   AND n.SchemaName <> 'dbo'
   AND n.ObjectName <> '__EFMigrationHistory'
 ORDER BY n.SchemaName, n.ObjectName, n.ColumnName;
```

# 5. Schema naming / Nomenclatura de schema

**Pattern / Padrão:** `camelCase`  **Examples / Exemplos:** `language`, `alert`, `actionPlan`, `behaviorObservation`

| # | English | Português |
|---|---|---|
| S1 | **Single word:** always all lowercase. Ex.: `language`, `alert`, `rca`. | **Nome único:** sempre todo em minúsculo. Ex.: `language`, `alert`, `rca`. |
| S2 | **Compound name:** the first word all lowercase; from the second word on, use PascalCase (each word starts with an uppercase letter). Ex.: `actionPlan`, `behaviorObservation`, `customField`. | **Nome composto:** a primeira palavra toda em minúsculo; a partir da segunda palavra, usar PascalCase (cada palavra começa com letra maiúscula). Ex.: `actionPlan`, `behaviorObservation`, `customField`. |
| S3 | No spaces, underscores, hyphens, accents or special characters. | Sem espaços, subscrito, hífen, acentos ou caracteres especiais. |
| S4 | One schema per module, named after the module in English. | Um schema por módulo, com o nome do módulo em inglês. |
| S5 | Legacy objects not yet migrated stay in `dbo`. Do not create new objects in `dbo`. | Objetos legados não migrados permanecem no `dbo`. Não criar objetos novos no `dbo`. |

| Correct / Correto | Incorrect / Incorreto | Reason / Motivo |
|---|---|---|
| `language` | `Language`, `LANGUAGE` | Single word must be lowercase / Nome único deve ser minúsculo |
| `actionPlan` | `ActionPlan`, `actionplan`, `action_plan`, `action-plan` | Compound: first word lowercase, then PascalCase, no separators / Composto: primeira palavra minúscula, depois PascalCase, sem separadores |

```sql
-- Create a schema / Criar um schema
CREATE SCHEMA actionPlan AUTHORIZATION dbo;

-- Validation: schemas outside the standard (S1–S3) / Validação: schemas fora do padrão (S1–S3)
SELECT s.name AS SchemaName
  FROM sys.schemas AS s
 WHERE s.schema_id BETWEEN 5 AND 16383          -- user schemas only / somente schemas de usuário
   AND (   s.name NOT LIKE '[a-z]%'        COLLATE Latin1_General_BIN   -- must start lowercase / deve iniciar com minúscula
        OR s.name LIKE '%[^A-Za-z0-9]%'    COLLATE Latin1_General_BIN)  -- no special chars / sem caracteres especiais
 ORDER BY s.name;
```

# 6. Prefix summary / Resumo dos prefixos

| Object / Objeto | Prefix / Prefixo | Pattern / Padrão | Example / Exemplo | Page / Página |
|---|---|---|---|---|
| Schema | – | `camelCase` | `language`, `actionPlan` | Section 5 / Seção 5 |
| Table / Tabela | – | `[schema].[Table]` | `language.StandardLanguage` | [Table (Tabela)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/267) |
| View | `vw` | `[schema].vw[Name]` | `language.vwStandardLanguage` | [View (View)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/264) |
| Stored procedure | `pr` | `[schema].pr[Name]` | `language.prStandardLanguage` | [Procedure (Procedure)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/265) |
| Primary key | `PK` | `PK_[schema]_[Table]` | `PK_language_Language` | [Primary Key (Chave Primária)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/261) |
| Foreign key | `FK` | `FK_[schema]_[Table]_[Column]` | `FK_language_StandardLanguage_LanguageId` | [Foreign Key (Chave Estrangeira)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/258) |
| Index / Índice | `IX` | `IX_[schema]_[Table]_[NN]` | `IX_alert_Alert_01` | [Index (Índice)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/263) |
| Unique index / Índice único **(NEW)** | `IXU` | `IXU_[schema]_[Table]_[NN]` | `IXU_language_Language_01` | [Unique Index (Índice Único)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/1215) |
| Default constraint **(NEW)** | `DF` | `DF_[schema]_[Table]_[Column]` | `DF_language_Language_IsActive` | [Default Constraint (Default)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/1216) |
| Check constraint **(NEW)** | `CK` | `CK_[schema]_[Table]_[Column]` | `CK_language_Language_Status` | [Check Constraint (Check)](https://dev.azure.com/solvacelabs/Solvace%20Product%20Improvement/_wiki/wikis/Solvace-Product-Improvement.wiki/1217) |

# 7. Key design rules / Principais regras de modelagem

| # | English | Português |
|---|---|---|
| R1 **(NEW)** | Every table must have a primary key. | Toda tabela deve ter chave primária. |
| R2 **(NEW)** | The PK is **CLUSTERED** by default. `NONCLUSTERED` only as a documented exception, and the table must then have another clustered index (no heaps). | A PK é **CLUSTERED** por padrão. `NONCLUSTERED` somente como exceção documentada, e a tabela precisa ter outro índice clusterizado (sem heap). |
| R3 **(NEW)** | Never create an extra index on the same columns as the PK: the PK already creates its own index. | Nunca criar um índice extra nas mesmas colunas da PK: a PK já cria o próprio índice. |
| R4 **(NEW)** | Every FK must have a supporting index (FK columns as the leading key columns), unless an existing index already covers it. | Toda FK deve ter um índice de suporte (colunas da FK como colunas iniciais da chave), exceto se um índice existente já cobrir. |
| R5 | Do not use `ON DELETE CASCADE` on foreign keys (**(NEW)** also `ON UPDATE CASCADE`). | Não utilizar `ON DELETE CASCADE` nas chaves estrangeiras (**(NOVO)** nem `ON UPDATE CASCADE`). |
| R6 **(NEW)** | FK and CK constraints must be enabled and trusted (`WITH CHECK`). | Constraints FK e CK devem estar habilitadas e confiáveis (`WITH CHECK`). |
| R7 **(NEW)** | No duplicate or redundant indexes. | Sem índices duplicados ou redundantes. |

# 8. Code review checklist / Checklist de code review **(NEW)**

- [ ] Schema of the module: single word lowercase, compound in camelCase / Schema do módulo: nome único em minúsculo, composto em camelCase
- [ ] Table, view and procedure names in PascalCase with the right prefix / Nomes de tabela, view e procedure em PascalCase com o prefixo correto
- [ ] Names follow N1–N14: English, singular, no spaces, accents, special characters, underscore, numbers, prepositions or reserved words / Nomes seguem N1–N14: inglês, singular, sem espaços, acentos, caracteres especiais, subscrito, números, preposições ou palavras reservadas
- [ ] No `SELECT *` and no `MONEY` / `SMALLMONEY` columns / Sem `SELECT *` e sem colunas `MONEY` / `SMALLMONEY`
- [ ] PK named `PK_[schema]_[Table]` and CLUSTERED (or exception documented) / PK nomeada `PK_[schema]_[Tabela]` e CLUSTERED (ou exceção documentada)
- [ ] Every FK named, indexed, without CASCADE / Toda FK nomeada, indexada e sem CASCADE
- [ ] Every DF, CK, IX and IXU named explicitly / Todo DF, CK, IX e IXU nomeado explicitamente
- [ ] No duplicate index / Nenhum índice duplicado
- [ ] Validation scripts of each page executed with no findings / Scripts de validação de cada página executados sem pendências

# 9. Change log / Histórico de alterações

| Version / Versão | Date / Data | Author / Autor | Change / Alteração |
|---|---|---|---|
| 1.0 | 2024-06-12 | Claudio Arruda | Initial version (PT) / Versão inicial (PT) |
| 1.1 | – | Claudio Arruda | FK: do not use ON DELETE CASCADE / FK: não utilizar ON DELETE CASCADE |
| 2.0 | 2026-11-16 | Claudio Arruda | Bilingual EN/PT; images replaced by text; scripts fixed; PK name aligned with current practice (`PK_[schema]_[Table]`, no column); composite FK with SiteId named by the main column; new rules for PK clustered, FK index, IXU, DF, CK and EF Core; validation scripts / Bilíngue EN/PT; imagens substituídas por texto; scripts corrigidos; nome da PK alinhado à prática atual (`PK_[schema]_[Tabela]`, sem coluna); FK composta com SiteId nomeada pela coluna principal; novas regras de PK clustered, índice de FK, IXU, DF, CK e EF Core; scripts de validação |
| 2.1 | 2026-11-16 | Claudio Arruda | Schema naming rules (section 5) / Regras de nomenclatura de schema (seção 5) |
| 2.2 | 2026-11-16 | Claudio Arruda | Naming rules for all objects N1–N14 and coding rules C1–C2 (section 4) / Regras de nomenclatura para todos os objetos N1–N14 e de codificação C1–C2 (seção 4) |
