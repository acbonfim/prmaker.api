# Solvace.Vacations - Módulo de Gerenciamento de Férias

## Estrutura do Módulo

```
Solvace.Vacations/
├── src/
│   ├── solvace.vacations.domain/          # Camada de domínio
│   │   ├── Entities/                       # Entidades do domínio
│   │   │   ├── VacationRequest.cs         # Solicitação de férias
│   │   │   └── UserVacationBalance.cs     # Saldo de férias do usuário
│   │   ├── Enums/
│   │   │   └── VacationStatus.cs          # Status das solicitações
│   │   ├── Requests/                       # DTOs de entrada
│   │   └── Responses/                      # DTOs de saída
│   │
│   ├── solvace.vacations.application/      # Camada de aplicação
│   │   ├── Contracts/                      # Interfaces
│   │   │   ├── IVacationApplication.cs
│   │   │   ├── IVacationRepository.cs
│   │   │   └── IUserVacationBalanceRepository.cs
│   │   └── VacationApplication.cs          # Lógica de negócio
│   │
│   └── solvace.vacations.infra/            # Camada de infraestrutura
│       ├── Contexts/
│       │   └── VacationContext.cs          # DbContext do EF Core
│       ├── Repositories/                    # Implementação dos repositórios
│       └── Extensions/
│           └── VacationModuleExtensions.cs # Configuração DI
│
└── VACATIONS_API_DOCUMENTATION.md          # Documentação completa da API
```

## Controller

A controller foi adicionada no módulo **Solvace.PullRequests** para facilitar o deploy:
- **Localização:** `CIME/modules/Solvace.PullRequests/src/solvace.prform.api/Controllers/VacationsController.cs`
- **Rota Base:** `/api/v1/Vacations`

## Funcionalidades

### 1. Solicitações de Férias
- ✅ Criar solicitação de férias
- ✅ Atualizar solicitação (apenas se pendente)
- ✅ Visualizar solicitações próprias
- ✅ Visualizar todas as solicitações
- ✅ Aprovar solicitação (gestor)
- ✅ Autorizar solicitação (RH/gestor)
- ✅ Excluir solicitação (com regras por role)

### 2. Calendário
- ✅ Visualizar calendário mensal
- ✅ Ver datas ocupadas
- ✅ Identificar quem está de férias em cada data

### 3. Saldo de Férias
- ✅ Criar saldo anual para usuários (admin)
- ✅ Consultar saldo próprio
- ✅ Consultar saldo de outros usuários (gestor)
- ✅ Controle automático de uso (dedução ao autorizar)

## Fluxo de Status

```
1. PendingApproval (Usuário cria)
   ↓
2. ApprovedByManager (Gestor aprova)
   ↓
3. AuthorizedByHR (Gestor/RH autoriza + deduz saldo)
   ↓
4. Completed (Automático quando startDate >= hoje)
```

## Permissões por Role

| Role    | Permissões |
|---------|-----------|
| **user**    | Criar, editar (se pendente), visualizar próprias solicitações, ver calendário |
| **gestor**  | Todas as permissões de user + aprovar, autorizar, excluir solicitações |
| **admin**   | Todas as permissões + gerenciar saldos de férias |

## Regras de Negócio Implementadas

1. **Criação de Solicitação:**
   - Valida saldo disponível
   - Impede conflito de datas do mesmo usuário
   - Data início não pode ser no passado

2. **Edição:**
   - Apenas o próprio usuário pode editar
   - Somente se status = PendingApproval

3. **Aprovação:**
   - Apenas gestores podem aprovar
   - Somente solicitações pendentes

4. **Autorização:**
   - Apenas gestores podem autorizar
   - Somente solicitações aprovadas
   - Deduz automaticamente do saldo

5. **Exclusão:**
   - Usuário: apenas próprias e pendentes
   - Gestor: qualquer não concluída/cancelada
   - Devolve saldo se já foi autorizada

## Configuração

### 1. Adicionar ao projeto

