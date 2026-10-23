# Usuários — levantamento funcional (amostra do espelho para os testes)

## Itens

### UC-053 — Administrador transfere um usuário para outra planta/área (ou altera a área)
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UserAreaController.cs:35-52`
- **Módulos:** revamp-users, legado-usuarios
- **Ator:** administrador Solvace ou de módulo. **Pré:** usuário existe na planta atual do logado (`LastSiteId`).
- **Fluxo:** `PUT /UserArea/transfer` com `UserId`, `SiteId`, `AreaId`, `ApplicationId` (logado e `LastSiteId` das claims) → RN-266, RN-267 → NTF-012. **Erro:** "O usuário informado não é um administrador" (`UserId`); "SiteId não encontrado" (`SiteId`); "Usuário não encontrado" (`UserId`). **Pós:** resposta `true`/`false` do passo 5 de RN-267. Alterar só a área: `PUT /UserArea` com `UserId`, `SiteId`, `AreaId` → `true`; erro RN-268.

### UC-065 — Administrador transfere usuário para outra unidade
- **Onde:** `...register-user-form.component.ts:267-285`, `src/app/settings/register-users/components/transfer-user/transfer-user.component.ts:87-114`
- **Ator:** administrador do módulo Users.
- **Pré-condições:** usuário ativo e não pendente (RN-293).
- **Fluxo principal:** **Transferir** abre diálogo (463px, "Transferir usuário"); **Unidade** e **Área** (RN-294); **Confirmar**; confirmação (título "Confirmação"); `PUT /UserArea/transfer`; diálogo fecha com `true`, grade recarrega, form limpo. Erro → `HttpErrorService.showErrorInToast`. Mensagens: NTF-013. Efeitos em outros módulos: GAP-066.

### UC-078 — Administrador transfere usuário de planta/área
- **Onde:** `.../TransferUserCommandHandler.cs:23`
- **Módulos:** revamp-users
- **Tags:** transferir usuário, transfer user
- Fluxo: RN-346 a RN-349. Pós: guest `IsUserTransfer = true` na origem (EST-015).

### RN-266 — Transferência: só administrador, planta atual e usuário existente
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/TransferUser/TransferUserCommandHandler.cs:25-39`
- **Módulos:** revamp-users
- **Regra:** logado sem `IsSolvaceAdmin` e sem `IsModuleAdmin` → chave `UserId`, "O usuário informado não é um administrador" (só no back). `LastSiteId` (claim) = 0 → chave `SiteId`, "SiteId não encontrado". `GetUserSessionInfoById(UserId, LastSiteId)` nulo → chave `UserId`, "Usuário não encontrado"; `OriginSiteId` e área de origem vêm desse retorno.

### RN-267 — Efeitos da transferência, em ordem
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/TransferUser/TransferUserCommandHandler.cs:41-76`
- **Módulos:** revamp-users, ext:sns, ext:redis
- **Regra:** 1) `RemoveRolesByUserId(UserId, logado, ApplicationId)`; 2) `AddUserTransfer` com `FromAreaId` = área atual, `ToAreaId` = `AreaId` pedido, `FromSiteId` = `OriginSiteId`, `ToSiteId` = `SiteId` pedido, `FromUserId` = `UserId`, `TransferDate` = `DateTime.UtcNow`, `TransferedBy` = Guid do logado; 3) `DeleteUserGuest(UserId, SiteId destino)`; 4) `AddNewUserGuest` com `SiteId = OriginSiteId`, `IsUserTransfer = true`, `IsActived = true` (vira convidado na origem); 5) `UpdateUserSiteAndAreaCommandRequest(UserId, SiteId, AreaId)`; 6) evento `TypeEventPublish.Upserted` (NTF-012); 7) `InvalidateUserSessionCacheAsync`. (GAP-058).

### RN-268 — Alterar área falha com mensagem fixa
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/UpdateUserArea/UpdateUserAreaCommandHandler.cs:17-22`
- **Regra:** `UpdateUserArea(UserId, SiteId, AreaId)` = `false` → `ArgumentException` "Falha ao atualizar área do usuário". Sem checagem de perfil nem cache (GAP-059).

#### Auditoria de transferência

### RN-293 — Transferir só para usuário ativo e não pendente
- **Onde:** `...register-user-form.component.ts:61-68,267-285`
- **Regra:** `canTransferUser = !!user?.isActive && !user?.isPending && !loadingForm`; `transferUser()` também sai se `!formUser.value.isActive` ou `user.isPending`. `availableTransferSites` = `sitesOptions` menos `user.siteId`; sem `siteId` a lista é vazia.

### RN-294 — Diálogo de transferência: Unidade → Área
- **Onde:** `src/app/settings/register-users/components/transfer-user/transfer-user.component.ts:52-114`, `...transfer-user.component.html:1-39`
- **Quando:** abrir o diálogo; escolher a unidade.
- **Regra:** `siteId` e `areaId` obrigatórios. **Área** nasce desabilitada; ao escolher a unidade `GET {masterdata}/UDA/area/site/{siteId}`, só `udaId > 0` (valor `udaId`, texto `udaName`). Unidades = `getAllActiveByEnvAuthanticated()` menos a atual (vazia → mantém a do formulário). Sem unidades: "Não há unidades disponíveis para transferência deste usuário." e **Confirmar** desabilitado (também com form inválido; `confirm()` exige `areaId`). Envia `PUT /UserArea/transfer` `{ userId, siteId, areaId, applicationId }`.
- **Módulos:** revamp-users, revamp-masterdata

#### Liberação de usuários SSO

### FN-086 — Transferir usuário de planta/área e alterar área
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UserAreaController.cs:35-52`
- **Módulos:** revamp-users, legado-usuarios
- **Tags:** transferir usuário, user transfer, alterar área, PUT UserArea/transfer
- Administrador move usuário para outra planta (`SiteId`) e área (`AreaId`) de uma aplicação (`ApplicationId`); `PUT /UserArea` só altera a área, sem os passos de transferência. UC-053, RN-266, RN-267, RN-268, NTF-012.

### FN-087 — Auditoria de transferências de usuário
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/AuditsTransferController.cs:26-93`
- **Módulos:** revamp-users, ext:qldb
- **Tags:** auditoria, audits transfer, histórico de transferência, QLDB
- Registra, altera, exclui, lista e consulta a auditoria (ledger QLDB). UC-054, RN-269, RN-270.

