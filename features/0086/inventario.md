# Inventário das chaves de plugin (2026-11-05)

Caminhos relativos a `CIME/modules/`; `Mig/` = `Solvace.PullRequests/src/solvace.prform.infra/Migrations/`.
Leitores: BE = API, SK = skills (`skills/`), FE = front, EX = executor (`tools/Cime.ExecutionAgent`).

## Modelo
- `Plugin` (`solvace.prform.domain/Entities/Plugin.cs`): Description (nome único), AdminOnly, IsPersonal,
  PersonalFields (JSON; null = todas do usuário), IsOptional, FieldSettings (JSON `{key: PluginFieldSetting}`:
  Label, Optional, UseGlobalDefault, Hidden, Suggestions, Help), `Configurations` (1 `PluginConfiguration`).
- `PluginConfiguration.Options` = texto `[{ "Key": "string" }]` (só o `[0]`; tudo string). Leitura das chaves
  diferencia maiúsculas (`PluginConfigurationExtensions`); FieldSettings não.
- `PluginCacheManager` (24 h, `.First()` lança se não existe). `IPluginConfigurationResolver.GetEffectiveConfigurationAsync(name)`.
- `EPlugin`: AI=3, AZURE_DEVOPS=8, PULLREQUEST=9, BUCKET_IMAGE=11 (9 e 11 só por id).
- Segredo: `SensitiveFieldPolicy` (nome: token, secret, password, passwd, pwd, apikey, pat, webhook, `…Key`).
- Banco novo só cria por migração "Skills Configurations" e "Knowledge Center Configurations"; os demais foram criados
  pela tela (dados vieram pelo `tools/Cime.DataMigrator`). Migrações antigas apagadas: `git show 825e59f^:<Mig>/...`.

## Plugins
| Plugin (nome exato) | Achado por | Pessoal | Chaves |
|---|---|---|---|
| Skills Configurations | `solvace.prform.api/Skills/SkillsConfigurationKeys.cs` | não | 46 |
| AI Configurations (id 3) | `Solvace.Azure/.../Options/AIConfigurationKeys.cs`, `Solvace.AI/.../PluginAIService.cs:17` | pessoal + opcional (7 `Bug*` do usuário) | 18 |
| AzureDevOps Configurations (id 8) | `Solvace.Azure/.../Services/AzureService.cs:20` | pessoal (PAT) | 13 |
| Knowledge Center Configurations | `solvace.prform.api/Knowledge/KnowledgeCenterConfigurationKeys.cs` | não | 12 |
| Github Configurations | `Solvace.GitHub/.../GitHubService.cs:30` | pessoal (Token) | 4 (+Branch morta) |
| Teams Configurations | `solvace.prform.application/Teams/TeamsApprovalService.cs:35` | pessoal + opcional (WebhookUrl) | 8 |
| Realtime Configurations | `solvace.prform.application/PluginRealTimeOptionsProvider.cs:15` | não | 1 |
| Claude Plugin / Gemini Plugin / OpenAI Plugin / Copilot Plugin | `"{Provider} Plugin"` em `PluginAIService.cs:41`, `AIServiceFactory.cs` | pessoal (ApiKey) | 9 / 7 / 3 / 3 |
| PullRequest (id 9) | id — FE `register.component.ts`, `workbench-prs.store.ts` | não | 2 |
| Bucket/Cloudinary (id 11) | id — `solvace.prform.api/Controllers/ImageController.cs:44` | não | 3 |

