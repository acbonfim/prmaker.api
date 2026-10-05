# Skills do PRMake para o Claude Code

Fonte única das skills (feature 0024). Cada deploy da API publica o conteúdo desta pasta em
`GET /api/v1/Skills` (a imagem copia `skills/` para `/app/skills`); `~/.claude/skills` de cada pessoa é só a instalação.

| Pasta | O que é |
|---|---|
| `analisar-bug/`, `gerar-prmake/`, `gerar-handover/`, `prmake-timeline/` | as skills (`SKILL.md`, `scripts/`, `skill.json` com requisitos e setup) |
| `_tooling/install.sh` | instalador de um comando para macOS/Linux/Git Bash (servido em `/Skills/install.sh`); instala jq/unzip/python3 que faltarem |
| `_tooling/install.ps1` | instalador do Windows no PowerShell (servido em `/Skills/install.ps1`): winget para Git/jq/Python e roda o `install.sh` no Git Bash |
| `_tooling/prmake-skills.sh` | `install` · `update [--quiet] [--force]` · `status` · `doctor` · `repos` (servido em `/Skills/tool`, instalado em `~/.claude/skills/.prmake/`) |

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
Quando o update pula uma skill por isso, o hook imprime um `ATENCAO: skills do PRMake NAO atualizadas — ...` no stdout (o stderr do hook
SessionStart não chega ao Claude), e o Claude avisa o usuário na sessão em vez de seguir com a versão velha.

**Mudar uma skill** = mudar aqui e fazer o deploy da API. A versão de cada skill é o hash do conteúdo (muda sozinha).
`skill.json`: `requires` (comandos necessários) e `setup` (`check`/`run`, ex.: o `.venv` do `python-tds` da `analisar-bug`).

**Executor e MCP (0039)**: `prmake-skills.sh agent install` baixa o executor (`prmake-agent`, binário publicado pela API em
`GET /api/v1/ExecutionWorker/agent/<rid>`), registra a máquina e liga o serviço do usuário — o PRMake passa a rodar a
`analisar-bug` pela tela ("Analisar com Claude", respostas, "Continuar") sem terminal. O instalador também registra no
Claude Code o MCP remoto do PRMake (`<api>/mcp`, header `x-api-key`); `prmake-skills.sh mcp` refaz, `mcp remove` tira.
Instalar já com o executor: `... | PRMAKE_TOKEN=<api-key> PRMAKE_AGENT=1 bash`. Fonte do executor: `tools/Cime.ExecutionAgent`.

**Repositórios da máquina (0048)**: cada pessoa clona onde quer — nada de caminho fixo. A ferramenta monta o mapa
`~/.prmake/repos.json` (nome do repositório pelo `remote origin` → pasta) procurando clones de trabalho dos repositórios
que casam com `BranchStrategy.repositories` (Skills Configurations) nas pastas comuns (`~/repos`, `~/source`, `~/dev`,
`~/projects`, `~/code`, `~/git`, `~/workspace`, `~/work`, `~/Documents` fora do macOS, `C:/repos`… no Windows) até 4
níveis, mais a própria home (1 nível). Ignora worktrees, ocultas, `node_modules`/`bin`/`obj`/`dist`/`packages`,
`.prmake-wt` e `kb-mirror`. Dois clones do mesmo repositório = ambíguo (ninguém escolhe sozinho).

```bash
bash ~/.claude/skills/.prmake/prmake-skills.sh repos                  # mostra o mapa, ambíguos e padrões sem clone
bash ~/.claude/skills/.prmake/prmake-skills.sh repos scan             # procura de novo (no terminal: confirma/corrige)
bash ~/.claude/skills/.prmake/prmake-skills.sh repos set <repo> <pasta>   # fixa à mão (valida o remote); a busca não mexe mais
bash ~/.claude/skills/.prmake/prmake-skills.sh repos path <repo>      # pasta (exit 2 = fora do mapa, 3 = ambíguo)
```

- `install` e `agent install` procuram e (com terminal) perguntam; quem já tinha instalado ganha o mapa no próximo
  `update` do hook, em segundo plano (marca `.repos-v1`), com um aviso de uma linha quando há pendência.
- `PRMAKE_REPOS_ROOTS` (pastas separadas por `:`) troca as raízes da busca; `PRMAKE_REPOS_SCAN_TIMEOUT` (60 s) o tempo
  máximo; `PRMAKE_HOME` a pasta do mapa. `EDV_SOLVACE_DIR`/`REVAMP_DIR` valem por cima do mapa.
- macOS: `Documents`/`Desktop`/`Downloads` ficam fora da busca padrão (pedem permissão do sistema, inclusive ao serviço
  do executor) — repositórios lá: `repos set` ou `PRMAKE_REPOS_ROOTS`.
- Usam o mapa: `revamp-repos.sh` (`list`/`where`/`grep`), `prmake-plan.sh branches`/`worktree`, `arch.sh stale` e o
  executor (pasta onde abre o Claude, `--add-dir` dos repositórios fora dela, "Meus executores" e o doctor).
