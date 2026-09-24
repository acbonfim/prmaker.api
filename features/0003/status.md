# Feature 0003 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0003` em `prform.api` (a partir de `master` com 0001 e 0002). Front: `feature/0003` criada, sem mudanças previstas.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | `ClaudeService` com o SDK oficial | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | abe503b |
| B2 | IA resolvida por requisição + chave pessoal (0002) | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 1072fe0 |
| Q1 | Configuração do plugin, teste real e publicação | ambos | B1, B2 | 2 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Modelo | ✅ confirmada | **Sempre o campo `Model` do plugin "Claude Plugin"** (o usuário/admin troca lá, sem deploy). `claude-haiku-4-5` só como padrão quando o campo estiver vazio. |
| D2 | SDK oficial `Anthropic` x HTTP cru | ✅ confirmada | Recomendação: SDK (retries/erros tipados). Os demais provedores seguem em HTTP cru. |
| D3 | Sugestões R3 (saída estruturada) e R4 (prompt no backend) | a confirmar | Fora desta feature; viram features próprias se aprovadas. |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### B1 — `ClaudeService` com o SDK oficial ✅
- Pacote NuGet **`Anthropic` 12.50.0** (nuget.org) no `solvace.ai.application`. Obs.: o restore local falhou no feed privado da CodeArtifact (credencial vencida, 401) — usei `dotnet restore --ignore-failed-sources`; o build do CI/Docker usa o nuget.org normalmente.
- `ClaudeOptions`: `Model` (vazio → `claude-haiku-4-5`), `BaseUrl` (vazio → `https://api.anthropic.com`; o valor antigo `…/v1/messages` é normalizado), `SystemInstruction`, `MaxOutputTokens` (8000), `TimeoutSeconds` (120), `MaxRetries` (3), `Effort` (opcional; low/medium/high/max).
- `GenerateContentAsync`: `client.Messages.Create` com a mensagem do usuário = prompt pronto; **sem `temperature`/`top_p`** (rejeitados nos modelos atuais); `system` só se houver; `output_config.effort` só se configurado (Haiku 4.5 não aceita effort). `HttpClient` do `IHttpClientFactory` (nomeado "Anthropic") para reaproveitar conexões; cliente do SDK criado por chamada porque a chave pode ser pessoal.
- Erros: SDK retenta 429/5xx/**529 overloaded** (com backoff) antes de falhar; exceções tipadas → mensagens em português (chave inválida → "confira em Minhas integrações"; 404 → aponta o campo Model; 400 → confira Model/Effort/MaxOutputTokens; sobrecarga/limite após as retentativas; conexão; timeout). `stop_reason` `max_tokens` → erro "aumente MaxOutputTokens" (a resposta cortada não é usada); `refusal` → erro. `TokensUsed` = entrada + saída.
- Teste descartável contra **API falsa local** (HttpListener): **20/20** — formato do request (x-api-key, anthropic-version, model, max_tokens, system presente/omitido, sem temperature, effort), BaseUrl antigo normalizado, Model/MaxOutputTokens do plugin, Model vazio → Haiku 4.5, 429 x2 → sucesso na 3ª, 529 → erro após 3 tentativas, 401 sem retentar, 404, max_tokens, refusal, validações locais sem chamada.

### B2 — IA resolvida por requisição + chave pessoal ✅
- `Services/PluginAIService.cs` é o `IAIService` registrado: a cada chamada resolve "AI Configurations" → `Provider` e "`{Provider} Plugin`" via `IPluginConfigurationResolver` (0002). Plugin de uso pessoal → ApiKey **do usuário** + campos fixos do global (ex.: `Model`); sem configuração → `PersonalIntegrationRequiredException` → 403 (o front da 0002 já mostra "Configurar").
- `AIServiceFactory.CreateService(provider, PluginConfiguration)` (antes recebia `Plugin`), mapeia também `MaxRetries` e `Effort`; não depende mais do `IPluginCacheManager`. Resolver o `IAIService` no DI não lança mais sem ApiKey (erro de construção de Gemini/OpenAI vira resposta com `Error`).
- Teste descartável (DI real com o `AddAIModule`, resolvedor, SQLite e API falsa): **7/7** — DI ok sem configuração; sem ApiKey pessoal → 403; dois usuários com as **próprias** chaves no header; `Model` fixo do plugin; **admin troca o `Model` no plugin e a próxima chamada já usa o novo, sem deploy**; sem Provider → erro claro.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0002 mergeada (#7 back, #3 front) e em produção; `feature/0003` criada a partir de `master` nos dois repos. Plano criado com análise e sugestões (spec 5 e 6). |
| 2026-09-24 | B1, B2 | Usuário aprovou (modelo configurável no plugin, SDK oficial). Iniciadas. |
| 2026-09-24 | B1, B2 | Concluídas (abe503b, 1072fe0). Testes: 20/20 (ClaudeService x API falsa) e 7/7 (resolução com chave pessoal). Falta a Q1 (usuário). |
| 2026-09-24 | Q1 | Teste do usuário: 400 "API key is not scoped to a workspace" (chave de organização). Campo opcional `WorkspaceId` no plugin → `MessageCreateParams.WorkspaceID` (cabeçalho `anthropic-workspace-id`) + mensagem orientando (bf0db85). Teste 23/23. |
