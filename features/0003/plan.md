# Feature 0003 — Claude como provedor de IA (plugin "Claude Plugin")

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0003` (backend) a partir de `master` (com 0001 e 0002). O front só entra se alguma fase pedir.

---

## 1. Análise

### 1.1 Como está hoje
- **`ClaudeService` é um esboço**: `GenerateContentAsync` devolve `"Claude ainda não implementado"`. `ClaudeOptions.Model` padrão é `claude-3-5-sonnet-20241022` (**aposentado**) e `BaseUrl` é o endpoint completo `…/v1/messages`.
- **Escolha do provedor** (`AddAIModule`): lê o plugin **"AI Configurations"** (`Provider`) e o plugin **"`{Provider} Plugin`"** (ex.: "Claude Plugin") via `GetCachedPluginByName` — **plugin global**, no construtor do DI. `AIServiceFactory.CreateService(providerName, Plugin)` monta as opções a partir de `ApiKey`, `BaseUrl`, `Model`, `SystemInstruction`, `ThinkingLevel`, `MaxOutputTokens`, `TimeoutSeconds`.
- ⚠️ Isso **não usa a 0002**: mesmo marcando "Claude Plugin" como *Uso pessoal*, a IA continuaria com o `ApiKey` global. E os construtores (ex.: `GeminiService`) lançam exceção sem ApiKey — o mesmo problema que o GitHub tinha antes da 0002/B4.
- **Fluxo de geração** (sem mudança nesta feature): o front monta **um prompt único** a partir do template `PromptBug`/`PromptUS` do plugin "AI Configurations" + card do DevOps + diffs de N repositórios (0001/F6) e chama `POST /AI/generate` com o texto; a resposta é texto livre e o front separa descrição e RCA pela tag `<RCA>…</RCA>`. Também usado no handover.
- Único consumidor no backend: `AIController.Generate`.

### 1.2 Claude na API da Anthropic (referência atual)
- **SDK oficial para C#**: pacote NuGet `Anthropic` (12.50.0 no nuget.org, que já é fonte do projeto). Traz retries automáticos (padrão 2 — 408/409/429/5xx, inclusive **529 overloaded**) e exceções tipadas (`AnthropicUnauthorizedException`, `AnthropicRateLimitException`, `Anthropic5xxException`, `AnthropicIOException`, base `AnthropicApiException`). Recomendação oficial: SDK em vez de HTTP cru.
- `max_tokens` é obrigatório; `stop_reason` precisa ser checado (`max_tokens` = texto cortado; `refusal` = recusado).
- **Parâmetros que mudaram**: `temperature`/`top_p` são **rejeitados (400)** nos modelos novos (Sonnet 5, Opus 5…) — não enviar. Thinking/effort variam por modelo (Haiku 4.5 **não** aceita `effort`).

---

## 2. Sugestões pedidas na spec

### 2.1 Modelo (spec 5) — decisão sua; recomendação: **Claude Haiku 4.5** (`claude-haiku-4-5`)

| Modelo | ID | Entrada / Saída (US$ por 1M tokens) | Contexto | Custo estimado por geração* |
|---|---|---|---|---|
| **Haiku 4.5** (recomendado) | `claude-haiku-4-5` | 1,00 / 5,00 | 200K | **≈ US$ 0,04** |
| Sonnet 5 (se a qualidade não bastar) | `claude-sonnet-5` | 2,00 / 10,00 | 1M | ≈ US$ 0,08 |
| Opus 5 (máxima qualidade) | `claude-opus-5` | 5,00 / 25,00 | 1M | ≈ US$ 0,19 |

\* Estimativa para uma geração típica: ~30K tokens de entrada (card + timeline + diffs de 2–3 repositórios) e ~1,5K de saída. A tarefa é resumir/redigir a partir de texto fornecido, sem memória nem ferramentas — o perfil do Haiku. O `Model` fica no plugin, então trocar de modelo é só configuração. Se um card estourar 200K tokens (raro: os patches já são cortados em 15 kB por arquivo), Sonnet 5 tem 1M.

### 2.2 Abordagem (spec 6) — está correta no essencial; melhorias em ordem de retorno

| # | Sugestão | Por quê | Nesta feature? |
|---|---|---|---|
| R1 | **Chave pessoal via 0002** (resolver) e serviço de IA resolvido por requisição | É o que a spec 3 pede e evita a IA quebrar o DI sem ApiKey | **Sim (B2)** |
| R2 | **Instruções fixas no `system`** e dados no `user` separados por tags (`<card>`, `<timeline>`, `<diffs>`) | Modelos seguem melhor instruções no system e diferenciam instrução de dado (evita o diff "virar instrução") | Parcial: suporte a `SystemInstruction` no plugin (B1); reescrever os templates é configuração/front |
| R3 | **Saída estruturada** (JSON com `pullRequestDescription` e `rootCause`, via `output_config.format`, suportado no Haiku 4.5) em vez de separar pela tag `<RCA>` | Hoje, se o modelo esquecer/alterar a tag, o RCA vira "no need" | Não — muda o contrato `/AI/generate` e o front; feature própria |
| R4 | **Montar o prompt no backend** (front manda card + commits escolhidos; backend busca card, timeline e diffs) | Um lugar só para regras/limites de tamanho, prompt não fica exposto no front, habilita cache | Não — refatoração maior; feature própria |
| R5 | Cache de prompt | **Não compensa hoje**: cada geração tem contexto diferente e as instruções fixas ficam abaixo do mínimo cacheável do Haiku 4.5 (4.096 tokens) | Não |
| R6 | Batch API (50% mais barato) | Resposta assíncrona (minutos a horas) — incompatível com a tela interativa | Não |
| R7 | Fallback entre provedores (ex.: Gemini 503 → Claude) | Com o Claude funcionando, dá para cair num segundo provedor quando o primeiro estiver sobrecarregado | Não — opcional futuro |

---

## 3. Configuração do plugin "Claude Plugin" (spec 4)

| Campo | Obrigatório | Uso pessoal (0002) | Padrão | Observação |
|---|---|---|---|---|
| `ApiKey` | sim | **Usuário preenche** (sensível) | — | Chave da API da Anthropic de cada pessoa |
| `Model` | não | fixo | `claude-haiku-4-5` | Só IDs da tabela acima (sem sufixo de data) |
| `BaseUrl` | não | fixo | `https://api.anthropic.com` | Aceita o valor antigo `…/v1/messages` (normalizado) |
| `MaxOutputTokens` | não | fixo | `8000` | Teto da resposta; se estourar, a API avisa (`stop_reason = max_tokens`) |
| `TimeoutSeconds` | não | fixo | `120` | Por tentativa |
| `SystemInstruction` | não | fixo | — | Instruções fixas (R2) |
| `Effort` | não | fixo | — | `low`/`medium`/`high`; **não usar com Haiku 4.5** (a API rejeita) |
| `MaxRetries` | não | fixo | `3` | Retentativas automáticas do SDK (429/5xx/529) |
| `WorkspaceId` | só p/ chave de organização | fixo (ou do usuário, se cada um usar um workspace) | — | `wrkspc_…`. A API exige o cabeçalho `anthropic-workspace-id` quando a chave **não** foi criada dentro de um workspace; chave de workspace pode deixar vazio |