## Skills Configurations (todas semeadas e com rótulo nas migrações)
| Chave | Tipo | Padrão | Lido por |
|---|---|---|---|
| BranchFlowByArea | JSON `[{areaContains, flow}]` | Release Management→release; Product Development Team→producao | BE `Skills/SkillsRulesService.cs`; SK |
| BranchStrategy | JSON `{repositories:[{match,kind}], flows:{flow:{kind:{base\|baseOptions,askBase,prs:[{suffix,from?,target}]}}}}` | ver /Skills/config | BE `SkillsRulesService`, `Promotion/PromotionService.cs`; SK; EX `RepoMap.cs` |
| BranchNamePattern | padrão `{card}` | `hotfix/{card}` | BE, SK |
| CommitMessagePattern | padrão `{card}`,`{summary}` | `AB#{card} {summary}` | BE, SK |
| PrTitlePattern | padrão `{card}`,`{TARGET}`,`{target}` | `AB#{card} {TARGET}` | BE, SK |
| DefaultRepository | texto | `edv-solvace` | BE, SK |
| TicketSystem | texto | `Freshservice` | só texto das skills (nenhum código) |
| BranchSuffixByTarget | JSON `{target: suffix}` | development -dev, qa -qa, release-version -rv, hotfix-version -hv, edge -edge | BE `SkillsRulesService`, `PromotionService`, `WorkItemsController.Catalog.cs`; SK |
| PromotionProfiles | JSON `{profiles:{kind:{label,targets:[{branch,draft?,mode?}]}}}` | | BE `PromotionService`, `PromotionParser` |
| PromotionBranchPattern | padrão `{branch}`,`{suffix}`,`{target}` | `{branch}{suffix}` | BE |
| ExecutorAnalysisModel | modelo (alias/id) | `opus` | BE `ExecutionQueueController.cs` |
| ExecutorCorrectionModel | modelo | `sonnet` | BE |
| ExecutorCorrectionNewSession | bool | `true` | BE `Execution/ExecutionQueueSettings.cs` |
| ExecutorNoteDelaySeconds | número | `120` | **morta** (0080) |
| SpecialistModel / SpecialistDeepModel | modelo | `sonnet` / `opus` | BE |
| WorkItemSlots | número 1–10 | `2` | BE, EX `Runner.cs` |
| AnalysisDefaultSteps / UsAnalysisDefaultSteps | JSON `[{key,title,description,kind?}]` | 9 etapas cada | SK `prmake-plan.sh` |
| CodeRefContextLines | número | `30` | SK |
| CodeRefGateSteps | CSV de etapas | `montar-analise,propor-solucoes` | BE `Execution/CodeCitationGate.cs` |
| EvidenceGateSteps | CSV de etapas | `montar-analise` | BE `Execution/EvidenceGate.cs` |
| ReverseEngineeringGateStep | CSV de etapas | `consultar-base,investigar-codigo` | BE `Knowledge/PluginReverseSettingsProvider.cs` |
| CorrectionLocalTests | enum changed/off | `changed` | SK |
| CorrectionTestMaxSeconds | número (s) | `420` | SK |
| AutoDevTestInQa | bool | `true` | BE `Execution/ExecutionCardDevOps.cs` |
| QaBranchPattern | CSV (`qa*` = prefixo) | `qa` | BE |
| SituationPostToDevOps | bool | `true` | BE |
| AssistantEnabled | bool | `true` | BE |
| AssistantHoursSteps | CSV de etapas (`corrigir-*`) | `montar-analise,propor-solucoes,corrigir-*,validar-qa` | BE |
| AssistantHoursPerStep | número (h) | `1` | BE, FE |
| AssistantAverageHours / …Bug / …UserStory | número (h) | `3` / `3` / `6` | BE, FE |
| WorkItemTaskKinds | JSON `[{kind,label,icon,executor,description,params:[{key,label,required?}],undo?}]` | 9 tipos | BE `WorkItemsController.Catalog.cs` |
| WorkItemTemplates | JSON `[{key,title,description,type?,tasks:[{title,kind,executor?,params}]}]` (`{card}`,`{branch}`,`{suffix:x}`,`{repository}`,`{flowTargets}`) | 4 modelos | BE |
| ReverseEngineeringApproverRoles | CSV | `admin,gestor` | BE `PluginReverseSettingsProvider.cs` |
| ReverseEngineeringRequiredDocs | CSV | `funcional,arquitetura,visao,spec-arquitetura,design,pratica` | BE |
| ReverseEngineeringMinCoverage | número 0–1 | `0.9` | BE |
| ReverseEngineeringTemplates | JSON `{docType: markdown}` | `{}` | BE |
| ReverseEngineeringReferenceDatabase | JSON `{environment,host,global,locals[]}` | DEMO | BE, SK `re.sh` |
| ReverseEngineeringGlossaryExclusions | JSON lista | ~75 palavras | BE |
| ReverseEngineeringSupersedes | JSON `{secao: [docs]}` | 11 | BE |
| ReverseEngineeringTranslations | JSON | multilingual/prod | BE |
| ReverseEngineeringInfra | JSON `{accounts:[{id,label}],regions[],readOnly}` | | BE |
| ReverseEngineeringGeneration | JSON `{subagentModel,modelByDoc{},maxParallel,areaBudgetKb,checkpointEvery,snapshotMaxAgeDays,smallFileLines,blockMaxLines}` | | BE, SK `re.sh` |

