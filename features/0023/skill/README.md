# Skill analisar-bug — cópia versionada (feature 0023)

A skill vive fora dos repositórios, em `~/.claude/skills/analisar-bug/` (nível do usuário). Esta pasta guarda
a versão da 0023 para revisão e histórico:

- `SKILL.md` — fluxo com o plano de execução no PRMake (seção *Plano de execução no PRMake*).
- `scripts/prmake-plan.sh` — cliente da API `api/v1/ExecutionPlan` (bash + curl + jq, compatível com o bash 3.2 do macOS).

Para instalar/atualizar em outra máquina: copiar os dois arquivos para `~/.claude/skills/analisar-bug/`
(os demais scripts da skill — `bug-fetch.sh`, `card-init.sh`, `cognito-query.sh`, `sql-query.*`,
`revamp-repos.sh` — não mudaram).

Antes do deploy do backend da 0023, o `start` responde `PLANO INDISPONIVEL` (404) e a skill segue como antes.