### FN-099 — Transferir usuário para outra unidade/área
- **Onde:** `...register-user-form.component.ts:267-285`, `src/app/settings/register-users/components/transfer-user/transfer-user.component.ts:52-114`
- **Módulos:** revamp-users, revamp-masterdata
- **Tags:** Transferir usuário, transfer user, unidade, área
- UC-065; RN-293, RN-294.

### FN-111 — Transferir usuário para outra planta/área
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/TransferUser/TransferUserCommandHandler.cs:23`
- **Tabelas:** a confirmar (histórico de transferência, convidado, área) — GAP-078
- **Módulos:** revamp-users, legado-usuarios
- **Tags:** transferir usuário, transfer user
- Só administrador; vira convidado na origem. UC-078; RN-346 a RN-349.

### FN-141 — Convidados e transferência de usuário entre unidades
- **Onde:** `Solvace.Users.Application.Abstractions/Commands/Interfaces/IWcmUserGuestCommands.cs:7-11`, `IWcmUserTransferGlobalCommands.cs:7`
- **Módulos:** revamp-users, revamp-administration
- **Tags:** convidado, guest
- RN-406, RN-407.

### NTF-012 — Evento "usuário atualizado" após transferência
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/TransferUser/TransferUserCommandHandler.cs:69-74`
- **Módulos:** revamp-users, ext:sns
- **Tags:** evento de usuário, user upserted event, SNS
- **Gatilho:** RN-267 passo 6. **Conteúdo:** `PublishEventUserCommandRequest { TypeEvent = TypeEventPublish.Upserted, UserId, EnvironmentUrl }`. **Canal:** tópico SNS do Users (RN-284). **Destinatários:** módulos assinantes (GAP-062).

### NTF-013 — Mensagens de retorno (toast e diálogos) do formulário de usuário
- **Onde:** `src/app/settings/register-users/components/register-user-form/register-user-form.component.ts:152-262`, `src/app/settings/register-users/components/transfer-user/transfer-user.component.ts:92-111`
- **Módulos:** revamp-users
- **Tags:** toast, mensagem, confirmação, feedback
- **Gatilho / canal:** toast (`SvcToastService`) e diálogo (`SvcDialogService.openConfirm`), só para quem operou; nenhum e-mail/push sai do front. Mensagens da liberação SSO: RN-298, RN-300 ("Liberação realizada com sucesso!").

| Ação | Sucesso | Erro |
|---|---|---|
| Adicionar/Alterar | "Alteração salva com sucesso" | "Usuário já cadastrado." (400 `UserNameAlreadyExists`, só cadastro) ou "Houve um erro durante a alteração." |
| Excluir | confirma "Você tem certeza que deseja excluir este usuário?" → "Usuário excluído!" | `#getDeleteErrorMessage` (`...register-user-form.component.ts:212-216,315-331`) lê `error.error.errors`: string → ela; array → 1º item string; objeto → 1ª chave (valor string ou 1º string do array); sem mensagem → "Ocorreu um erro ao excluir o usuário." |
| Rejeitar | confirma "Tem certeza que deseja rejeitar usuário?" → "Rejeitado com sucesso" | "Ocorreu um erro durante a solicitação. Por favor, tente novamente; se o erro persistir, entre em contato com o administrador do sistema." |
| Redefinir senha | confirma "Tem certeza que deseja redefinir senha do usuário?" → "Senha redefinida com sucesso" | "Ocorreu um erro ao tentar alterar a senha" |
| Transferir | confirma "Tem certeza que deseja realizar transferência?" → "Transferido com sucesso" | toast da mensagem do back |

### PRF-002 — Administrador Solvace (`IsSolvaceAdmin`)
- **Quem é:** administrador global do Solvace (flag no cadastro do usuário, lida por `IsSolvaceAdmin(userId)`).
- **O que pode:** transferir usuário de planta, rejeitar usuário, excluir usuário SSO e, no front, ver as ações administrativas do formulário de usuário.
- **Onde é checado:** back `Solvace.Users/src/Solvace.Users.Application/Commands/TransferUser/TransferUserCommandHandler.cs:25`, `.../UpdateRejectUser/UpdateRejectUserCommandHandler.cs:31`, `.../DeleteUserSso/DeleteUserSsoCommandHandler.cs:89`; front `register-user-form.component.ts:342` (`isSolvaceAdmin` libera tudo).
- **Como se concede:** cadastro do usuário (fora deste módulo — a confirmar a tela).

### PRF-003 — Administrador do módulo (`IsModuleAdmin`)
- **Quem é:** usuário com papel de administrador do módulo Users na planta.
- **O que pode:** o mesmo que PRF-002 nas três operações acima (transferir, rejeitar, excluir SSO), sempre na planta em que está logado (`LAST_SITE_ID` = planta do administrador).
- **Onde é checado:** `TransferUserCommandHandler.cs:26`, `UpdateRejectUserCommandHandler.cs:32`, `DeleteUserSsoCommandHandler.cs:90` (todos com `CanX = isSolvaceAdmin || isModuleAdmin`; falha → "O usuário informado não é um administrador" na chave `LoggedUserId`).
- **Como se concede:** papel por aplicação (PRF-004), tela Permissões de acesso (`settings/access-permission`, KC ART-60).

### GAP-066 — Back: regras, notificações, liberação SSO e exportação
- Fora do pacote: e-mails/notificações ao criar, rejeitar, liberar ou redefinir senha (front só mostra toast); bloqueios de exclusão, efeitos da transferência, persistência do status, propagação do update. Liberação SSO: controller só cobre entrada (`Solvace.Users/src/Solvace.Users.API/Controllers/UserSsoController.cs:39-155`); fora do pacote: `ReleaseUsersSsoCommand` (merge, criação, notificação), revalidação do vínculo, colunas XLSX/PDF.

### GAP-058 — Transferência sem transação visível
- Passos de RN-267 independentes (`TransferUserCommandHandler.cs:41-76`); falha no meio pode deixar papéis removidos e site/área sem atualizar.

### PRF-001 — Usuário autenticado (qualquer papel)
- **Quem é:** qualquer pessoa com token Bearer válido (claims `UserId`, `EnvironmentId`, `EnvironmentUrl`, `LastSiteId`).
- **O que pode:** chamar todos os endpoints dos controllers de `Solvace.Users.API` marcados com `[BearerAuthorize]` — buscar e listar usuários, ver cartões e organograma, consultar sessão, avatar, preferências, times, áreas, auditorias e exportar listas — sem checagem de papel no controller (ex.: `Solvace.Users/src/Solvace.Users.API/Controllers/UserController.cs:43-44`, `AuditsController.cs:12-14`, `GroupsController.cs:11-13`). O dado devolvido é filtrado pela planta atual do token (`LastSiteId`).
- **Onde é checado:** back, atributo `[BearerAuthorize]` por controller; no front, as rotas `settings/*` **não têm `canActivate`** (qualquer logado abre a tela; o back é quem decide) — GAP sobre isso nas áreas de visitantes.

