# Preview da Migration - Módulo Vacations

## 📋 Resumo das Alterações

Esta migration criará **2 novas tabelas** no banco de dados MySQL para o módulo de gerenciamento de férias.

**Migration ID:** `20260420205921_InitialVacationModule`

---

## 🗄️ Tabelas que Serão Criadas

### 1. **UserVacationBalances** - Saldo de Férias dos Usuários

Armazena o saldo de dias de férias disponíveis para cada usuário por ano.

```sql
CREATE TABLE `UserVacationBalances` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `AvailableDays` int NOT NULL,
    `UsedDays` int NOT NULL,
    `Year` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `CreatedBy` longtext CHARACTER SET utf8mb4 NULL,
    `UpdatedBy` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_UserVacationBalances` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;
```

**Campos:**
- `Id` - Identificador único (auto-increment)
- `UserId` - GUID do usuário (vem do backend de autenticação)
- `AvailableDays` - Total de dias disponíveis no ano
- `UsedDays` - Dias já utilizados
- `Year` - Ano de referência
- `CreatedAt` - Data de criação
- `UpdatedAt` - Data da última atualização
- `CreatedBy` - Quem criou o registro
- `UpdatedBy` - Quem atualizou o registro

**Índice:**
- `IX_UserVacationBalances_UserId_Year` - Índice único para garantir um saldo por usuário/ano

---

### 2. **VacationRequests** - Solicitações de Férias

Armazena todas as solicitações de férias dos usuários com seus respectivos status e aprovações.

```sql
CREATE TABLE `VacationRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `StartDate` datetime(6) NOT NULL,
    `EndDate` datetime(6) NOT NULL,
    `BusinessDays` int NOT NULL,
    `Status` int NOT NULL,
    `ManagerNotes` varchar(1000) CHARACTER SET utf8mb4 NULL,
    `HRNotes` varchar(1000) CHARACTER SET utf8mb4 NULL,
    `ApprovedByManagerId` char(36) COLLATE ascii_general_ci NULL,
    `ApprovedByManagerAt` datetime(6) NULL,
    `AuthorizedByHRId` char(36) COLLATE ascii_general_ci NULL,
    `AuthorizedByHRAt` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `CreatedBy` longtext CHARACTER SET utf8mb4 NULL,
    `UpdatedBy` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_VacationRequests` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;
```

**Campos:**
- `Id` - Identificador único (auto-increment)
- `UserId` - GUID do usuário solicitante
- `StartDate` - Data de início das férias
- `EndDate` - Data de fim das férias
- `BusinessDays` - Quantidade de dias úteis
- `Status` - Status da solicitação (1=Pendente, 2=Aprovado, 3=Autorizado, 4=Concluído, 5=Cancelado)
- `ManagerNotes` - Observações do gestor (até 1000 caracteres)
- `HRNotes` - Observações do RH (até 1000 caracteres)
- `ApprovedByManagerId` - GUID do gestor que aprovou
- `ApprovedByManagerAt` - Data/hora da aprovação
- `AuthorizedByHRId` - GUID do responsável RH que autorizou
- `AuthorizedByHRAt` - Data/hora da autorização
- `CreatedAt` - Data de criação da solicitação
- `UpdatedAt` - Data da última atualização
- `CreatedBy` - Quem criou a solicitação
- `UpdatedBy` - Quem atualizou a solicitação

**Índices:**
- `IX_VacationRequests_UserId` - Otimiza consultas por usuário
- `IX_VacationRequests_StartDate_EndDate` - Otimiza consultas por período de datas

---

## 🔍 Índices Criados

### 1. Índice Único: UserVacationBalances
```sql
CREATE UNIQUE INDEX `IX_UserVacationBalances_UserId_Year`
ON `UserVacationBalances` (`UserId`, `Year`);
```
**Propósito:** Garante que cada usuário tenha apenas um saldo por ano.

### 2. Índice Composto: VacationRequests (Datas)
```sql
CREATE INDEX `IX_VacationRequests_StartDate_EndDate`
ON `VacationRequests` (`StartDate`, `EndDate`);
```
**Propósito:** Otimiza consultas de calendário e busca por período.

### 3. Índice Simples: VacationRequests (Usuário)
```sql
CREATE INDEX `IX_VacationRequests_UserId`
ON `VacationRequests` (`UserId`);
```
**Propósito:** Otimiza consultas das solicitações de um usuário específico.

---

## 📊 Tabela de Controle de Migrations

O Entity Framework criará/atualizará a tabela `__EFMigrationsHistory`:

```sql
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;
```

Será inserido o registro:
```sql
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260420205921_InitialVacationModule', '8.0.21');
```

---

## ⚙️ Características Técnicas

### Script Idempotente
O script gerado é **idempotente**, ou seja, pode ser executado múltiplas vezes sem causar erros:
- Verifica se a migration já foi aplicada antes de executar
- Usa `IF NOT EXISTS` para todas as operações
- Envolvido em uma transação para garantir consistência

### Charset e Collation
- Charset padrão: `utf8mb4` (suporta caracteres especiais e emojis)
- GUIDs: `ascii_general_ci` (otimizado para GUIDs)

### Tipos de Dados
- **int**: Números inteiros (IDs, dias, status)
- **char(36)**: GUIDs no formato padrão
- **datetime(6)**: Data/hora com precisão de microssegundos
- **varchar(1000)**: Strings com tamanho limitado (notas)
- **longtext**: Strings sem limite definido (auditoria)

---

## 🚀 Como Aplicar a Migration

### Opção 1: Automático ao Iniciar a Aplicação (Recomendado)
As migrations serão aplicadas automaticamente quando você iniciar o projeto:

```bash
cd CIME/modules/Solvace.PullRequests/src/solvace.prform.api
dotnet run
```

**Saída esperada:**
```
Migrations do DefaultContext executadas com sucesso.
Migrations do VacationContext executadas com sucesso.
```

### Opção 2: Manual via EF Core Tools
```bash
cd CIME/modules/Solvace.Vacations/src/solvace.vacations.infra
dotnet ef database update --context VacationContext --startup-project ../../../Solvace.PullRequests/src/solvace.prform.api
```

### Opção 3: Executar o Script SQL Manualmente
```bash
mysql -u seu_usuario -p seu_banco_de_dados < VacationMigration.sql
```

---

## ✅ Verificações Após Aplicar

Execute estas queries para verificar se tudo foi criado corretamente:

```sql
-- Verificar se as tabelas foram criadas
SHOW TABLES LIKE 'VacationRequests';
SHOW TABLES LIKE 'UserVacationBalances';

