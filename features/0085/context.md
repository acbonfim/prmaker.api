# Contexto do código (mapeado em 2026-11-05 para a 0085)

EP = CIME/modules/Solvace.ExecutionPlans/src · API = CIME/modules/Solvace.PullRequests/src/solvace.prform.api

## Backend
- `ExecutionPlanContext` (EP/solvace.executionplans.infra/Contexts/ExecutionPlanContext.cs), schema `execution`, xmin `Version`.
  Migração: `dotnet ef migrations add X --context ExecutionPlanContext --project CIME/modules/Solvace.ExecutionPlans/src/solvace.executionplans.infra --startup-project CIME/modules/Solvace.PullRequests/src/solvace.prform.api`.
  Última: `20261105000934_ReparentCorrectionsFromEmptyAnalysis0082`. Prform (DefaultContext) última: `20261105001615_SeedPersonalFieldHelp0083`.
- Entidades em EP/solvace.executionplans.domain/Entities/ (private setters, `DomainException`); status em ExecutionStatus.cs.
  `ExecutionPlan` (Kind default "analisar-bug", Phase analysis|correction, ParentPlanId, UpsertSteps/UpdateStep/StartStepByUser/
  CompleteStepByUser/CancelStep/TryAutoComplete/ChangeStatus), `ExecutionStep` (Key, Order, Status, Executor, Kind, Repository, DependsOn).
- `ExecutionPlanApplication` (partials ExecutionPlanApplication*.cs): `CreateAsync` (:43), `ChangeStatusAsync`, `NotifyAsync` (:827).
  Controller API/Controllers/ExecutionPlanController.cs (header `X-Execution-Client` = skill).
  Cancelar hoje = ChangeStatus("cancelled"); nada desfaz; não cancela o ExecutionRequest ativo.
- Fila: EP/.../Entities/ExecutionRequest.cs (`ExecutionRequestKind.All`, `ExecutionLane.Of(kind)` main|specialist), índices filtrados
  `IX_ExecutionRequests_ActiveCard` / `IX_ExecutionRequests_ActiveCaseCard`, duplicata → `ExecutionRequestDuplicateException`
  (ExecutionQueueRepository.cs:172). `ExecutionQueueApplication.cs`: NextAsync→TryClaimAsync→ClaimableAsync, `CreateAsync` (:76),
  `PlanChangedAsync` (:455), prompts FreshPrompt/ResumePrompt/ExecutorSuffix (:1025-1085); `ExecutionQueueApplication.Specialist.cs`
  (`SpecialistSlots=2`, `SpecialistPrompt` :89, `EnqueueSpecialistAsync`). Controller API/Controllers/ExecutionQueueController.cs
  (`GET next` :119 adiciona Model e CaseToken). Executor: tools/Cime.ExecutionAgent (JobRunner.cs:41 checa specialist; Runner.cs:127 vagas;
  subir `<Version>` no csproj; publicado trimmed SEM reflexão no JSON — usar contexto gerado/Utf8JsonWriter).
- CaseChat: EP/.../Entities/CaseChat.cs, EP/.../CaseChatApplication.cs (`ContextAsync` :455 = markdown do `prmake_case_context`;
  `DecideProposalAsync` :254), API/Controllers/CaseChatController.cs.
- MCP: API/Execution/PrmakeMcpTools.cs (padrão `[McpServerTool(Name=..., ReadOnly=...)] [Description] ... => Safe(async () => ...)`,
  `Safe` :597, `PlanIdAsync`, `ActorAsync`, `Serialize`), CaseChatMcpTools.cs, PrmakeBaseMcpTools.cs; registro em API/Program.cs:71-81.
  `CaseSpecialistMiddleware` em API/Execution/CaseSpecialistGuard.cs (`AllowedTools`; POST fora de CaseChat → 403).
