# Feature 0002 — Integrações pessoais (token por usuário)

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch (nos dois repos): `feature/0002` criada a partir de `master` (já com a 0001).

| Repo | Caminho | Sigla das fases |
|---|---|---|
| Backend (este) | `prform.api` | `B*` |
| Frontend (Angular 20 + PrimeNG 20 + Material, **zoneless**) | `../solvace.prform.web/prform-app` | `F*` |

---

## 1. Análise

### 1.1 Como está hoje

**Plugins (backend)**
- `Plugin` (`Plugins`: Description, AdminOnly, soft delete) 1—1 `PluginConfiguration` (`Options` = JSON `[{ "Chave": "valor", ... }]`). Os campos de cada plugin são **dinâmicos** (as chaves do JSON).
- `PluginCacheManager`: lista inteira em memória (`plugins:all`, 24 h), carregada no startup (`PluginCacheHostedService`) e recarregada a cada create/update/delete. Leitura por nome (`GetCachedPluginByName`) ou id.
- `PluginConfigurationController`: `get-all` (admin), `get-all-by-id` (qualquer logado, salvo `AdminOnly`), create/update/delete (admin).

**Quem lê token hoje (todos com o mesmo token global)**
| Consumidor | Plugin | Como lê |
|---|---|---|
| `GitHubService` (`Solvace.GitHub`) | "Github Configurations" (Token, Owner, Repo, Branch) | construtor — lança exceção se não houver token |
| `AzureService` (`Solvace.Azure`) | "AzureDevOps Configurations" (Organization, Project, ApiVersion, RootCauseFieldPath…) | construtor |
| `HttpClient` nomeado "AzureDevOps" | idem (PersonalAccessToken) | **fixa o header Basic na criação do cliente**, com o PAT global |
| IA, Bucket (Cloudinary), RealTime, PullRequest (repos/branches) | vários | fora do escopo desta feature (spec 3: "devops e github por hora") |

**Usuário**: a api-key (JWT) traz a claim `ExternalId` (Guid) — já usada pela Timeline. É a chave do usuário para as configurações pessoais.

**Front**: tudo que chama GitHub/Azure está na tela de PR (`pages/authenticated/register`) e nos seus dialogs (Abrir PR, Gerar com IA, Handover, Detalhes do card). Menu do usuário em `components/top-menu` ("Meu perfil", "Sair"). Admin edita plugins em `pages/authenticated/plugin/dialogEdit` (campos dinâmicos + toggle "Somente administradores").

### 1.2 Modelo proposto

```
Plugins (existente)                      UserPluginConfigurations (nova)
────────────────────                     ─────────────────────────────────────
Id                                 1───N PluginId (FK)
Description                              UserExternalId (Guid)      UNIQUE(PluginId, UserExternalId)
AdminOnly                                Options (JSON dos valores; sensíveis criptografados)
IsPersonal  (NOVO, default false)        CreatedAt/UpdatedAt/CreatedBy/UpdatedBy
PluginConfiguration.Options = "modelo"
  (chaves = campos que cada usuário preenche)
```

- Os **campos** de um plugin pessoal continuam definidos no plugin global (spec 2.2/7): o usuário preenche **as mesmas chaves**. Chaves que não existem mais no global são ignoradas; chaves novas aparecem vazias.
- **Plugin removido, desativado ou desmarcado como pessoal** (spec 8): some da lista do usuário e **não é lido** como pessoal; os dados do usuário ficam guardados (voltam a valer se o plugin for remarcado).
- **Plugins não pessoais** (spec 9): leitura exatamente como hoje.

### 1.3 Resolução do valor em runtime

`IPluginCacheManager` ganha uma porta única para os consumidores:

```csharp
// Plugin comum → configuração global (como hoje).
// Plugin pessoal → configuração do usuário da requisição (claim ExternalId);
//   se faltar algo → PersonalIntegrationRequiredException (vira 403 com código próprio).
PluginConfiguration GetEffectiveConfiguration(string pluginName);
```

GitHub/Azure passam a chamar isso **por operação** (não no construtor) e o Azure passa a montar o header `Authorization` por requisição.

### 1.4 Cache (spec 5)
Mesma estratégia dos plugins globais (`ICacheService`, 24 h, recarga ao salvar): chave `plugins:user:{versão}:{externalId}`. A **versão** é incrementada sempre que um plugin global muda (create/update/delete/flag) — assim todos os caches de usuário são invalidados sem precisar varrer chaves.

