# 0092 — Status

| Fase | Status | Commits | Notas |
|---|---|---|---|
| B1 back (config, tipos, renderizador, migração, MCP) | concluída | back ef61b84 | `ValidationEnvironments`, `env-validation`, `validation`, `evidence`, `repeat`/`{each}`/`{eachLabel}`, `{validationEnvironments}`/`{validationDefaults}`, migração `SeedEnvValidation0092`, MCP `prmake_env_validation` (liberado ao especialista) |
| B2 skills (credenciais, validar-ambiente, guias) | concluída | back 4f211d0 | `prmake-skills.sh env-credentials`, skill `validar-ambiente` (env.sh + env_browser.py), guias executar/especialista/analisar-bug/analisar-us, executor 1.3.1 (nega ler o arquivo de senhas) |
| F1 front (input, prévia, evidências) | concluída | front c9bff1b | input `environments` (chips + outro), prévia com `repeat`, arquivo `evidence`, galeria de prints na sub-tarefa |
| Q1 integração | concluída | | ver abaixo |

## Q1 (2026-11-08)
- `dotnet build Solvace.Master.sln` ok; testes: PullRequests 380, ExecutionPlans 199, executor 29, SkillTests 49 — todos ok.
- Migração rodada num banco isolado (cópia dos plugins do harness): Up adiciona chave/tipo/tipo de item/modelo, 2ª
  execução não muda nada (idempotente), Down volta exatamente ao estado anterior (md5 igual).
- Navegador (`env_browser.py`): sem senha → exit 4 com o comando; senha errada (usuário inexistente no DEV) → exit 3
  com a mensagem do app ("username or password is invalid") em ~10 s; página local em modo somente leitura: busca
  (POST de leitura) passou, "Salvar" bloqueado, servidor recebeu só a busca; prints numerados + `shots.json`.
- `env.sh`: `envs`, `resolve` (configuração, URL, corporativo, ambíguo lista as URLs), `plants dev`, `admin dev MIL`
  (admin da planta achado no banco), `has-password`.
- `env-credentials set` testado com TTY simulado (senha gravada certa, arquivo 600); sem TTY recusa.
- Front: `ng build` dev ok e 76 specs da área ok (agente F1).

## Pendências
- Primeiro teste real (usuário): gravar a senha (`env-credentials set qa admin@ewcm.com.br`), criar o item pela
  ferramenta/`/validar-em-ambiente` e executar — login real nos ambientes não foi testado aqui (sem senha nesta sessão).
- Login com SSO obrigatório (ex.: clientes com só SSO) não é suportado pelo navegador sem janela: use um usuário com
  senha (admin da planta).

## Log
- 2026-11-08: spec/contrato/plano. Login dos ambientes: Angular `myworkspace/sign-in` com `#username-field`,
  `#password-field`, `#signin-button` (botão "log in with Solvace.com" = SSO). Catálogo de ambientes no corporativo:
  `CORPORATE_SOLVACE_DB.TB_WCM_ENVIRONMENT` (nome, URL, `CATALOG_GLOBAL`, SSO). Plantas no global:
  `TB_WCM_SITE` (`SITE_ACRONYM`, `INSTANCE_NAME` = banco local). Usuários: `TB_WCM_USER` (`USER_EMAIL`, `SITE_ID`, `ACTIVE`).
