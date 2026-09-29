# Skills do PRMake para o Claude Code

Fonte única das skills (feature 0024). Cada deploy da API publica o conteúdo desta pasta em
`GET /api/v1/Skills` (a imagem copia `skills/` para `/app/skills`); `~/.claude/skills` de cada pessoa é só a instalação.

| Pasta | O que é |
|---|---|
| `analisar-bug/`, `gerar-prmake/`, `gerar-handover/`, `prmake-timeline/` | as skills (`SKILL.md`, `scripts/`, `skill.json` com requisitos e setup) |
| `_tooling/install.sh` | instalador de um comando (servido em `/Skills/install.sh`) |
| `_tooling/prmake-skills.sh` | `install` · `update [--quiet] [--force]` · `status` (servido em `/Skills/tool`, instalado em `~/.claude/skills/.prmake/`) |

**Instalar** (o comando com a api-key está na tela *Skills* do PRMake):

```bash
curl -fsSL -H "x-api-key: <api-key>" https://api.softhouse.app.br/api/v1/Skills/install.sh | PRMAKE_TOKEN=<api-key> bash
```

**Atualização automática**: o instalador coloca um hook `SessionStart` em `~/.claude/settings.json` que roda
`prmake-skills.sh update --quiet` a cada sessão do Claude Code (só baixa o que mudou; sem rede, não faz nada).
Arquivo alterado à mão numa skill instalada não é sobrescrito — `prmake-skills.sh status` mostra, `update --force <skill>` substitui.

**Mudar uma skill** = mudar aqui e fazer o deploy da API. A versão de cada skill é o hash do conteúdo (muda sozinha).
`skill.json`: `requires` (comandos necessários) e `setup` (`check`/`run`, ex.: o `.venv` do `python-tds` da `analisar-bug`).
