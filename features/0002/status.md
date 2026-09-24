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
| B3 | Aplicação, cache por usuário e endpoints | back | B1, B2 | 2 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F2 | Modal "Minhas integrações" | front | contrato | 2 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| B4 | GitHub/Azure com token pessoal + bloqueio | back | B3 | 3 | ⬜ | | | | |
| F3 | Bloqueio no front | front | F2 | 3 | ⬜ | | | | |
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

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0001 mergeada (PRs #5 back / #2 front); `master` local atualizada nos dois repos; branches `feature/0002` criadas a partir dela. Plano criado. |
| 2026-09-24 | B1, B2, F1 | Usuário aprovou o plano com os defaults D1–D9. Onda 1 iniciada. |
| 2026-09-24 | B1, B2, F1 | Concluídas (9156545, d726d29, front 3eaafa1). Onda 1 fechada; liberadas B3 e F2. |
| 2026-09-24 | B3, F2 | Iniciadas (onda 2). |