### FN-003 — Consultar avatares (usuário, área, equipe) e nomes de áreas
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/AvatarController.cs:28-100`
- **Módulos:** revamp-users, ext:s3
- **Tags:** avatar, foto, iniciais
- Nome, cargo, cor das iniciais e URL assinada da foto (usuário) ou só nome (área/equipe): `POST Avatar/ListAvatar`, `GET Avatar`, `GET Avatar/User/{userId}`, `GET Avatar/Area/{areaId}`, `GET Avatar/Team/{teamId}`; `GET Area/names` lista as áreas do banco da planta (`AreaController.cs:22-29`). RN-008 a RN-010, RN-011, EST-002, UC-004.

### FN-009 — Criar usuário federado (SSO) como "solicitação de acesso"
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/CreateUserFederated/CreateUserFederatedCommandHandler.cs:16`, `.../Services/Create/CreateUserFederatedService.cs:21`
- **Módulos:** revamp-users, login, revamp-authentication, ext:cognito, ext:dynamodb
- **Tags:** usuário federado, SSO, solicitação de acesso
- Primeiro login SSO de desconhecido: cria cadastro pendente e notifica administradores. RN-041 a RN-044, RN-045; UC-008.

### FN-015 — Corrigir SiteId/LastSiteId de usuário sem área
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/FixUserSiteId/FixUserSiteIdCommandHandler.cs:25`
- **Módulos:** revamp-users
- **Tags:** corrigir site, fix site id, LastSiteId
- Ajusta o site se o usuário não tem área nele. RN-056; UC-014.

### FN-021 — Resolver avatar de uma área
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/AvatarController.cs:72-85`
- **Tabelas:** TB_EMP_EmployeeUnityDepartArea (banco global)
- **Módulos:** revamp-users
- **Tags:** Avatar/Area/{areaId}, avatar de área, GetAvatarArea
- Dado `areaId`, devolve `GetAvatarResponse` (id, nome, tipo "Area"). RN-074; UC-018.

### FN-027 — Listar usuários pendentes de SSO e sugestões de mesclagem
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetUsersPendingSsoPaged/GetUsersPendingSsoPagedQueryHandler.cs:27-80`
- **Módulos:** revamp-users, revamp-authentication, ext:s3
- **Tags:** pending SSO, pendentes SSO, merge suggestions
- Pendentes de vínculo SSO com `TotalMergeSuggestions`. RN-093, RN-094.

### FN-033 — Organograma de usuários: expandir um nó (unidades/departamentos/áreas, usuários e subordinados)
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UserChartController.cs:36-43`, `Solvace.Users/src/Solvace.Users.Application/Queries/UserChart/GetUserChartNode/GetUserChartNodeHandler.cs:40-52`
- **Módulos:** revamp-users
- **Tabelas:** TB_EMP_EmployeeUnityDepartArea (banco local da planta), TB_WCM_USER, TB_WCM_USER_AREA, TB_WCM_USER_LEADER, TB_CAF_CARGO
- **Tags:** organograma, children, expandir nó, subordinados diretos, UDA
`GET /UserChart/children`: filhos (`AreaDepartments`) e usuários (`Users`) do nó (RN-118). UC-022 a UC-024; RN-118 a RN-131; REL-003.

### FN-039 — Buscar usuários por nome, por lista de ids e por id (com área, departamento e cargo)
- RN-148, RN-147.

### FN-045 — Baixar planilha-modelo de importação de usuários SSO (Excel)
- RN-158; UC-030.

### FN-051 — Consultar preferências do usuário (globais, por planta, aplicações, funcionalidades, filtros e padrão)
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetUserPreferences/` (handlers `GetGlobalPreferences`, `GetSitePreferences`, `GetApplicationPreferences`, `GetFeaturesByApplicationId`, `GetFiltersByFeatureName`, `GetDefaultPreferences`), `.../UserPreferenceController.cs:35,45,55`, `.../UserPreferenceFeaturesController.cs:28,38`, `.../UserPreferenceFeatureFiltersController.cs:28`
- **Módulos:** revamp-users, ext:dynamodb
- **Tags:** user preferences, preferências do usuário, filtros salvos
- Seis consultas sobre o DynamoDB (documento do usuário por ambiente) e o padrão de usuário novo. UC-034, RN-178 a RN-181.

### FN-057 — Listar usuários por perfil (role) de uma aplicação
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetUsersPerRoleByApplicationIdGlobalQuery.cs:114-164`
- **Tabelas:** TB_WCM_USER_ROLE, TB_WCM_USER, TB_WCM_ROLE, TB_WCM_USER_GUEST, TB_WCM_USER_PICTURE, TB_WCM_USER_PREFERENCE
- **Módulos:** revamp-users
- **Tags:** usuários por perfil, GET /UsersPerRole/{applicationId}
- Usuários do site com seus perfis na aplicação, foto e cor. RN-187 a RN-189, RN-193, RN-194. UC-038.

### FN-063 — Organograma de usuários (região > site > UDA > usuários com contagens)
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetWcmRegionQuery.cs:17-254`
- **Tabelas:** TB_WCM_SITE, TB_WCM_COUNTRY, TB_WCM_REGION, TB_WCM_USER, TB_WCM_USER_AREA, TB_WCM_USER_LEADER, TB_WCM_USER_PICTURE, TB_WCM_USER_PREFERENCE, TB_EMP_EmployeeUnityDepartArea (local)
- **Módulos:** revamp-users
- **Tags:** organograma, user chart, subordinados diretos
- Raiz (regiões e sites com quantidade) e nós (UDAs filhas, usuários de área/líder). RN-207 a RN-212, REL-005. UC-043.

### FN-069 — Listar usuários pendentes de aprovação (com administradores do site)
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetWcmUserGlobalQuery.cs:347-450`
- **Módulos:** revamp-users
- **Tags:** usuários pendentes, GetWcmUsersPending, aprovação de usuário
- Pendentes (RN-222) de todas as unidades, cada um com os administradores da sua unidade (RN-232).