O plugin "AI Configurations" continua escolhendo o provedor (`Provider = Claude`) e guardando os templates `PromptBug`/`PromptUS`.

---

## 4. Fases

```
Onda 1:  B1 (ClaudeService com o SDK)      B2 (IA resolvida por requisição + chave pessoal)
Onda 2:  Q1 (configuração, teste real, publicação)
```

### B1 — `ClaudeService` com o SDK oficial
- [x] Pacote `Anthropic` no `solvace.ai.application`.
- [x] `ClaudeOptions`: padrões atuais (Model `claude-haiku-4-5`, BaseUrl `https://api.anthropic.com`, `MaxOutputTokens`, `TimeoutSeconds`, `MaxRetries`, `SystemInstruction`, `Effort`).
- [x] `GenerateContentAsync`: `Messages.Create` com `Model`, `MaxTokens`, `System` (se houver), prompt como mensagem do usuário, sem `temperature`; `Effort` só se configurado.
- [x] Resposta: junta os blocos de texto; `stop_reason` `max_tokens` → erro claro (resposta cortada) ; `refusal` → erro; `TokensUsed` = entrada + saída.
- [x] Erros tipados → mensagens em português (chave inválida, sem permissão, limite/sobrecarga após as retentativas, conexão, 400).
- [x] Teste descartável contra um servidor HTTP local falso (formato da requisição enviada, sucesso, 401, 429 com retry, 529, `max_tokens`).

### B2 — IA resolvida por requisição + chave pessoal
- [x] `IAIService` registrado como um serviço que, **a cada chamada**, resolve "AI Configurations" e "`{Provider} Plugin`" via `IPluginConfigurationResolver` (0002) — chave pessoal quando o plugin for de uso pessoal; sem configuração → 403 `PERSONAL_INTEGRATION_REQUIRED` (o front já trata).
- [x] `AIServiceFactory.CreateService(provider, PluginConfiguration)` (hoje recebe `Plugin`) + mapeamento dos campos novos.
- [x] Construtores dos provedores deixam de derrubar o DI (a falta de configuração vira erro na chamada).

### Q1 — Configuração e publicação (usuário)
- [ ] Plugin "Claude Plugin": `ApiKey` (Usuário preenche), `Model`, e demais campos da seção 3; marcar **Uso pessoal**.
- [ ] "AI Configurations": `Provider = Claude`.
- [ ] Cada pessoa cria a própria chave em console.anthropic.com e salva em Minhas integrações.
- [ ] Teste real: Gerar com IA (descrição + RCA) e handover.