### 1.5 Pontos de atenção
1. **Segredos**: tokens pessoais no banco → criptografar em repouso e **nunca** devolver campos sensíveis ao front (só "preenchido"). Hoje o `get-all-by-id` devolve o token global a qualquer logado — para plugins pessoais isso não pode se repetir.
2. **Chave de criptografia** precisa ser a mesma em todas as instâncias/deploys do Cloud Run (não dá para usar o DataProtection com chaves em disco efêmero).
3. **Owner/Organization/Project**: com o plugin pessoal, o usuário preenche **todos** os campos (spec 2.3). Para não virar trabalho repetitivo, o modal pré-preenche os campos **não sensíveis** com o valor global.
4. **Efeito colateral bom**: com token pessoal, o autor do PR no GitHub passa a ser a própria pessoa (hoje é sempre a conta do token global).
5. **Skill `gerar-prmake`** usa a api-key do usuário → herda a integração pessoal dele automaticamente.

---

## 2. Decisões (default adotado — confirmar)

| # | Tema | Default adotado no plano |
|---|---|---|
| D1 | Valor efetivo de plugin pessoal | Só o valor **do usuário** — sem fallback para o global (senão todo mundo continuaria usando o token compartilhado). O global serve de "modelo" dos campos. |
| D2 | Quando está "configurado" | Todos os campos do plugin preenchidos pelo usuário. |
| D3 | Campos sensíveis | Chave contendo `token`, `secret`, `password`, `apikey`, `pat` ou terminando em `key` (sem distinção de maiúsculas). Gravados **criptografados** (AES-GCM, chave em `UserIntegrations:EncryptionKey` — Secret Manager em prod); a API devolve só `hasValue`; o front mostra "••••••" com opção de trocar. |
| D4 | Bloqueio (spec 4) | Backend: `403 { error, code: "PERSONAL_INTEGRATION_REQUIRED", plugins: [...] }` em qualquer operação GitHub/Azure sem configuração. Front: a tela de PR inteira fica bloqueada (com botão "Configurar minhas integrações") enquanto houver plugin pessoal pendente; demais telas liberadas. |
| D5 | Plugin marcado como pessoal mas ainda sem suporte no código (ex.: IA) | Aparece no modal para o usuário preencher, mas o código continua lendo o global até ser adaptado (log de aviso). |
| D6 | Plugin desmarcado/excluído | Dados do usuário **mantidos** no banco, invisíveis e não lidos; remarcar reativa. |
| D7 | Pré-preenchimento no modal | Campos não sensíveis vêm com o valor global como sugestão; sensíveis vêm vazios. |
| D8 | `get-all-by-id` de plugin pessoal | Devolve os valores **do usuário** (sensíveis mascarados) — telas que leem campos de config (ex.: nomes de campos do Azure na IA) continuam funcionando. |
| D9 | Transição | Nenhum plugin nasce pessoal (`IsPersonal = false`): nada muda até o admin marcar. Para GitHub/Azure pessoais, o admin **deve limpar o token global** depois que o time configurar. |

---

## 3. Contrato de API

Base `/api/v1`, autenticação atual (x-api-key).

```http
# Admin: flag no plugin (create/update existentes)
PluginRequest/PluginRespose ganham  "isPersonal": true|false

# Usuário: minhas integrações (só plugins pessoais ativos)
GET  /UserIntegration
→ 200 [{
    "pluginId": 12, "description": "Github Configurations", "configured": false,
    "fields": [
      { "key": "Token", "value": null, "hasValue": false, "sensitive": true },
      { "key": "Owner", "value": "electradv", "hasValue": true, "sensitive": false, "suggested": true }
    ],
    "updatedAt": null
}]

PUT  /UserIntegration/{pluginId}
{ "values": { "Token": "ghp_...", "Owner": "electradv" } }
   # sensível omitido ou null = mantém o valor salvo; "" = limpa
→ 200 (mesmo item do GET)   → 400 { error } (chave desconhecida etc.)

GET  /UserIntegration/status
→ 200 { "ready": false, "pending": [{ "pluginId": 12, "description": "Github Configurations" }] }

# Qualquer rota que use GitHub/Azure sem configuração pessoal:
→ 403 { "error": "Configure suas integrações pessoais: Github Configurations", "code": "PERSONAL_INTEGRATION_REQUIRED", "plugins": ["Github Configurations"] }
```

---

## 4. Fases

```
Onda 1:  B1 (modelo)      B2 (criptografia)      F1 (flag no admin)
Onda 2:  B3 (aplicação + cache + endpoints; B1, B2)    F2 (modal Minhas integrações; contrato)
Onda 3:  B4 (GitHub/Azure usam o pessoal + bloqueio; B3)   F3 (bloqueio no front; F2)
Onda 4:  Q1 (integração)
```

