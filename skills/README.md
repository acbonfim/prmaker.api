# Skills do PRMake para o Claude Code

Fonte única das skills (feature 0024). Cada deploy da API publica o conteúdo desta pasta em
`GET /api/v1/Skills` (a imagem copia `skills/` para `/app/skills`); `~/.claude/skills` de cada pessoa é só a instalação.

| Pasta | O que é |
|---|---|
| `analisar-bug/`, `gerar-prmake/`, `gerar-handover/`, `prmake-timeline/` | as skills (`SKILL.md`, `scripts/`, `skill.json` com requisitos e setup) |
| `_tooling/install.sh` | instalador de um comando para macOS/Linux/Git Bash (servido em `/Skills/install.sh`); instala jq/unzip/python3 que faltarem |
| `_tooling/install.ps1` | instalador do Windows no PowerShell (servido em `/Skills/install.ps1`): winget para Git/jq/Python e roda o `install.sh` no Git Bash |
| `_tooling/prmake-skills.sh` | `install` · `update [--quiet] [--force]` · `status` (servido em `/Skills/tool`, instalado em `~/.claude/skills/.prmake/`) |

**Instalar** (o comando com a api-key está na tela *Skills* do PRMake):

```bash
# macOS / Linux
curl -fsSL -H "x-api-key: <api-key>" https://api.softhouse.app.br/api/v1/Skills/install.sh | PRMAKE_TOKEN=<api-key> bash
```

```powershell
# Windows (PowerShell)
$env:PRMAKE_TOKEN='<api-key>'; irm -Headers @{'x-api-key'=$env:PRMAKE_TOKEN} https://api.softhouse.app.br/api/v1/Skills/install.ps1 | iex
```

Windows (0035): as skills rodam no Git Bash (o shell do Claude Code). A ferramenta cria em `~/bin` os atalhos `python3`
(o da Microsoft Store não roda) e `jq` (sem CRLF), põe `~/bin` e `PYTHONUTF8=1` no `~/.bashrc`, usa `setup.windows` do
`skill.json` (venv em `.venv/Scripts`) e registra o hook com `"shell": "powershell"` chamando o Git Bash pelo caminho
absoluto. `rsync`/`unzip` são opcionais em todos os sistemas (fallback: cp/find e bsdtar ou `python3 -m zipfile`).

**Atualização automática**: o instalador coloca um hook `SessionStart` em `~/.claude/settings.json` que roda
`prmake-skills.sh update --quiet` a cada sessão do Claude Code (só baixa o que mudou; sem rede, não faz nada).
Arquivo alterado à mão numa skill instalada não é sobrescrito — `prmake-skills.sh status` mostra, `update --force <skill>` substitui.

**Mudar uma skill** = mudar aqui e fazer o deploy da API. A versão de cada skill é o hash do conteúdo (muda sozinha).
`skill.json`: `requires` (comandos necessários) e `setup` (`check`/`run`, ex.: o `.venv` do `python-tds` da `analisar-bug`).

**Executor e MCP (0039)**: `prmake-skills.sh agent install` baixa o executor (`prmake-agent`, binário publicado pela API em
`GET /api/v1/ExecutionWorker/agent/<rid>`), registra a máquina e liga o serviço do usuário — o PRMake passa a rodar a
`analisar-bug` pela tela ("Analisar com Claude", respostas, "Continuar") sem terminal. O instalador também registra no
Claude Code o MCP remoto do PRMake (`<api>/mcp`, header `x-api-key`); `prmake-skills.sh mcp` refaz, `mcp remove` tira.
Instalar já com o executor: `... | PRMAKE_TOKEN=<api-key> PRMAKE_AGENT=1 bash`. Fonte do executor: `tools/Cime.ExecutionAgent`.
