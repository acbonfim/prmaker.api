# Plano — Feature 0086: Configurações por seção (redesenho dos plugins)

Worktrees: back `prform.api-0086`, front `../solvace.prform.web/prform-app-0086` (branch `feature/0086`, a partir da
master; `node_modules` linkado de `prform-app-0019`). Spec: `spec.md`. Este contrato é a fonte da verdade entre os
agentes.

## Decisões (aprovadas em 2026-11-05)

- **Organização por assunto**, não por plugin: as seções juntam chaves de vários plugins. **Os valores não mudam de
  lugar** — mesmo plugin, mesma chave, mesmo formato gravado (string; JSON como texto). Nenhum leitor (API, skills,
  executor, front) muda.
- **Catálogo no código** (`PluginSettingsCatalog`, projeto `solvace.prform.api`): descreve cada chave (seção, grupo,
  ordem, rótulo, ajuda, tipo, padrão, opções, variáveis, colunas, quem lê, obsoleta). Rótulo/ajuda gravados pelo admin
  em `FieldSettings` sobrepõem os do catálogo. Chave nova no código = entrada nova no catálogo no mesmo PR (teste
  garante formato).
- **Escopo completo**: catálogo + API + página nova + editores simples e estruturados + revisão/histórico + área
  avançada (plugins, campos pessoais — reaproveita o editor da 0032).
- Rota da tela não muda: `auth/plugin-manager` (menu "Plugins" → rótulo "Configurações"). Deep link
  `?section=<key>&field=<id>`.

## Contrato

### `GET api/v1/PluginConfiguration/settings` (admin)

Catálogo resolvido contra o banco (lido do cache de plugins).

```jsonc
{
  "sections": [{
    "key": "devops", "title": "Azure DevOps", "icon": "view_kanban", "description": "…",
    "groups": [{ "key": "conexao", "title": "Conexão", "description": "…", "fields": [ /* SettingField */ ] }]
  }],
  "plugins": [{ "id": 8, "name": "AzureDevOps Configurations", "exists": true, "isPersonal": true,
                "isOptional": false, "adminOnly": false, "version": "4711" }],
  "uncataloged": [{ "pluginId": 8, "pluginName": "…", "key": "Foo", "value": "…", "secret": false }],
  "missingPlugins": ["OpenAI Plugin"]
}
```

`SettingField`:

```jsonc
{
  "id": "8:Organization",            // "<pluginId>:<key>"; plugin inexistente → "<pluginName>:<key>"
  "pluginId": 8,                     // null = plugin não cadastrado (campo desabilitado, aviso)
  "pluginName": "AzureDevOps Configurations",
  "key": "Organization",
  "label": "Organização", "help": "…",          // FieldSettings do admin > catálogo
  "type": "text",                    // text|textarea|secret|bool|number|enum|list|steps|map|table|pattern|prompt|json
  "value": "solvace",                // string gravada; null quando secret ou chave inexistente
  "hasValue": true,                  // secret: se há valor gravado
  "exists": true,                    // a chave existe no dicionário do plugin
  "default": "…",                    // padrão de reserva do código (null = sem padrão)
  "isDefault": false,                // value == default (comparação de texto; JSON comparado normalizado)
  "options": [{ "value": "opus", "label": "Opus" }],   // enum (e sugestões de text)
  "allowCustom": false,              // enum aceita valor fora da lista
  "min": 1, "max": 10, "unit": "s",  // number
  "listFormat": "csv",               // list|steps: "csv" (a,b,c) ou "json" (["a","b"])
  "stepSources": ["Skills Configurations:AnalysisDefaultSteps", "Skills Configurations:UsAnalysisDefaultSteps"],
                                     // steps: de onde vêm as etapas (chave `key` de cada item) + aceita "prefixo-*"
  "variables": [{ "name": "card", "description": "número do card", "example": "77434" }],  // pattern|prompt
  "columns": [{ "key": "areaContains", "label": "Área contém", "type": "text", "required": true,
                "options": null, "width": "md" }],   // table (type da coluna: text|textarea|number|bool|enum|list)
  "rowKey": null,                    // table cujo valor é OBJETO {chave: {…}}: nome da coluna que vira a chave
  "mapKeyLabel": "Destino", "mapValueLabel": "Sufixo", "mapValueType": "text",  // map (text|textarea|list)
  "readBy": ["api", "skills", "front", "executor"],
  "personal": { "userFills": true, "required": false, "useGlobalDefault": true } ,  // null fora de plugin pessoal
  "obsolete": false,                 // obsoleta: escondida por padrão, "remover" disponível
  "advanced": false,                 // detalhe técnico: recolhido em "Mais opções" do grupo
  "multilineHint": false
}
```