-- Ver estrutura das tabelas
DESCRIBE VacationRequests;
DESCRIBE UserVacationBalances;

-- Ver índices
SHOW INDEX FROM VacationRequests;
SHOW INDEX FROM UserVacationBalances;

-- Verificar registro da migration
SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260420205921_InitialVacationModule';
```

---

## 🔄 Rollback (Se Necessário)

Se precisar reverter a migration, execute:

```bash
cd CIME/modules/Solvace.Vacations/src/solvace.vacations.infra
dotnet ef migrations remove --context VacationContext --startup-project ../../../Solvace.PullRequests/src/solvace.prform.api
```

Ou manualmente via SQL:
```sql
START TRANSACTION;

DROP INDEX IX_VacationRequests_UserId ON VacationRequests;
DROP INDEX IX_VacationRequests_StartDate_EndDate ON VacationRequests;
DROP INDEX IX_UserVacationBalances_UserId_Year ON UserVacationBalances;

DROP TABLE VacationRequests;
DROP TABLE UserVacationBalances;

DELETE FROM __EFMigrationsHistory WHERE MigrationId = '20260420205921_InitialVacationModule';

COMMIT;
```

---

## 📝 Impacto no Banco de Dados

### Espaço Estimado
- **UserVacationBalances**: ~200 bytes por registro
- **VacationRequests**: ~300 bytes por registro
- **Índices**: ~150 bytes por entrada nos índices

**Exemplo para 100 usuários com média de 3 solicitações por ano:**
- UserVacationBalances: 100 registros × 200 bytes = ~20 KB
- VacationRequests: 300 registros × 300 bytes = ~90 KB
- Total estimado: **~110 KB** (muito leve)

### Performance
- Todas as consultas principais estão otimizadas com índices
- Índice único previne duplicação de saldos
- Índices compostos aceleram consultas de calendário

---

## 📌 Notas Importantes

1. **Compatibilidade**: MySQL 5.7+ ou MariaDB 10.2+
2. **Transaction Safe**: Todo o script roda dentro de uma transação
3. **Idempotente**: Pode ser executado múltiplas vezes com segurança
4. **Charset**: utf8mb4 permite emojis e caracteres especiais
5. **Timezone**: As datas são armazenadas com precisão de microssegundos

---

## 🎯 Próximos Passos

Após aplicar esta migration:

1. ✅ Tabelas criadas e prontas para uso
2. ✅ Endpoints da API disponíveis em `/api/v1/Vacations`
3. ✅ Swagger documentando todos os endpoints
4. ⏳ Criar saldos de férias para os usuários (via endpoint admin)
5. ⏳ Testar fluxo completo de solicitação de férias
6. ⏳ Desenvolver frontend conforme documentação

**Arquivo SQL completo:** `VacationMigration.sql`

---

**Desenvolvido para o Sistema Solvace - Módulo de Férias**