### FN-075 — Obter dados de sessão do usuário (perfil, preferências, unidade, área)
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetUserSessionInfoByIdGlobalQuery.cs:11-126`
- **Módulos:** revamp-users, revamp-authentication
- **Tags:** sessão do usuário, GetUserSessionInfoById, preferências
- RN-239.

### FN-081 — Registrar aceite GDPR do usuário
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Commands/GdprGlobalCommands.cs:16-29`
- **Módulos:** revamp-users
- **Tags:** GDPR, aceite, TB_WCM_GDPR
Grava o aceite no banco global. RN-255.

### FN-089 — Organograma de usuários (User Chart)
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UserChartController.cs:26-43`
- **Módulos:** revamp-users, ext:s3
- **Tags:** organograma, user chart, org chart, UserChart/children
- Árvore região > planta > unidade/departamento/área > usuário > subordinados, com contagens. UC-056, RN-272 a RN-275, REL-009.

### FN-095 — Editar usuário existente (formulário "Alterar")
- **Onde:** `...register-user-form.component.ts:170-182,287-309`
- **Módulos:** revamp-users
- **Tags:** editar usuário, Alterar, Modificar usuario
- Selecionar usuário na grade põe o form em modo `update`. UC-061; RN-288, RN-290.

### FN-102 — Importar planilha XLSX na liberação SSO e baixar template
- **Onde:** `src/app/settings/user-release-sso/modals/dialog-user-release-sso/dialog-user-release-sso.component.ts:116-189`
- **Módulos:** revamp-users
- **Tags:** Importar XLSX, Exportar template
- UC-068; RN-301.

### FN-108 — Cadastro de líderes (definir o líder de um usuário na planta atual)
- **Onde:** `src/app/settings/register-leaders/register-leaders.component.ts:26-255`, `register-leaders.component.html:1-259`, `UserLeaderController.cs:107-115`
- **Módulos:** revamp-users
- **Tags:** Cadastro de líderes, Leaders register, register-leaders, líder
- Lista usuários da planta atual com o líder; escolhe/remove o líder; exporta. UC-075, RN-323 a RN-326.

### FN-115 — Gerenciar usuário convidado (guest/visitante) em uma planta
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/UserGuest/CreateUserGuest/CreateUserGuestCommandHandler.cs:23`, `.../UpdateUserGuest/UpdateUserGuestCommandHandler.cs:23`, `.../RemoveUserGuest/RemoveUserGuestCommandHandler.cs:23`
- **Módulos:** revamp-users
- **Tags:** convidado, guest
- Visitante (área -15) com papéis. UC-082 a UC-084; RN-356 a RN-358; CFG-037; EST-015.

### FN-121 — Listar e pesquisar usuários (grade, cartões, filtros e ordenação)
- **Onde:** `Solvace.Users.API/Controllers/UserController.cs:213` (AllUsers), `:443` (search-users), `:455` (search-users-card), `:76` (Info), `:377` (pending)
- **Módulos:** revamp-users
- **Tags:** Users, Usuários, All Users, search users
- `GET User/AllUsers` (filtros de REL-013, `sortColumn` RN-379; escopo planta do token), `search-users`/`search-users-card` (seletor/cartão), `GET User/Info`, `GET User/pending` (pendentes, EST-016, `PendingUserSortColumn`).
- Regras: RN-368, RN-369, RN-370, RN-379. Exportação: FN-124.

### FN-127 — Mesclar usuário pendente com usuário existente
- **Onde:** `Solvace.Users.API/Controllers/UserController.cs:192` (merge), `:433` (matchToMerge)
- **Módulos:** revamp-users, revamp-authentication, ext:cognito
- **Tags:** Merge user, Mesclar usuário, match to merge, SSO
- `GET User/matchToMerge` (`userId`, `userName`, `email`, `firstLastName`) sugere o usuário; `POST User/merge` (`UserId`, `UserIdToLinkTo`) transfere a identidade SSO e elimina o pendente.
- Regras: RN-376. Caso de uso: UC-093. Notificação: NTF-017.

### FN-133 — Login federado: criar usuário federado, reconciliar SSO e corrigir planta
- **Onde:** `Solvace.Users.API/Controllers/UserFederatedController.cs:26`, `:41`, `:65`
- **Módulos:** revamp-users, revamp-authentication, ext:cognito
- **Tags:** Federated user, Usuário federado, Reconcile, SSO
- `POST UserFederated` e `POST UserFederated/Reconcile` são anônimos (RN-368); `PUT UserFederated/fixSite/{siteId}` exige token.
- Regras: RN-368, RN-388, RN-389, EST-016.

### FN-139 — Preferências do usuário
- **Onde:** `Solvace.Users.Application.Abstractions/Commands/Requests/WcmUserPreferenceRequest.cs:5-26`, `Commands/Interfaces/IUpdateUserPreferenceCommand.cs:7-9`
- **Módulos:** revamp-users, revamp-multilingual, ext:microsoft-graph
- **Tags:** preferências, idioma
- RN-415; CFG-045.

### FN-146 — Pesquisar usuários em lista
- **Onde:** `src/app/search/users/users.component.html:63-116`
- **Módulos:** revamp-users, legado-usuarios
- **Tags:** busca de usuários, search users, Lista, List
- Tabela paginada no servidor: Nome, Email, Número (`registryNumber`), Unidade (`originSiteName`), Área (`areaName`), Líder (`leaderFullName`), Status. RN-419, RN-421, RN-425, RN-429; UC-099.

### FN-152 — Vincular usuário federado pendente a usuário existente (merge)
- **Onde:** `src/app/settings/register-users/register-users.component.ts:65-71,138-156,174-210`
- **Módulos:** revamp-users, revamp-authentication, legado-usuarios
- **Tags:** vincular usuário, link user, merge user, liberação
- Clique em federado pendente abre `DialogLinkUserComponent` (100%, máx. 588px). RN-434, RN-435; UC-103.

### UC-005 — Salvar o filtro de check-in
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/UserPreferences/ChangeCheckInFilter/ChangeCheckInFilterCommandHandler.cs:15-36`
- **Módulos:** revamp-users
- **Tags:** check-in, filtro de check-in
- **Ator:** usuário logado (endpoint: GAP-002).
- **Fluxo principal:** `ChangeCheckInFilterCommandRequest` (UserId, CheckInFilter); monta o JSON (RN-013); grava `CHECKIN_FILTER` (`UpdateCheckInFilterByUserId`); invalida caches; `ChangeUserPreferenceCommandResponse(true)` (RN-014).

### UC-011 — Administrador redefine a senha de um usuário
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/ResetUserPassword/ResetUserPasswordCommandHandler.cs:42-76`
- **Módulos:** revamp-users, ext:cognito
- **Tags:** reset password, redefinir senha
- **Ator:** administrador (`LoggedUserId`). **Pré-condições:** RN-051.
- **Fluxo:** reset no Cognito; notifica usuário e administrador (RN-052); devolve `true`. Erros: RN-051.