- Skills config: API/Skills/SkillsConfigurationKeys.cs, SkillsConfigService.BuildAsync → `GET Skills/config`
  (API/Controllers/SkillsController.cs). Seed idempotente modelo: migração `SeedSpecialist0080` (jsonb_set + defaults || existente).
  Seeds de branch: 20260929061656_SeedSkillsConfigurations.cs, 20261002144003_SeedIntegrationApiBranchStrategy.cs,
  20261030192019_SeedPromotionProfiles.cs (PromotionBranchPattern "{branch}-{target}"; PromotionService.BuildNewBranchName :828).
- Card: `GET Azure/card/{id}/full` (Fields dict, `System.WorkItemType` "User Story" vs resto = Bug), evidências
  `IAzureService.GetCardEvidenceAsync`. Timeline via `IExecutionTimelineWriter.WriteAsync`.
- Tempo real: `IRealTimeNotifier.NotifyGroupAsync(group, event, payload)`; EP/solvace.executionplans.domain/RealTime/ExecutionPlanRealTimeEvents.cs.
- Números por card hoje: max+1 + índice único + retry. Não há sequência no código (a 0085 cria a primeira).

## Skills
- Ver skills/README.md. `skills/analisar-bug/scripts/prmake-plan.sh`: load_settings (:280) grava `$CARD_DIR/.prmake-settings.json`;
  `branches` (:1187-1284, BranchNamePattern + BranchFlowByArea + BranchStrategy flows prs suffix); `contexto` (:712), `contexto-correcao` (:781),
  `correction` (:926), `open-pr` (:1419). Bloco Windows/Git Bash obrigatório (prmake-plan.sh :100-109).
- `skills/especialista-do-caso/SKILL.md` (80 linhas) + scripts/prmake-case.sh; NÃO carrega regras/branches hoje (causa do `-rv`).
- analisar-bug: references/correcao.md §6-§8 (propor-solucoes, prmake_correction, execução). bug-fetch.sh imprime workItemType/isBug.
- gerar-prmake trata US (prompts.userStory, sem RCA). Cache do card: ~/.prmake/cards/<card>/ (dados/, analises/, case/, .prmake-settings.json).
- Catálogo: hash da pasta da skill = versão; auto-update por hook. Skill nova = pasta com SKILL.md + skill.json.

## Front (prform-app-0085, Angular 20 standalone, zoneless)
- Rotas: src/app/pages/authenticated/authenticated.routes.ts (`register` = tela de PR, `data.tab {title, icon, card:true}`, AuthGuard).
  Menu: src/app/components/side-menu/side-menu.component.ts (`menu` array `{label, icon, link:'auth/<path>'}`).
  Abas: TabsService.bindScreen/setMeta (exemplo reverse-engineering.component.ts:57).
- Reaproveitáveis: `app-card-panel` (title, icon), `app-card-timeline` ([cardNumber], [autoLoad], load(), openFrom(el)),
  `app-execution-plan` ([cardNumber] + @ViewChild .load(card), (headline)), `app-case-compact` ([card] signal input),
  `openCaseChat(dialog, card, origin)`, `app-card-alert-bar` ([alerts],[inline]), `app-card-situation`,
  `CardDetailsDialogComponent` (data {card: CardFull, cardNumber}), card-full.model.ts (`CardFull`).
- NÃO usar `CardPrStateService` (singleton da tela de PR). Estado próprio da página.
- Serviços: `${environment.apiUrl}X` (apiUrl termina com '/'). WsService: startConnection/on/off/addToGroup/removeFromGroup/_resynced.
- Material 20 (MatDialog, MatIcon, MatMenu, MatTooltip), tokens em src/styles.scss (--surface-1/2/3, --cc-*), dark.
  SVG custom: MatIconRegistry.addSvgIconLiteral (ex.: registerGithubIcon em github-actions-menu.component.ts).
  Diálogo modelo: components/execution-plan/cancel-plan-dialog.component.ts.
- Tour: components/guided-tour (tour.model.ts, pr-screen.tour.ts, GuidedTourService.autoStart/start).
- Zoneless: signals ou cdr.detectChanges(). Testes Karma + provideZonelessChangeDetection(). Build: `npx ng build --configuration development`.
