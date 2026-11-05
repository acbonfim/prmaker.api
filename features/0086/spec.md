# 0086 — Redesenho das configurações de plugin

> Proposta (rascunho para aprovação). Nada implementado ainda.

## Problema

Hoje a tela de plugins é uma grade de cartões; cada plugin abre um editor com uma lista plana de chaves, todas
editadas como texto (inclusive JSON de dezenas de linhas e prompts longos). Levantamento em 2026-11-05:

- **13 plugins, ~130 chaves.** Só o `Skills Configurations` tem 46 chaves de 8 assuntos diferentes (branches,
  promoção, executor, travas da análise, correção/QA, assistente, itens de trabalho, engenharia reversa).
- **Assuntos espalhados entre plugins.** Dev Test in QA: estado/coluna no `AI Configurations`, movimento automático e
  branches de QA no `Skills`. Classificação: nomes dos campos no `AzureDevOps`, opções no `AI`. Horas: estimativa
  inicial no `AI`, médias no `Skills`. Modelo de IA: em 5 lugares. Branches/destinos de PR: `BranchStrategy`,
  `PromotionProfiles` e `ActiveBranchs` (plugin 9).
- **Sem tipo.** Booleano, número, lista de etapas, CSV, segredo, prompt com variáveis e JSON estruturado são todos
  uma caixa de texto. O admin não sabe o formato, o padrão, nem quem lê a chave.
- **Chaves mortas** convivendo com as vivas: `ExecutorNoteDelaySeconds`, `TicketSystem` (só citada no texto das
  skills), `Branch` do GitHub.
- **Riscos de manutenção:** o `PUT update-configuration` substitui o dicionário inteiro (duas abas abertas = uma
  apaga a outra); `CloudinaryUrl` contém segredo e não é tratada como segredo; plugins 9 e 11 achados por id.

## Proposta

### 1. Catálogo das chaves (fonte única do "o que é cada chave")

Um catálogo no backend descreve **cada chave que o código lê**: plugin onde fica, seção, ordem, rótulo, ajuda,
tipo de editor, opções/limites, valor padrão (o mesmo de reserva que o código já usa), variáveis aceitas (prompts e
padrões), quem lê (API, skills, front, executor) e se está obsoleta.

- Servido por `GET PluginConfiguration/catalog` (admin).
- **Só descreve — não guarda valor.** Valores continuam onde estão (mesmo plugin, mesma chave); nenhum leitor muda.
- Rótulo/ajuda que o admin já gravou em `FieldSettings` continuam valendo por cima do catálogo.
- Chave nova no código = entrada nova no catálogo no mesmo PR (a regra "nada fixo em código" continua: o catálogo é
  o esquema, o valor é do admin).

### 2. Tela nova: Configurações por seção (não por plugin)

Página inteira (`/plugin-manager`), sem diálogo. Navegação lateral por **assunto**; cada seção mostra as chaves de
qualquer plugin que trate do assunto (com a etiqueta discreta do plugin de origem).