### UC-017 — Usuário carrega listas de nomes (planta, usuários e visitantes, subordinados)
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UserController.cs:110-159`
- **Módulos:** revamp-users
- **Tags:** names, subordinate/names
- **Ator:** usuário autenticado. **Pré-condições:** claims `UserId` e `LastSiteId`.
- **Fluxo principal:** `GET /User/names?active=` (RN-070); `GET /User/UsersAndGuests/names?active=&siteId=` (RN-071); `GET /User/{userId}/subordinate/names` (RN-072).
- **Fluxos alternativos e de erro:** `UserId` = `Guid.Empty` → falha do validador; `LastSiteId` = 0 em `/names` e `/subordinate/names` → RN-057. **Pós-condições:** somente leitura.

### UC-023 — Usuário expande uma unidade, departamento ou área (UDA) no organograma
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/UserChart/GetUserChartNode/GetUserChartNodeHandler.cs:40-52,79-177`
- **Módulos:** revamp-users
- **Ator:** usuário autenticado.
- **Fluxo principal:** `GET /UserChart/children?siteId=<id>&udaId=<uda>` → `Users` da área (RN-125, RN-126) e UDAs filhas com contagens (RN-122 a RN-124).
- **Fluxos alternativos e de erro:** sem foto → `PictureFile = null`; sem cargo vigente → `EmployeePosition = null` (RN-127); RN-120.

### UC-029 — Administrador exporta permissões por papel (PDF)
- **Onde:** `Solvace.Users.Application/Queries/GetPdfUserRoles/GetPdfUserRolesQueryHandler.cs:35-88`

### UC-035 — Usuário consulta times
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetTeamInfo/GetTeamInfoHandler.cs:18-39`, `.../GetTeamByIdsHandler.cs:17-34`
- **Fluxo principal:** `GET /Team` (`pageSize`, `pageIndex`, `name`, `active`, `originatorId`) ou `GET /Team/InfoByIds?teamId=1&teamId=2`; planta = `LastSiteId` do token.
- **Fluxos alternativos e de erro:** RN-178, RN-182.

### UC-041 — Exportar a matriz usuário x perfil (PDF/Excel) com busca e ordenação
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/UsersPerRoleController.cs:48-68`
- **Fluxo principal:** 1) `GET /UsersPerRole/{applicationId}/download/pdf|excel` com `searchText`, `pageIndex` (0 vira 1), `pageSize`, `sortColumnText`, `sortDirection`; 2) `GetUsersPerRoleByApplicationIdPagedQueryRequest`: busca (RN-195), ordenação (RN-196), paginação em memória (RN-197); 3) `File(...)`.
- **Fluxos alternativos e de erro:** nenhum usuário → resultado paginado vazio (sem `Data`, sem `Total`).

### UC-047 — Sistema carrega os dados de sessão do usuário que entrou
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetUserSessionInfoByIdGlobalQuery.cs:11-126`
- **Módulos:** revamp-users, revamp-authentication
- **Tags:** sessão, login, user session
- **Ator:** sistema/login. **Fluxo:** consulta o usuário ativo (RN-239); inexistente/inativo → nulo.

### UC-054 — Sistema registra e consulta a auditoria de transferência
- **Onde:** `Solvace.Users/src/Solvace.Users.API/Controllers/AuditsTransferController.cs:26-93`
- **Módulos:** revamp-users, ext:qldb
- **Fluxo:** `POST /AuditsTransfer?action=` cria; `PUT` upsert; `DELETE /AuditsTransfer/{id}?action=&auditSiteId=` exclui; `GET /AuditsTransfer` lista; `GET /AuditsTransfer/{id}` consulta. RN-269, RN-270.

### UC-060 — Administrador cadastra um usuário
- **Onde:** `src/app/settings/register-users/components/register-user-form/register-user-form.component.ts:142-168`
- **Ator:** administrador do módulo Users.
- **Pré-condições:** modo `new`; unidade atual carregada (RN-289).
- **Fluxo principal:** escolhe **Tipo** (RN-287); preenche (RN-285); **Adicionar** (RN-286); endpoint do tipo (RN-288); toast, form limpo e `formCompleted` (grade recarrega). Erros: RN-286.
- **Pós-condições:** e-mail/notificação ao criar: GAP-066.

### UC-067 — Administrador vincula usuário SSO a um usuário existente
- **Onde:** `src/app/settings/user-release-sso/modals/dialog-link-user/dialog-link-user.component.ts:55-77`
- **Ator:** administrador do módulo Users.
- **Pré-condições:** linha marcada com **Link** "Sim" (RN-296).
- **Fluxo principal:** "Vincular usuário": sugestão (radio) ou manual (RN-298); digita o nome; marca "Confirmo os dados acima para vincular o usuário"; **Confirmar** → `isMerge` (alertas e **Pular etapa**: RN-298). Resultado: linha verde "Vínculo automático do sistema"; `userIdToLinkTo` no payload (RN-300).

### UC-073 — Administrador remove um visitante
- **Onde:** `register-visitors.component.ts:188-214`, `UserGuestController.cs:83-103`
- **Módulos:** revamp-users
- **Fluxo principal:** 1. "Remover". 2. Confirma "Tem certeza que deseja remover esse usuário?". 3. `DELETE /UserGuest/{userId}` (RN-310). 4. Toast "Removido com sucesso", formulário limpo, tabela recarregada, cache de elegíveis invalidado (RN-316).
- **Fluxos alternativos e de erro:** "LastSiteId não encontrado"; "Falha ao deletar usuário convidado". Sem `update`, com `loadingForm` ou inválido: nada acontece.

### UC-080 — Administrador consulta usuários pendentes
- **Onde:** `.../GetAllPendingUserQueryHandler.cs:17`
- **Módulos:** revamp-users
- **Tags:** pending users, usuários pendentes
- Fluxo: RN-352.

### UC-086 — Usuário altera suas preferências
- **Onde:** `.../ChangeUserPreferenceCommandHandler.cs:20`
- **Módulos:** revamp-users, revamp-multilingual
- **Tags:** preferências, preferences
- Fluxo: RN-362, RN-363.

### UC-092 — Administrador rejeita ou exclui um cadastro pendente
- **Onde:** `Solvace.Users.API/Controllers/UserController.cs:224`, `:240`
- **Módulos:** revamp-users
- **Fluxo:** `PUT User/reject` (`UserId`): administrador (RN-374), grava `RejectUser` na base Global, invalida cache `_status_`, `200` `true`, usuário Rejeitado (EST-016). `DELETE User/{userId}`: administrador (RN-374), status Pendente/Rejeitado (RN-375), apaga na base Global e no Cognito (sem bloquear, GAP-082), `200` `true`; falha de banco: `InvalidOperationException` "Falha ao excluir usuário do banco de dados.".

### UC-098 — Sistema grava a trilha de auditoria
- **Onde:** `Solvace.Users.Application/Commands/Audits/CreateUserCommandHandler.cs:20-75` (idem `AuditsGuest`, `AuditsLeader`, `AuditsTransfer`)
- **Módulos:** revamp-users, ext:qldb
- **Tags:** auditoria, QLDB
- **Ator:** o serviço (MediatR) ao publicar `Create/Update/Delete` de User, Guest, Leader ou Transfer.
- **Pré-condições:** `CancellationToken` não cancelado (RN-408).
- **Fluxo principal:** `Insert` / `Upsert` / `Delete<T>` (RN-409); `Dispose` no `finally`. Pós: nova revisão no ledger.

### UC-104 — Administrador consulta os usuários pendentes
- **Onde:** `src/app/settings/register-users/components/pending-users/pending-users.component.ts:34-66`
- **Módulos:** revamp-users
- **Tags:** pendentes, pending users
- **Ator:** administrador. Reticências → "Usuários pendentes" → diálogo somente leitura (RN-436).

## Regras de negócio

#### Notificação (CreateNotification)

### RN-006 — Auditoria: usuário logado, ação da query; POST insere, PUT faz upsert, DELETE remove no QLDB
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/Audits/CreateUserCommandHandler.cs:20-75`; `.../AuditsController.cs:60-71`
- **Módulos:** revamp-users, ext:qldb
- **Tags:** QLDB, Insert, Upsert
- **Regra:** `AuditUserId ??= claims.UserId`, `AuditUserName ??= claims.UserName` (só se o corpo não trouxe); `AuditAction` = query `action` (sempre sobrescreve, não validada). `POST` -> `_qldbExtension.Insert(request)`; `PUT` -> `Upsert(request.Id, request)`; `DELETE` -> `Delete<DeleteUserRequest>(request.Id, request)` (`AuditSiteId` da query, sem validar). Cancelado -> não grava, sem erro. `Id` do `UserFull` = `ID`, senão `USER_ID`. DELETE é evento no QLDB: histórico não apagado.

