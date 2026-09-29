# Feature 0030 — Tudo configurável no PRMake (sem deploy para mudar regra)

## O que estava fixo
- **Backend**: nomes dos campos de classificação, Remaining/Original/Completed e root cause (alertas) em `DevOpsActionsService`/`AzureService`; opções de classificação só no código (a chave `BugClassificationPresets` não existia em produção).
- **Skills**: fluxo por área (`bug-fetch.sh`), tabela de branches por repositório/fluxo, `hotfix/<card>`, `AB#<card> …` (commit/título), `edv-solvace` como padrão, "Freshservice", prompts lidos do plugin id=3 (global).

## Decisões
- **"AzureDevOps Configurations"** ganha `FieldResolutionType`, `FieldGeneralClassification`, `FieldClassification`, `FieldRemainingWork`, `FieldOriginalEstimate`, `FieldCompletedWork` (fixos do admin, ocultos em Minhas integrações). `IAzureService.GetFieldNamesAsync` resolve com os padrões; alertas, estimativa, zerar Remaining e classificação usam esses nomes.
- **"AI Configurations"** ganha `BugClassificationPresets` com as 14 opções (o código fica só como reserva; `classifications` também cai na reserva quando o usuário não configurou o plugin).
- **Plugin novo "Skills Configurations"** (global): `BranchFlowByArea`, `BranchStrategy` (tipos de repositório por glob + regras por fluxo/tipo: base, `baseOptions`/`askBase`, PRs com sufixo/origem/destino), `BranchNamePattern`, `CommitMessagePattern`, `PrTitlePattern`, `DefaultRepository`, `TicketSystem`.
- **Migração `SeedSkillsConfigurations`** (DefaultContext): cria o plugin e acrescenta só as chaves que faltam (valores já editados pelo admin são preservados; idempotente); rótulos em `FieldSettings`. `Down` remove as chaves e o plugin.
- **`GET /Skills/config`**: configuração efetiva do usuário — `settings` (JSON já como objeto), `prompts` (bug, userStory, summary) e `fields`.
- **analisar-bug**: `prmake-plan.sh settings` e `branches <card> <repo> [--flow] [--base]` (calcula tipo, fluxo pela área, base, branches, cherry-picks, títulos e imprime os comandos; exit 3 = perguntar); `open-pr` usa o `PrTitlePattern`; `bug-fetch.sh` calcula o fluxo pelo `BranchFlowByArea`; a SKILL.md não tem mais regras fixas — manda seguir o `branches`/`settings`.
- **gerar-prmake**: prompts pelo `/Skills/config` (id=3 como reserva), repositório padrão e título do PR pela configuração.
- **Front**: no editor de configurações de plugin, valores longos/JSON viram área de texto monoespaçada com validação de JSON (Salvar desabilitado com JSON inválido).