## AI Configurations
| Chave | Tipo | Padrão | Lido por |
|---|---|---|---|
| Provider | enum Claude/Gemini/OpenAI/Copilot | | BE `PluginAIService.cs:36`, `ArchitectureChatService.cs:105` |
| PromptBug / PromptUS | prompt | | BE `SkillsConfigService.cs:68`; FE `dialog-prompt.ts`; SK `gerar-prmake` |
| TemplatePassagemConhecimento | prompt (markdown) | | FE `handover-dialog.component.ts`; SK `gerar-handover` |
| BugSummaryPrompt | prompt `{cardNumber}{title}{reproSteps}{description}{rootCause}` | | BE `DevOpsActionsService.cs:90` |
| PromotionParsePrompt | prompt (vazio = padrão do código) | | BE `Promotion/PromotionParser.cs:22,119` |
| BugTestInProductionRequiredArea / Area / State / Comment | texto | ver `DevOpsActionsService.cs:93-96` | BE |
| BugReadyForQaState | texto | `In Development (done)` | BE |
| BugDevTestInQaState / BugDevTestInQaColumn | texto | `In Development (doing)` / `Dev Test in QA` | BE |
| BugInitialOriginalEstimate / RemainingWork / CompletedWork | número (h) | 6 / 6 / 0 | BE `:81-83` |
| BugClassificationPresets | JSON `[{key,label,resolutionType,generalClassification,classification,pattern}]` | 14 (reserva no código) | BE `DevOpsActionsService.cs:303` |
| AiModelPricesUsdPerMillion | JSON `{prefixoModelo:{input,output,cacheRead?,cacheWrite?}}` | `Mig/20261001235956` | BE `AiUsage/AiUsageRecorder.cs` |

## AzureDevOps Configurations
PersonalAccessToken (segredo, usuário), Organization, Project, ApiVersion, RootCauseFieldPath (`/fields/X`, padrão
`/fields/Custom.RCATechnicalCategorytext`), TitleFieldName (só FE), RetroStepsFieldName (só FE, padrão
`Microsoft.VSTS.TCM.ReproSteps`), FieldResolutionType, FieldGeneralClassification, FieldClassification,
FieldRemainingWork, FieldOriginalEstimate, FieldCompletedWork (padrões em `Solvace.Azure/.../Options/AzureDevOpsFieldKeys.cs`).

## Knowledge Center Configurations
Environment (enum dev/prod), DevHost/DevDatabase/DevSchema, ProdHost/ProdDatabase/ProdSchema, ExcludePatterns (JSON
lista de regex), ExcludeArticles / AllowArticles (JSON lista), MinTextLength (número ≥ 200), FullSyncHours (número).
Lidos em `Knowledge/PluginKnowledgeSettingsProvider.cs` e `Skills/SkillsConfigService.cs`.

## Demais
- **Github**: Token (segredo), Owner, Repo, ApiBaseUrl (opcional, Enterprise), Branch (**morta**).
- **Teams**: WebhookUrl (segredo, usuário), GroupName, MessageTitle (`{cardNumber}`), MessageBody (`{author}{prNumber}{repository}{branch}{targetBranch}{prTitle}`), ButtonText, TitleColor (enum Default/Dark/Light/Accent/Good/Warning/Attention), IncludeDescription (bool), DescriptionMaxLength (número).
- **Realtime**: AllowedOrigins (CSV).
- **Claude Plugin**: ApiKey, BaseUrl, Model (`claude-haiku-4-5`), SystemInstruction (prompt), MaxOutputTokens (8000), TimeoutSeconds (120), MaxRetries (3), Effort (enum low/medium/high/max), WorkspaceId. Lidos em `Solvace.AI/.../AIServiceFactory.cs:38-50`.
- **Gemini Plugin**: ApiKey, BaseUrl, Model, SystemInstruction, ThinkingLevel, MaxOutputTokens, TimeoutSeconds.
- **OpenAI / Copilot Plugin**: ApiKey, BaseUrl, Model.
- **PullRequest (9)**: ActiveBranchs (JSON ou literal JS `[{label, branchName}]`, FE faz `eval`), ActiveRepositories (JSON `[{label, value}]`).
- **Bucket (11)**: ApiKey, ApiSecret, CloudinaryUrl (contém segredo; nome não casa com a política).

## Sobreposições que a tela resolve por seção
Dev Test in QA (AI × Skills), classificação (DevOps × AI), horas (AI × Skills), modelos (5 lugares), prompts (AI,
provedor, Teams, Skills), branches/destinos (Skills × plugin 9), repositório (GitHub × Skills × plugin 9), etapas
(definidas em AnalysisDefaultSteps; citadas pelas travas e pelo assistente).