Regras de formato gravado (o editor sempre devolve **string**):
- `bool` → `"true"`/`"false"`; `number` → invariante (`"0.9"`); `enum` → o valor.
- `list`/`steps` → `csv`: itens unidos por `,` sem espaço; `json`: array JSON compacto.
- `map` → objeto JSON; `table` → array JSON de objetos (ou objeto quando `rowKey`), **preservando propriedades que não
  são colunas** em cada linha; `json` → JSON (identado com 2 espaços).
- Valor que não é JSON estrito (ex.: `ActiveBranchs` em literal JS com aspas simples/chaves sem aspas): o editor
  tenta normalizar (aspas, chaves, vírgula final); se não der, cai no editor `json` com o texto cru e o aviso.

### `PATCH api/v1/PluginConfiguration/settings` (admin)

```jsonc
{ "changes": [ { "pluginId": 8, "key": "Organization", "value": "nova", "expected": "solvace", "remove": false } ] }
```
- Aplica tudo numa transação (pode tocar vários plugins). Mescla: só as chaves enviadas mudam.
- `expected` = valor que a tela leu (null = a chave não existia). Diferente do atual → **409**
  `{ "conflicts": [{ "id", "key", "pluginId", "current", "expected" }] }` (nada gravado). Segredo: sem checagem e o
  `current` vem mascarado.
- Validação por tipo do catálogo (bool, number+min/max, enum sem `allowCustom`, JSON das estruturas, raiz
  array/objeto) → **400** `{ "errors": [{ "id", "message" }] }`. Chave fora do catálogo: só texto.
- `remove: true` apaga a chave (obsoletas e fora do catálogo).
- Concorrência: `xmin` em `PluginConfiguration` + nova tentativa (até 3) relendo e rechecando `expected`.
- Depois: `RefreshPluginsAsync`, aviso em tempo real do plugin 9 (mesmo de hoje), linha no histórico.
- **200** `{ "plugins": [{ "id", "version" }], "fields": [ /* SettingField atualizados */ ] }`.

### `GET api/v1/PluginConfiguration/settings/history?pluginId=&key=&take=50` (admin)

`[{ "pluginId", "pluginName", "key", "oldValue", "newValue", "changedBy", "changedByName", "changedAt" }]` (mais recente
primeiro; segredo → `"********"`/`""`). Tabela `prform."PluginSettingChanges"` (Id, PluginId, Key, OldValue,
NewValue, ChangedBy, ChangedAt `timestamptz`). O `PUT update-configuration` antigo também grava histórico (diferença
chave a chave).

### Seções (ordem) e grupos

Mapa completo na `spec.md` §2. Chaves de seção: `devops`, `branches`, `analise`, `executor`, `itens`, `assistente`,
`ia`, `prompts`, `engenharia-reversa`, `knowledge`, `integracoes`. A área "Plugins (avançado)" é do front (não vem no
catálogo).

Tipos esperados das chaves com estrutura (o resto é text/bool/number/enum/secret/pattern/prompt conforme o
inventário):

