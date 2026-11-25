# 0092 — Contrato (back ↔ front ↔ skills)

## Configuração (Skills Configurations)
`ValidationEnvironments` (JSON, semeado por migração idempotente; o admin edita em Configurações):
```json
{
  "environments": [
    { "key": "dev", "label": "Desenvolvimento", "url": "https://devsp.solvacelabs.com/", "user": "admin@ewcm.com.br",
      "kind": "dev", "database": { "host": "dev", "global": "DB_SOLV_DEV_GLOBAL" } },
    { "key": "demo", "label": "Demonstração", "url": "https://demo.solvacelabs.com/", "readOnly": true, ... }
  ],
  "plantAdminPattern": "admin{plant}@solvacelabs.com",
  "corporate": { "host": "corporate", "database": "CORPORATE_SOLVACE_DB", "table": "TB_WCM_ENVIRONMENT" },
  "defaults": { "bug": ["dev", "qa"], "user-story": ["dev", "qa"] }
}
```
`readOnly: true` = o navegador da validação bloqueia gravações (POST/PUT/PATCH/DELETE fora da lista de leitura).

## Itens de trabalho
- Tipo de sub-tarefa `env-validation` (label "Validar no ambiente", ícone `screenshot_monitor`, executor `claude`),
  params: `environment` (chave ou URL), `plant` (sigla da planta, opcional), `user` (e-mail, opcional), `scenario`
  (textarea: o que validar), `expected` (textarea: critério).
- Tipo de item `validation` ("Validação", ícone `verified`).
- Ferramenta (modelo) `validar-em-ambiente` (grupo "QA e fechamento"): ask `environments` (input `environments`,
  opções = ambientes da configuração como `{value, label}`, várias separadas por vírgula, aceita outro nome/URL), `plant`,
  `user`, `scenario`; uma sub-tarefa `env-validation` **por ambiente** (`"repeat": "{ask:environments}"`).

### `repeat` nos modelos
Sub-tarefa do modelo com `repeat` (texto com marcadores, normalmente `{ask:<key>}`): o valor renderizado é dividido por
vírgula/ponto-e-vírgula; sai uma sub-tarefa por valor (vazio → nenhuma). Dentro dela, `{each}` = o valor e
`{eachLabel}` = o rótulo da opção do ask cujo valor bate (senão o próprio valor). O catálogo mantém o `repeat` cru na
sub-tarefa do modelo (a tela expande na prévia).

### Opções dos ambientes
`ask.options = "{validationEnvironments}"` → o catálogo devolve `[{value: "dev", label: "Desenvolvimento · devsp.solvacelabs.com"}, …]`
(rótulo com " · somente leitura" quando `readOnly`). Input `environments` não é lista fechada (aceita outro valor).

## Arquivos
Tipo de arquivo do item `evidence` (prints da validação) — `POST WorkItems/{n}/files` multipart com `kind=evidence` e
`taskNumber`. A tela mostra os prints da sub-tarefa como galeria (na ordem do nome: `01-…`, `02-…`).

## MCP
`prmake_env_validation(card, environments, plant?, user?, scenario?, expected?, execute?)` — cria o item pela ferramenta
`validar-em-ambiente` (mesma checagem de duplicidade do `prmake_tool_run`) e, com `execute=true`, põe na fila.
Liberado para o Especialista do caso.

## Máquina do usuário (skills)
- Senhas: `prmake-skills.sh env-credentials [list | set [ambiente] [usuario] | remove <ambiente> <usuario>]` →
  `~/.claude/solvace-env-credentials.json` (600; só terminal interativo; senha sem eco; nunca pelo chat).
- Skill `validar-ambiente` (venv com Playwright, Chrome do sistema): `env.sh envs|resolve|plants|admin|has-password`
  e o navegador `env.sh browser start|go|click|fill|select|press|wait|shot|snap|stop` + `env.sh evidence` (sobe os prints
  para a sub-tarefa).