| Seção | Grupos | Chaves (plugin) |
|---|---|---|
| **Azure DevOps** | Conexão | Organization, Project, ApiVersion, PersonalAccessToken (DevOps) |
| | Campos do card | Field*, RootCauseFieldPath, TitleFieldName, RetroStepsFieldName (DevOps) |
| | Estados e movimentos | BugReadyForQaState, BugTestInProduction*, BugDevTestInQa* (AI) · AutoDevTestInQa, QaBranchPattern, SituationPostToDevOps (Skills) |
| | Classificação | BugClassificationPresets (AI) |
| **Branches e PRs** | Padrões de nome | BranchNamePattern, BranchSuffixByTarget, CommitMessagePattern, PrTitlePattern, DefaultRepository (Skills) |
| | Fluxo por área e repositório | BranchFlowByArea, BranchStrategy (Skills) |
| | Tela de PR | ActiveRepositories, ActiveBranchs (PullRequest #9) |
| | Promoção em lote | PromotionProfiles, PromotionBranchPattern (Skills) · PromotionParsePrompt (AI) |
| **Análise e correção** | Etapas padrão | AnalysisDefaultSteps, UsAnalysisDefaultSteps |
| | Travas | ReverseEngineeringGateStep, CodeRefGateSteps, EvidenceGateSteps, CodeRefContextLines |
| | Testes locais | CorrectionLocalTests, CorrectionTestMaxSeconds |
| **Executor e modelos** | Modelos | ExecutorAnalysisModel, ExecutorCorrectionModel, SpecialistModel, SpecialistDeepModel |
| | Execução | ExecutorCorrectionNewSession, WorkItemSlots |
| **Itens de trabalho** | Tipos de sub-tarefa | WorkItemTaskKinds |
| | Modelos de item | WorkItemTemplates |
| **Assistente e horas** | | AssistantEnabled, AssistantHoursSteps, AssistantHoursPerStep, AssistantAverageHours(Bug/UserStory) (Skills) · BugInitial* (AI) |
| **IA do servidor** | Provedor | Provider (AI) · Claude/Gemini/OpenAI/Copilot Plugin (Model, Effort, MaxOutputTokens, TimeoutSeconds, …) |
| | Custos | AiModelPricesUsdPerMillion (AI) |
| **Prompts e modelos de texto** | | PromptBug, PromptUS, BugSummaryPrompt, TemplatePassagemConhecimento (AI) · SystemInstruction (provedor) · MessageTitle/MessageBody (Teams) |
| **Engenharia reversa** | Aprovação e cobertura | ApproverRoles, RequiredDocs, MinCoverage, Supersedes |
| | Geração | ReverseEngineeringGeneration, ReverseEngineeringTemplates |
| | Fontes | ReferenceDatabase, Translations, Infra, GlossaryExclusions |
| **Knowledge Center** | Ambiente / Filtro / Sync | Environment, Dev*/Prod*, ExcludePatterns, ExcludeArticles, AllowArticles, MinTextLength, FullSyncHours |
| **Integrações** | GitHub, Teams, Tempo real, Imagens | Owner, Repo, ApiBaseUrl, Token · WebhookUrl, GroupName, ButtonText, TitleColor, IncludeDescription, DescriptionMaxLength · AllowedOrigins · Cloudinary |
| **Plugins (avançado)** | | Lista técnica dos plugins: nome, só admin, uso pessoal/opcional, quem preenche cada campo, chaves fora do catálogo, criar/duplicar/excluir |

Topo da página: busca única (rótulo, chave, ajuda, valor) que filtra todas as seções; contador de pendências
(chave faltando, JSON inválido, valor diferente do padrão).

### 3. Editor por tipo (em vez de caixa de texto)

| Tipo | Editor |
|---|---|
| `bool` | interruptor |
| `number` | campo numérico com mínimo/máximo e unidade (s, h, linhas) |
| `enum` | lista de escolha (ex.: CorrectionLocalTests, Environment, TitleColor, Effort, modelos) |
| `secret` | mascarado, botão "alterar"; nunca mostra o valor (CloudinaryUrl passa a ser segredo) |
| `list` | chips (CSV ou lista JSON — grava no formato que o leitor espera) |
| `steps` | chips com autocompletar a partir das etapas cadastradas em AnalysisDefaultSteps/UsAnalysisDefaultSteps (aceita `corrigir-*`) |
| `map` | tabela chave → valor (BranchSuffixByTarget, ReverseEngineeringTemplates, Supersedes) |
| `table` | tabela de linhas com colunas definidas (BranchFlowByArea, AnalysisDefaultSteps, BugClassificationPresets, ActiveBranchs, ActiveRepositories, preços de IA) — reordenar, incluir, excluir |
| `prompt` / `pattern` | editor grande com as variáveis aceitas em chips clicáveis (`{card}`, `{summary}`…), aviso de variável desconhecida e prévia com um exemplo |
| `json` | editor de JSON com validação, "formatar" e diferença para o padrão — só para as estruturas profundas (BranchStrategy, PromotionProfiles, WorkItemTaskKinds, WorkItemTemplates, objetos da engenharia reversa) |

Em todo campo: rótulo, ajuda, **padrão** ("restaurar padrão"), selo "alterado", **quem lê** (API · skills ·
front · executor) e, nos campos pessoais, quem preenche (usuário/fixo) — como hoje.

### 4. Salvar com segurança

- **Salvar por seção**, com revisão do que muda (antes → depois) antes de gravar.
- Endpoint novo `PATCH PluginConfiguration/{id}/values` — grava só as chaves alteradas (mescla no servidor), com
  verificação de versão (`xmin`): se outro admin salvou antes, avisa em vez de sobrescrever. O `PUT` atual fica
  para a área avançada.
- Histórico de alterações por chave (quem, quando, antes/depois) — **opcional, fase 2**.

### 5. Limpeza

- Chaves mortas (`ExecutorNoteDelaySeconds`, `Branch` do GitHub) marcadas obsoletas: escondidas por padrão, com
  "remover" na área avançada. `TicketSystem` vira chave viva (skills passam a ler pelo /Skills/config) ou obsoleta.
- Chave que o código lê e que não existe no banco aparece como "usando o padrão" com botão "gravar".
- Fora de escopo: semear em banco novo os plugins criados à mão (AI, DevOps, GitHub…) — anotar como pendência.

## Fases (proposta)

- **B1** Catálogo (todas as ~130 chaves) + `GET catalog` + `PATCH values` com versão + segredo pelo tipo.
- **F1** Página nova: navegação por seção, busca, editores simples (bool/number/enum/secret/list/steps/prompt/json).
- **F2** Editores estruturados: `map` e `table`.
- **F3** Área avançada (plugins, campos pessoais) reaproveitando o editor da 0032; remoção do diálogo antigo.
- **Q1** e2e no harness local (Postgres isolado) com cópia das configurações de produção.

## Critérios de aceite

- Toda chave lida pelo código aparece numa seção, com rótulo, ajuda, tipo e padrão; nenhuma chave existente some.
- Os leitores (API, skills, executor, front) não mudam: mesmos plugins, mesmas chaves, mesmo formato gravado.
- Salvar uma seção não apaga chaves de outra seção nem alterações de outro admin.
- JSON/estrutura inválida não é gravada.