### RN-012 — Aceite do GDPR é do usuário logado e invalida o cache de status
- **Onde:** `.../Commands/ConfirmGdpr/ConfirmGdprCommandHandler.cs:14-26`; `.../API/Controllers/GdprController.cs:30-32`
- **Módulos:** revamp-users
- **Tags:** GDPR, "Falha ao adicionar GDPR do usuário"
- **Regra:** usuário das claims (`Guid.Parse(UserId)`). `AddAcceptByUserId` `false` -> `ArgumentException("Falha ao adicionar GDPR do usuário")`; sucesso -> `InvalidateUserStatusCacheAsync` e `true`.

### RN-018 — Estrutura da UDA: chave composta, nome até 256, tipos 1 a 4
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/EntityConfigurations/UnityDepartAreaConfiguration.cs:11-25`; `.../Abstractions/Enums/UnityDepartAreaType.cs:9-15`
- **Módulos:** revamp-users
- **Tags:** UDAName, UDAType, PK_dbo_UDA
- **Tabelas:** TB_WCM_UNITY_DEPART_AREA
- **Regra:** PK `PK_dbo_UDA` = (`UDAId`, `SiteId`), `UDAId` sem geração automática; `UDAName` obrigatório, máx. 256; `UDAFatherId`, `UDAType`, `IsActive`, `SiteId` obrigatórios; `CreateDate` padrão `SYSUTCDATETIME()`. Tipo 1 Plant, 2 Department, 3 Area, 4 Location. Índices `IX_dbo_UDA_01` (UDAId, inclui SiteId), `IX_dbo_UDA_02` (SiteId), fill factor 80.

### RN-024 — Consultas por lista de usuários em lotes de 2000 parâmetros
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Dapper/BatchedQueryConnectionWrapper.cs:10-37`; `.../DbConnectionExtensions.cs:8-37`
- **Módulos:** revamp-users
- **Tags:** batch, MaxParametersPerBatch, Dapper
- **Regra:** `MaxParametersPerBatch = 2000` (limite do SQL Server: 2100). Cada lote roda a consulta com a lista parcial; resultados concatenados na ordem (`BatchedQueryForUsersAsync`/`BatchedQueryAsync`).

### RN-030 — Nome de usuário (UserName) é único na base global
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/CreateUser/CreateUserCommandHandler.cs:85-90`
- **Tags:** username único, UserNameAlreadyExists
- **Regra:** antes do Cognito, `UserNameExistsAsync(global, UserName)` verdadeiro → erro de validação, chave `UserNameAlreadyExists`, "A conta de usuário já existe". Só o nome de usuário é único (e-mail pode repetir).

### RN-036 — Fornecedor: comando próprio força TypeId 3 e dispensa área; empregado e contratado repassam o pedido
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/CreateUserSuplier/CreateUserSuplierCommandHandler.cs:18-32`, `.../CreateUserSuplierCommandValidator.cs:23-25`
- **Tags:** CreateUserSuplier, UserTypeId.Supplier
- **Regra:** `TypeId` = `UserTypeId.Supplier` (3), ignorando o pedido; `SupplierId` obrigatório > 0 (sem mensagem customizada); repassa nome, usuário, e-mail, flags, `SupplierId` e ambiente; `AreaId` = 0 (RN-038 não grava área), sem líder, registro nem contratado. Valem RN-034. `CreateUserEmployee` e `CreateUserContracted` (`.../CreateUserEmployee/CreateUserEmployeeCommandHandler.cs:17-36`, `.../CreateUserContracted/...Handler.cs:17-36`) copiam todos os campos (inclusive `TypeId`, que decide RN-035) para `CreateUserCommandRequest`, com validadores herda