### B1 — Modelo e migração
**Depende de:** — · **Spec:** 1, 6, 7
- [x] `Plugin.IsPersonal` (+ `SetPersonal`), `PluginRequest`/`PluginRespose.IsPersonal`, `UpdateConfiguration` grava a flag, `get-all` devolve.
- [x] Entidade `UserPluginConfiguration` (PluginId FK, UserExternalId, Options, auditoria) + `DbSet` + índice único `(PluginId, UserExternalId)`; FK sem cascade físico (plugin é soft delete).
- [x] Migração `AddUserPluginConfigurations` (coluna com default `false` + tabela). Gerar com a factory temporária (ver memória `dev-db-is-prod`); ensaiar no MySQL local.

### B2 — Proteção de segredos
**Depende de:** — · **Spec:** 6 (+ ponto 1.5.1)
- [x] `ISecretProtector` (AES-GCM, chave de 32 bytes em Base64 em `UserIntegrations:EncryptionKey`); formato versionado (`v1:` + nonce + tag + cifra).
- [x] Sem chave configurada: API sobe, mas salvar integração pessoal → 500 com mensagem clara (nunca grava em texto puro).
- [x] `SensitiveFieldPolicy` (regra do D3).
- [x] Teste descartável: ida e volta, adulteração detectada, chave errada falha.

### B3 — Aplicação, cache e endpoints
**Depende de:** B1, B2 · **Spec:** 2.1–2.3, 5, 7, 8, 9
- [ ] `IUserPluginConfigurationApplication`: listar (pessoais ativos, campos = chaves do global, sensíveis sem valor, sugestões D7), salvar (valida chaves, mantém sensível omitido, criptografa), status.
- [ ] Cache por usuário com versão global (seção 1.4); `PluginApplication` incrementa a versão em create/update/delete.
- [ ] `IPluginCacheManager.GetEffectiveConfiguration(name)` + `PersonalIntegrationRequiredException` (usuário via `IHttpContextAccessor`, claim `ExternalId`).
- [ ] `UserIntegrationController` (`GET`, `PUT {pluginId}`, `GET status`), autenticado.
- [ ] `get-all-by-id` de plugin pessoal → valores do usuário mascarados (D8).
- [ ] Mapear `PersonalIntegrationRequiredException` → 403 com `code` (filtro/middleware).

### B4 — GitHub/Azure com o token pessoal + bloqueio
**Depende de:** B3 · **Spec:** 3, 4
- [ ] `GitHubService`: configuração lida por operação via `GetEffectiveConfiguration`; cliente Octokit criado sob demanda com o token do usuário; caches de repositórios/status passam a ser **por usuário** (repos visíveis dependem do token).
- [ ] `AzureService` + `HttpClient` "AzureDevOps": PAT por requisição (header montado no service).
- [ ] Todas as rotas GitHub/Azure/PR-GitHub devolvem o 403 do D4 sem configuração.
- [ ] Teste descartável: plugin comum (lê global), pessoal configurado (lê do usuário), pessoal pendente (403), plugin desmarcado (volta ao global).

### F1 — Flag "Uso pessoal" no admin de plugins
**Depende de:** contrato · **Spec:** 1
- [x] Toggle "Uso pessoal" no `dialogEdit` com tooltip: "Configurações marcadas como de uso pessoal precisam ser preenchidas por cada usuário (em Minhas integrações) para usar a aplicação."
- [x] Indicação visual (badge) na lista de plugins.

### F2 — "Minhas integrações"
**Depende de:** contrato · **Spec:** 2, 2.1–2.3
- [ ] Item "Minhas integrações" no menu do usuário (`top-menu`), com indicador quando há pendência.
- [ ] Modal: um bloco por plugin pessoal (nome, status configurado/pendente, campos dinâmicos); sensíveis como senha com "••••••"/"Alterar"; salvar por plugin.
- [ ] `UserIntegrationService` (signals): lista, status, `refresh()` após salvar.

### F3 — Bloqueio no front
**Depende de:** F2 · **Spec:** 4
- [ ] Tela de PR bloqueada (estado vazio com explicação + botão que abre o modal) enquanto `status.ready = false`; desbloqueia sozinha ao salvar.
- [ ] Interceptor: 403 `PERSONAL_INTEGRATION_REQUIRED` → snackbar com ação "Configurar" (abre o modal).

### Q1 — Integração e publicação
**Depende de:** todas
- [ ] Configurar `UserIntegrations:EncryptionKey` (local e Secret Manager/Cloud Run).
- [ ] Aplicar migração (usuário), marcar GitHub/Azure como pessoais, cada um configurar, **limpar o token global** (D9).
- [ ] Regressão da 0001 (abrir PR, status, IA, handover) com token pessoal.

---

## 5. Como executar
Mesmo protocolo da 0001: reservar a fase em `status.md` (🟡 + responsável + commit só do status), trabalhar em `feature/0002`, ao concluir marcar ✅ com commits e notas de handoff. Commits sempre com `git commit -- <arquivos>` (há arquivos do usuário em stage).
