# Feature 0002 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0002` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend), a partir de `master` (com a 0001 mergeada).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo e migração (IsPersonal + UserPluginConfigurations) | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 9156545 |
| B2 | Proteção de segredos (AES-GCM) | back | — | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | d726d29 |
| F1 | Flag "Uso pessoal" no admin de plugins | front | contrato | 1 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 3eaafa1 |
| B3 | Aplicação, cache por usuário e endpoints | back | B1, B2 | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 23854f5 |
| F2 | Modal "Minhas integrações" | front | contrato | 2 | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front a757d00 |
| B4 | GitHub/Azure com token pessoal + bloqueio | back | B3 | 3 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F3 | Bloqueio no front | front | F2 | 3 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| Q1 | Integração, chave de criptografia e publicação | ambos | todas | 4 | ⬜ | | | | |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | ✅ confirmada (default) | sem fallback para o global |
| D2 | ✅ confirmada (default) | todos os campos preenchidos |
| D3 | ✅ confirmada (default) | sensíveis criptografados e nunca devolvidos |
| D4 | ✅ confirmada (default) | 403 com código; tela de PR bloqueada |
| D5 | ✅ confirmada (default) | |
| D6 | ✅ confirmada (default) | |
| D7 | ✅ confirmada (default) | |
| D8 | ✅ confirmada (default) | |
| D9 | ✅ confirmada (default) | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

### B1 — Modelo e migração ✅
- `Plugin.IsPersonal` (+ `SetPersonal`, default `false` no banco); `PluginRequest`/`PluginRespose.IsPersonal`; `UpdateConfiguration` grava a flag; `get-all` e `get-all-by-id` devolvem.
- `Entities/UserPluginConfiguration.cs` (tabela `UserPluginConfigurations`: `PluginId` FK **Restrict** — plugin é soft delete —, `UserExternalId` char(36), `Options` longtext = JSON `{ chave: valor }`, auditoria); índices `(PluginId, UserExternalId)` único e `UserExternalId`.
- Migração `20260924052414_AddUserPluginConfigurations` (aditiva). Ensaio no MySQL 8 local: up → plugin existente com `IsPersonal = 0` e tabela criada; down → coluna/tabela removidas, dados intactos; up de novo ok.
- ⚠️ O `PUT update-configuration` grava `IsPersonal` a partir do corpo (como já faz com `AdminOnly`): quem chamar sem o campo zera a flag. Os dois chamadores do front mandam o plugin inteiro (vindo do `get-all`), então preservam.

### B2 — Proteção de segredos ✅
- `solvace.prform.application/Security/`: `ISecretProtector` + `AesGcmSecretProtector` (AES-256-GCM; `enc:v1:` + Base64(nonce 12 | tag 16 | cifra); **contexto como dado associado** — use `"{pluginId}:{userExternalId}"` na B3, assim a cifra de um usuário não serve em outro registro); `UserIntegrationOptions` (`UserIntegrations:EncryptionKey`, 32 bytes Base64 — `openssl rand -base64 32`); `SensitiveFieldPolicy.IsSensitive(key)`.
- Registrados no `Program.cs` (`Configure<UserIntegrationOptions>` + `AddSingleton<ISecretProtector, AesGcmSecretProtector>`).
- Sem chave: `IsConfigured = false` e `Protect` lança `InvalidOperationException` com mensagem clara (nunca grava em texto puro). Chave em formato inválido: exceção ao resolver o serviço.
- **Desvio do D3**: sensível por **palavra** do nome (Token, Secret, Password, Passwd, Pwd, ApiKey/Api+Key, Pat, ou terminando em Key) — "contém pat" como substring marcaria `RootCauseFieldPath` do Azure. `PersonalAccessToken` continua sensível (Token).
- Teste descartável: 26/26 (ida e volta, nonce aleatório, contexto/adulteração/chave errada falham, sem chave recusa, regra de campos com os nomes reais dos plugins).

### F1 — Flag "Uso pessoal" no admin ✅ (repo front)
- `dialogEdit`: toggle "Uso pessoal" + ícone de info com o tooltip da spec; `PluginData.isPersonal`. Lista de plugins: badge "Uso pessoal".
- `ng build` ok. Não testado no navegador.