### RN-042 — Usuário federado já existente é recusado
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/CreateUserFederated/Services/Create/CreateUserFederatedService.cs:23-26`
- **Tags:** duplicidade, UserExistsAsync
- **Regra:** `UserExistsAsync(conexão, SsoId, SsoUserName)` verdadeiro → erro de validação, chave `UserNameAlreadyExists`, "A conta de usuário já existe". Lançada antes do `try`: não é embrulhada pela mensagem da RN-043. Critério de "existe": GAP-008.

### RN-048 — Mesclagem publica Deleted do pendente e notifica o usuário liberado
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/MergeUser/MergeUserCommandHandler.cs:84-142`
- **Módulos:** revamp-users, ext:sns
- **Tags:** Deleted, liberação de usuário, regra 132
- **Regra:** `PublishEventUserCommandRequest` com `TypeEvent = Deleted` (RN-054); não publica `Upserted` do destino. Notificação: NTF-009; exceção é engolida e o retorno continua `true`.

#### Excluir usuário SSO (DeleteUserSso)

### RN-054 — Eventos Upserted (retrato atual) e Deleted (só identificação); falha de publicação
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Commands/PublishEventUser/PublishEventUserCommandHandler.cs:26-55`
- **Módulos:** revamp-users, ext:sns
- **Tags:** Upserted, Deleted, SNS
- **Regra:** `Upserted`: `GetUserById` e copia `FullName`, `FirstLastName`, `UserName`, `PictureFile`, `BucketS3`, `KeyS3`, `PreferenceColor` (nulos viram vazio), `IsActive`, `SiteId`, `LastSiteId`, `SupplierId`, `ContractorId`, `TypeId`, `CreateDate`, `AreaId` (nulo vira 0); `LastUpdateDate` = UTC atual; JSON (Newtonsoft) no tópico `TopicNames.GetTopicName()`. `TypeEvent` ≠ `Upserted`: JSON só com `TypeEvent`, `UserId`, `EnvironmentUrl`; demais campos do pedido são descartados. Validador: `UserId` e `EnvironmentUrl` não vazios. Qualquer exceção → `ArgumentException` "Não foi possível publicar o event

### RN-060 — Filtros livre e por campo: nome, e-mail, matrícula, ativo, federado, ids
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/Builder/UsersWhereClause.cs:55-66,99-175`
- **Tabelas:** TB_WCM_USER
- **Módulos:** revamp-users
- **Tags:** filter, name, email
- **Regra:** `filter`: `(u.USER_FULLNAME like '%' + @filter + '%' or u.USER_EMAIL like '%' + @filter + '%' or u.USER_REGISTRY like '%' + @filter + '%')` (contém). `name`: `u.USER_FULLNAME like '%' + @fullName + '%'`. `email`: `u.USER_EMAIL like '%' + @email + '%'`. `registryNumbers`: `u.USER_REGISTRY = @registryNumber` (exato, valor único). `isActive`: `u.ACTIVE = @isActive`. `isFederated`: `u.FEDERATED = 1` / `0` (true = SSO). `userIds`: `( u.USER_ID IN @userIds )` se lista não vazia. Nulo = não filtra.

### RN-066 — URL da foto do usuário e do líder é assinada no S3 com validade configurável
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetAllUsersFiltered/GetAllUsersFilteredQueryHandler.cs:40,86,107,115`
- **Tabelas:** TB_WCM_USER_PICTURE (BUCKET_S3, KEY_S3, PICTURE_FILE)
- **Módulos:** revamp-users, ext:s3
- **Tags:** foto, picture, URL assinada
- **Regra:** SQL `PictureFile = CONCAT(KEY_S3, PICTURE_FILE)`, `NULL` se `KEY_S3` ou `PICTURE_FILE` nulo (idem `LeaderPictureFile`). Não vazio → `_awsS3Provider.GetUrlAssign(BucketS3, PictureFile, expireDate)`, `expireDate = DateTime.UtcNow.AddHours(ExpireImageHours)` (CFG-007); vazio → `null` (front usa iniciais, `InitialColor` = `INITIALS_COLOR` de `TB_WCM_USER_PREFERENCE`). Líder: `LeaderBucketS3`.

### RN-072 — Subordinados: só usuários ativos que têm o usuário como líder na planta
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetUsersBySiteIdGlobalQuery.cs:72-85`
- **Tabelas:** TB_WCM_USER_LEADER, TB_WCM_USER
- **Módulos:** revamp-users
- **Tags:** subordinados, subordinates, liderados
- **Quando:** `GET /User/{userId}/subordinate/names`.
- **Regra:** `select u.USER_ID as UserId, u.USER_FIRST_LAST_NAME as Name from TB_WCM_USER_LEADER l inner join TB_WCM_USER u on l.USER_ID = u.USER_ID where l.LEADER_ID = @userId and l.SITE_ID = @siteId and u.ACTIVE = 1`; `userId` da rota, `siteId = LastSiteId`. Sem `ORDER BY` nem validador de `userId`.

### RN-078 — Requisição cancelada devolve resposta vazia
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetAudits/GetAuditsQueryHandler.cs:20-23`
- **Módulos:** revamp-users
- **Regra:** `cancellationToken.IsCancellationRequested` → auditoria devolve resposta vazia; consultas de usuário/grupo/área devolvem `(0, vazio)`, lista vazia (ou `null` em `GetArea*`), sem erro.

### RN-084 — Status do convidado é derivado de área e ativo
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetUsersGuestPaged/GetUsersGuestPagedQueryHandler.cs:64`
- **Módulos:** revamp-users
- **Tags:** status, pending, active
- **Regra:** `AreaId == null` → `Status.Pending`; senão `IsActive` → `Status.Active`; senão `Status.Inactive`. Sem área vence sempre. EST-005.

### RN-090 — Usuários com permissão numa aplicação aceitam filtro opcional de ativo
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetUsersWithPermition/GetUsersWithPermitionQueryHandler.cs:21-35`
- **Módulos:** revamp-users, ext:s3
- **Tags:** users with permission, usuários com permissão, Active
- **Regra:** `(UserId, ApplicationId, Active?)`; os dois Guids `NotEmpty()`; `Active` `bool?` (nulo = sem filtro; semântica GAP-024). Devolve `Active`, `InitialColor`, `Name`, `UserId`, `PictureSigned` (RN-085).

### RN-096 — Excel de usuários: larguras fixas por coluna
- **Onde:** `Solvace.Users/src/Solvace.Users.Application/Queries/GetUsersSearchExport/GetExcelUsers/GetExcelUsersQueryHandler.cs:52-81`
- **Módulos:** revamp-users
- **Tags:** Excel, xlsx, largura de coluna
- **Regra:** coluna com alias "Email" → largura 40; demais → 20. Nulo vira `string.Empty`. Título com o nome do arquivo, centralizado (`ExcelTextAlignmentOption.Center`). `usersPaged.Data ?? []`. Colunas: GAP-027.

### RN-102 — Lista de avatares: nome, foto e cor das iniciais por usuário
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetAvatarListQuery.cs:10-24`
- **Tabelas:** TB_WCM_USER, TB_WCM_USER_PREFERENCE, TB_WCM_USER_PICTURE
- **Módulos:** revamp-users
- **Tags:** avatar, iniciais, INITIALS_COLOR
- **Regra:** `LEFT JOIN` com preferências (`INITIALS_COLOR`) e foto (`PICTURE_FILE`): sem foto/preferência → campos nulos; IDs inexistentes omitidos. Em lotes (`BatchedQueryForUsersAsync`). `PictureFile` bruto, sem assinatura.