| Chave | Tipo | Detalhe |
|---|---|---|
| BranchFlowByArea | table | colunas areaContains (text), flow (enum producao/release, allowCustom) |
| BranchSuffixByTarget | map | Destino → Sufixo |
| BranchStrategy, PromotionProfiles | json | estrutura profunda (com ajuda do formato) |
| AnalysisDefaultSteps, UsAnalysisDefaultSteps | table | key, title, description (textarea), kind (enum "", question) |
| CodeRefGateSteps, EvidenceGateSteps, ReverseEngineeringGateStep, AssistantHoursSteps | steps | csv |
| QaBranchPattern, ReverseEngineeringApproverRoles, ReverseEngineeringRequiredDocs | list | csv |
| ReverseEngineeringGlossaryExclusions, ExcludePatterns, ExcludeArticles, AllowArticles | list | json |
| BugClassificationPresets | table | key, label, resolutionType, generalClassification, classification, pattern |
| AiModelPricesUsdPerMillion | table | `rowKey: "model"`; input, output, cacheRead, cacheWrite (number) |
| ActiveBranchs | table | label, branchName (normaliza literal JS) |
| ActiveRepositories | table | label, value |
| WorkItemTaskKinds, WorkItemTemplates | json | (editor estruturado fica para depois; JSON com ajuda) |
| ReverseEngineeringSupersedes | map | Seção antiga → documentos (`mapValueType: list`) |
| ReverseEngineeringTemplates | map | Documento → modelo markdown (`textarea`) |
| ReverseEngineeringReferenceDatabase/Translations/Infra/Generation | json | |
| BranchNamePattern, CommitMessagePattern, PrTitlePattern, PromotionBranchPattern | pattern | variáveis do código |
| PromptBug, PromptUS, BugSummaryPrompt, PromotionParsePrompt, TemplatePassagemConhecimento, SystemInstruction, MessageTitle, MessageBody | prompt | variáveis quando o código substitui |
| PersonalAccessToken, Token, ApiKey, ApiSecret, WebhookUrl, **CloudinaryUrl** | secret | CloudinaryUrl por tipo (não pelo nome) |
| ExecutorNoteDelaySeconds, Github `Branch` | — | `obsolete: true` |

## Fases (ondas)

| Onda | Fase | Escopo | Depende de |
|---|---|---|---|
| 1 | **B1** | Catálogo completo (~130 chaves), `GET/PATCH settings`, histórico (tabela + migração `PluginSettings0086`, xmin), validação por tipo, histórico no PUT antigo, testes | — |
| 1 | **FA** | Página nova (rota, navegação por seção, busca, pendências, estado de edição, salvar seção com revisão antes→depois, conflito 409, histórico, deep link, aviso ao sair), editores simples (text/textarea/secret/bool/number/enum/list/steps), área "Plugins (avançado)" (grade atual + editor 0032 + chaves fora do catálogo), modelo/serviço, specs | contrato |
| 1 | **FB** | Editores estruturados: `table`, `map`, `json`, `pattern`/`prompt` (variáveis clicáveis, aviso de variável desconhecida, prévia com exemplo), normalização de literal JS, specs | contrato |
| 2 | **Q1** | Build dos dois lados, e2e no harness local (Postgres isolado com cópia das configurações), ajustes | B1, FA, FB |

Contrato de editor (FA ↔ FB): componentes standalone em
`src/app/pages/authenticated/plugin-settings/editors/`, todos com
`field = input.required<SettingField>()`, `value = model.required<string>()` (string gravada) e
`invalid = output<string | null>()` (mensagem de erro ou null). Arquivos/seletores do FB:
`table-editor.component.ts` (`app-setting-table-editor`), `map-editor.component.ts` (`app-setting-map-editor`),
`json-editor.component.ts` (`app-setting-json-editor`), `prompt-editor.component.ts` (`app-setting-prompt-editor`,
serve `pattern` e `prompt`). O FA cria `plugin-settings.model.ts` (tipos do contrato) — o FB importa dele; se ainda não
existir quando o FB começar, o FB cria com os tipos acima e o FA completa.