### B3 — Aplicação, cache por usuário e endpoints ✅
- **Endpoints** (contrato §3): `GET /UserIntegration`, `PUT /UserIntegration/{pluginId}` (`{ values }`; omitido/null = mantém, `""` = limpa; chave fora do modelo → 400; sem chave de criptografia e com segredo → 503), `GET /UserIntegration/status`. `UserIntegrationController` `[Authorize]`, usuário pela claim `ExternalId`.
- **`solvace.prform.application/UserIntegrations/`**: `UserPluginConfigurationApplication` (lista/salva/status, `IsConfigured` = D2, sugestão D7), `PluginConfigurationResolver` e `PersonalIntegrationRequiredException` (`Code = "PERSONAL_INTEGRATION_REQUIRED"`, `Plugins`).
- **Desvio 1 (plano §1.3)**: em vez de estender o `IPluginCacheManager` (singleton), criei o serviço scoped **`IPluginConfigurationResolver.GetEffectiveConfigurationAsync(pluginName)`** — devolve o mesmo `PluginConfiguration` de hoje (as extensões `GetConfigurationValue` continuam valendo): plugin comum → global; pessoal → valores do usuário ou exceção. **É isso que a B4 deve usar no GitHub/Azure.**
- **Desvio 2 (cache)**: chave `plugins:user:{versão}:{externalId}` com **30 min** (não 24 h): com várias instâncias no Cloud Run, o que o usuário salva numa só chega às outras quando o cache expira. Para não bloquear quem acabou de configurar, o resolvedor **relê do banco** antes de lançar o 403. `IPluginCacheManager.GetConfigurationVersion()` incrementa a cada `RefreshPluginsAsync`.
- **403**: `PersonalIntegrationExceptionFilter` (MVC, registrado via `Configure<MvcOptions>`) → `{ error, code, plugins }`. ⚠️ Para a B4: exceções lançadas **dentro** de `try/catch (Exception)` dos services seriam engolidas — resolver a configuração **antes** dos blocos try.
- **`get-all-by-id` de plugin pessoal (D8)**: valores do usuário (segredos `********` se salvos; campos não salvos vazios — sem sugestão, coerente com D1).
- **Segredo inválido** (chave trocada, cifra de outro usuário): tratado como não preenchido + log de aviso.
- **Validação**: build ok; teste descartável com DI real (SQLite, `PluginCacheManager`/`CacheService`, `HttpContext` com claim) — 23/23: listagem só de pessoais ativos, sugestão, criptografia no banco, manter/limpar segredo, resolver (pessoal/comum/sem config/sem usuário), cifra copiada entre usuários rejeitada, desmarcar volta ao global e mantém os dados, versão global, sem chave de criptografia.

### F2 — "Minhas integrações" ✅ (repo front)
- `services/user-integration.service.ts` (signals `integrations`, `status`, `hasPending`; `loadStatus`, `loadIntegrations`, `save`).
- `components/my-integrations-dialog/`: um bloco por plugin pessoal (nome, chip Configurado/Pendente, campos dinâmicos na ordem do admin). Segredo: input de senha vazio com "•••••••• salvo — digite para trocar", mostrar/ocultar o que digitou e apagar o salvo; só é enviado se digitado (ou `""` ao apagar). Não sensível: pré-preenchido com o valor do usuário ou a sugestão ("Sugestão da configuração geral"). Salvar por plugin; **pode salvar parcial** (o chip mostra se ainda está pendente).
- Menu do usuário: item **"Minhas integrações"** (com "!" quando pendente) e bolinha âmbar ao lado do nome; `top-menu` chama `loadStatus()` ao iniciar.
- `ng build` ok. Não testado no navegador.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0001 mergeada (PRs #5 back / #2 front); `master` local atualizada nos dois repos; branches `feature/0002` criadas a partir dela. Plano criado. |
| 2026-09-24 | B1, B2, F1 | Usuário aprovou o plano com os defaults D1–D9. Onda 1 iniciada. |
| 2026-09-24 | B1, B2, F1 | Concluídas (9156545, d726d29, front 3eaafa1). Onda 1 fechada; liberadas B3 e F2. |
| 2026-09-24 | B3, F2 | Iniciadas (onda 2). |
| 2026-09-24 | B3, F2 | Concluídas (23854f5, front a757d00). Onda 2 fechada; liberadas B4 e F3. |
| 2026-09-24 | B4, F3 | Iniciadas (onda 3). |