### RN-108 — Equipes: filtro de ativo considera o grupo e o tipo do grupo
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetTeamInfoQuery.cs:62-72`
- **Tabelas:** TB_SYS_GRUPOS (`GRUPO_ATIVO`), TB_SYS_GRUPOS_TIPOS (`ATIVO`)
- **Módulos:** revamp-users, edv-solvace
- **Tags:** equipe ativa, inativa, GRUPO_ATIVO
- **Regra:** `active` nulo → sem filtro; `true` → `(GRUPO_ATIVO = 1 AND TIPO.ATIVO = 1)`; `false` → `(GRUPO_ATIVO = 0 OR TIPO.ATIVO = 0)`. `TIPO.ATIVO` nulável: tipo com `ATIVO` nulo não entra em "true" nem "false" (a confirmar).

### RN-114 — Subordinados: apenas liderados ativos na planta
- **Onde:** `Solvace.Users/src/Solvace.Users.Infra.Data.Global.SqlServer/Queries/GetUsersBySiteIdGlobalQuery.cs:72-101`
- **Tabelas:** TB_WCM_USER_LEADER, TB_WCM_USER
- **Módulos:** revamp-users
- **Tags:** subordinados, líder, HasSubordinates
- **Regra:** `GetSubordinateNames(userId, siteId)`: `TB_WCM_USER_LEADER` com `LEADER_ID = userId` e `SITE_ID = siteId` juntada a `u.ACTIVE = 1` (só diretos): `UserId`, `Name`. `HasSubordinates` = 1 se há ao menos um (`bool?`).



#### Nível raiz (regiões e plantas)

### RN-120 — Planta sem connection string falha com "ConnectionString not found for site id '{siteId}'."
- **Onde:** `GetUserChartNodeHandler.cs:86-87`, `148-149`, `181-182`
- **Módulos:** revamp-users
- **Regra:** sem a chave `SiteId` em `_ConnectionStrings` (site inexistente, sem `INSTANCE_NAME` ou cancelamento) → `Exception($"ConnectionString not found for site id '{siteId}'.")` → 500. Na lista de usuários só lança se `GetWcmUsersChart` devolveu algo.

### RN-126 — Dados exibidos de cada usuário e foto por URL assinada de 8 horas
- **Onde:** `GetUserChartNodeHandler.cs:153,159-172`; `UserNode.cs`; `GetWcmRegionQuery.cs:198-210`
- **Módulos:** revamp-users, ext:s3
- **Tags:** foto do usuário, URL assinada, S3
- **Regra:** `UserNode` = `UserId`, `Email` (`USER_EMAIL`), `UserRegistry` (`USER_REGISTRY`), `FullName` (`USER_FULLNAME`), `IsActive` (`ACTIVE`), `InitialColor` (`INITIALS_COLOR`), `PictureFile`, `EmployeePosition` (RN-127), `NumberOfDirectSubordinates` (RN-128), `AreaName`/`DepartmentName` (RN-129). `PictureFile`: `CONCAT(KEY_S3, PICTURE_FILE)` não nulo/vazio → `IAwsS3Provider.GetUrlAssign(BucketS3, <chave>, expiresDate)` com `expiresDate = DateTime.UtcNow.AddHours(8)`; senão `null`. `BucketS3` do `UserNode` nunca é preenchido (a API devolve `null`; confirmar se o front depende dele).

### RN-132 — Consultas que exigem site: `LastSiteId` igual a 0 é recusado; usuário inexistente gera "Usuário não encontrado"
- **Onde:** `Solvace.Users.Application/Queries/GetUserSessionInfo/GetUserSessionInfoByIdQueryHandler.cs:42-48`, `.../GetUserAvatar/GetUserAvatarQueryHandler.cs:31-41`
- **Regra:** `LastSiteId is 0` -> `ArgumentException("LastSiteId não encontrado")`; vale para sessão, avatar, cartões, match, nomes por área e sugestões SSO (exceções: `GetUserNamesByTeamIds`, RN-150; `GetUserInfosCached` repassa sem checar). Consulta por `UserId` nula (sessão, avatar, `GetUserByUserIdQueryHandler.cs:26-29`) -> `ToOneValidationExceptionByKeyValue("UserId", "Usuário não encontrado")`.
- **Tags:** LastSiteId não encontrado, Usuário não encontrado, site. **Módulos:** revamp-users

### RN-138 — Status, gamificação e moeda ficam em cache Redis por 8 horas, por ambiente e usuário; resposta vazia se status/sessão nulos
- **Onde:** `.../GetUserInfosCached/GetUserInfosCachedQueryHandler.cs:36-53,55-72`
- **Regra:** chave `GetUserInfosCachedQueryResponse_{keySuffix}_{environmentUrl}_{userId}`, `keySuffix` ∈ {`status`, `isGamefied`, `currency`}; TTL `TimeSpan.FromHours(8)` (fixo em código); só grava se não nulo. **A sessão não é cacheada.** Sem invalidação (GAP-036). `status` ou `session` nulos -> `GetUserInfosCachedQueryResponse` vazio (`:58-64`; na prática não ocorre).
- **Tags:** cache, Redis, 8 horas. **Módulos:** ext:redis, revamp-gamification

### RN-144 — Classificação final: Supplier/Contractor vêm depois do GDPR; todos os demais tipos são "Employee"
- **Onde:** `.../GetUserLoginStatus/GetUserLoginStatusQueryHandler.cs:57-72`
- **Regra:** `UserTypeId`: Oficial=1, Internal=2, Supplier=3, Others=4, System=5, Contractor=6 (`WcmUserQuery.cs:34-42`). Só 3 e 6 têm status próprio; 1, 2, 4 e 5 -> `EmployeeUser`.
- **Tags:** SupplierUser, ContractorUser, EmployeeUser, tipo de usuário. **Módulos:** revamp-users

#### Avatar, cartões, buscas e vínculo