A referência já foi adicionada em `solvace.prform.api.csproj`:
```xml
<ProjectReference Include="..\..\..\Solvace.Vacations\src\solvace.vacations.infra\solvace.vacations.infra.csproj"/>
```

### 2. Registrar no Program.cs

Já configurado em `Program.cs`:
```csharp
using solvace.vacations.infra.Extensions;

builder.Services
    .AddVacationModule(builder.Configuration);
```

### 3. Executar Migrations

As migrations serão executadas automaticamente ao iniciar a aplicação:
```csharp
var vacationContext = scope.ServiceProvider.GetRequiredService<VacationContext>();
vacationContext.Database.Migrate();
```

### 4. Criar Migration Manual (se necessário)

```bash
cd CIME/modules/Solvace.Vacations/src/solvace.vacations.infra
dotnet ef migrations add InitialCreate --context VacationContext --startup-project ../../Solvace.PullRequests/src/solvace.prform.api
```

## Banco de Dados

Utiliza o mesmo banco de dados configurado na `DefaultConnection` (MySQL).

**Tabelas criadas:**
- `VacationRequests` - Solicitações de férias
- `UserVacationBalances` - Saldos de férias dos usuários

## Endpoints Principais

Ver documentação completa em: **[VACATIONS_API_DOCUMENTATION.md](./VACATIONS_API_DOCUMENTATION.md)**

**Resumo:**
- `POST /api/v1/Vacations/request` - Criar solicitação
- `GET /api/v1/Vacations/my-requests` - Minhas solicitações
- `GET /api/v1/Vacations/calendar?month=7&year=2024` - Calendário
- `GET /api/v1/Vacations/balance?year=2024` - Meu saldo
- `POST /api/v1/Vacations/request/{id}/approve` - Aprovar (gestor)
- `POST /api/v1/Vacations/request/{id}/authorize` - Autorizar (RH)

## Autenticação

Utiliza o mesmo sistema de autenticação JWT do módulo PullRequests:
- **Header:** `Authorization: Bearer <token>`
- **Claims:**
  - `ExternalId`: GUID do usuário (claim customizada)
  - `ClaimTypes.Role`: Roles (user, gestor, admin)

## Migrations

### Preview da Migration
Antes de aplicar as migrations, você pode visualizar o que será alterado no banco de dados:

📄 **[MIGRATION_PREVIEW.md](./MIGRATION_PREVIEW.md)** - Documentação completa das alterações
📄 **[VacationMigration.sql](./VacationMigration.sql)** - Script SQL completo

### Aplicar Migrations

**Opção 1: Automático (Recomendado)**
As migrations serão aplicadas automaticamente ao iniciar a aplicação:
```bash
cd CIME/modules/Solvace.PullRequests/src/solvace.prform.api
dotnet run
```

**Opção 2: Manual via EF Core**
```bash
cd CIME/modules/Solvace.Vacations/src/solvace.vacations.infra
dotnet ef database update --context VacationContext --startup-project ../../../Solvace.PullRequests/src/solvace.prform.api
```

**Opção 3: SQL Manual**
```bash
mysql -u seu_usuario -p seu_banco_de_dados < VacationMigration.sql
```

### Verificar Migrations Aplicadas
```sql
SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260420205921_InitialVacationModule';
```

## Próximos Passos

1. ✅ Estrutura do módulo criada
2. ✅ Entidades e lógica de negócio implementadas
3. ✅ Controller e endpoints configurados
4. ✅ Documentação completa gerada
5. ✅ Migrations criadas e documentadas
6. ⏳ Executar migrations no banco de dados (veja seção acima)
7. ⏳ Testar endpoints via Swagger/Postman
8. ⏳ Desenvolver frontend baseado na documentação

## Suporte

Para dúvidas sobre implementação do frontend, consulte o arquivo **VACATIONS_API_DOCUMENTATION.md** que contém:
- Descrição detalhada de cada endpoint
- Exemplos de request/response
- Cenários de uso para cada tela
- Checklist de implementação
