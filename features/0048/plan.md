# Plano — Feature 0048

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch `feature/0048` a partir de `origin/master` em `prform.api-0048` (API, ferramenta das skills, skills, executor) e
> `prform-app-0048` (front, só a F1).

| Fase | O quê | Depende de |
|---|---|---|
| B1 | Migração: regra `edv-solvace-api` → `integration-api` no `BranchStrategy` (+ fluxos), só se faltar | — |
| S1 | Ferramenta: `~/.prmake/repos.json`, `prmake-skills.sh repos` (show/scan/set/unset/path), install interativo, migração `.repos-v1` | — |
| E1 | Executor 1.0.7: lê o mapa (workspace, `--add-dir`), busca se faltar, `repoMap` no report, `doctor` | — |
| F1 | Front: "Meus executores" mostra o mapa (ambíguos/faltando com o comando) | — (formato fixado abaixo) |
| S2 | Skills: `revamp-repos.sh` pelo mapa; `branches` com a pasta real (exit 4); `arch.sh` pelo mapa | S1 |
| S3 | Textos: `analisar-bug` (SKILL.md, consultas, correção) — `where` pelo remote; ausente → `ask`; `repos set` | S2 |
| T1 | Teste local integrado | todas |

**Ondas**: 1 = B1, S1, E1, F1 (em paralelo) · 2 = S2 → S3 · 3 = T1.

---

## Contratos fixados (para as fases rodarem em paralelo)

### `~/.prmake/repos.json`
Formato da spec ("Formato do mapa"). Raiz: `PRMAKE_HOME` (padrão `~/.prmake`) — mesmo pai de `~/.prmake/cards`.
Leitores ignoram campos desconhecidos; `version` diferente de 1 → tratam como sem mapa.

### `prmake-skills.sh repos path <repo>` (consumido por S2 e E1)
- Saída: a pasta (uma linha) e exit 0.
- exit 2 = não está no mapa · exit 3 = ambíguo (candidatos em stderr, um por linha) · exit 1 = erro (sem `jq` etc.).
- Precedência aplicada aqui: variável (`EDV_SOLVACE_DIR` para `edv-solvace`; `REVAMP_DIR/<pasta cujo remote casa>`)
  → mapa (`manual`/`env` > `scan`) → padrão `~/repos/solvace/...` (se existir e o remote casar).
- Nome comparado sem diferenciar maiúsculas; aceita forma curta para revamp (`Users` → `revamp-Users`) quando única.

### `Capabilities` do report do executor (consumido por F1)
```json
{
  "os": "...",
  "repos": ["edv-solvace", "revamp_separado/Solvace.Users"],
  "repoMap": {
    "items": [{ "name": "revamp-Users", "path": "/Users/x/.../Solvace.Users", "kind": "revamp-backend",
                "branch": "master", "source": "scan", "confirmed": false }],
    "ambiguous": { "revamp-BOS": ["/a/revamp-BOS", "/b/BOS"] },
    "missing": ["edv-solvace-api"],
    "updatedAt": "2026-10-02T12:00:00-03:00"
  }
}
```
- `repos` continua igual (compatível com a tela antiga). `repoMap` ausente = executor antigo.
- `missing`: padrões de `BranchStrategy.repositories` sem nenhum clone no mapa (o `match` como está, ex.: `revamp-*`).
- O backend guarda `Capabilities` como `jsonb` e devolve como veio (`ExecutionWorker.Capabilities`) — **sem mudança no
  backend** para isso.

---

## B1 — Regra do `edv-solvace-api` (backend)
Arquivo: `CIME/modules/Solvace.PullRequests/src/solvace.prform.infra/Migrations/<ts>_SeedIntegrationApiBranchStrategy.cs`
(modelo: `20260930200000_SeedDevTestInQaConfiguration.cs`; `dotnet ef migrations add` contra o Postgres local).

- `BranchStrategy` é uma **string JSON** dentro de `PluginConfigurations.Options[0]` do plugin "Skills Configurations".
  No `Up`, em SQL: `(opts->0->>'BranchStrategy')::jsonb` → se `repositories` não tem item com `match` =
  `edv-solvace-api` (sem diferenciar maiúsculas), insere `{match:"edv-solvace-api", kind:"integration-api"}` **logo
  após** a regra `edv-solvace-apps` (ordem não importa hoje — `edv-solvace` é exato —, mas fica legível); para cada
  fluxo (`producao`, `release`) sem a chave `integration-api`, acrescenta
  `{askBase:true, baseOptions:["master","release-version"], prs:[{suffix:"", target:"{base}"}]}`; grava de volta com
  `jsonb_pretty(...)` como string (o formato que a tela edita).
- Valor inválido/vazio (admin quebrou o JSON) → não toca (`WHERE ... IS JSON`/bloco `DO` com tratamento).
- `Down`: remove a regra e as chaves `integration-api` só se forem exatamente as semeadas.
- Sem mudança no `SkillsConfigurationKeys`/tela (a chave já existe).

## S1 — Ferramenta das skills: mapa e comando `repos` (`skills/_tooling/prmake-skills.sh`, `install.ps1`)
- Funções novas (bloco "Windows/Git Bash (0035)" já existe no script):
  - `repo_name_of <dir>` — `git -C dir remote get-url origin` → último segmento sem `.git`.
  - `repo_rules` — baixa/usa o `/Skills/config` (`BranchStrategy.repositories`; cache em
    `$PRMAKE_HOME/.skills-config.json`, 1 h) → `match\tkind`. Sem acesso → retorna 1 (scan não grava).
  - `repos_scan [--quiet]` — raízes (`PRMAKE_REPOS_ROOTS` ou padrão da spec + pais das pastas já no mapa + workspace do
    `~/.prmake-agent/config.json`); `find` com `-maxdepth 4` e `-prune` (node_modules, bin, obj, .git, Library,
    .prmake-wt, dist, packages, kb-mirror); só `.git` **diretório** (worktree = arquivo, ignora); não desce dentro de
    repositório achado. Tempo limite `PRMAKE_REPOS_SCAN_TIMEOUT` (60 s) com `timeout`/`gtimeout` quando houver, senão
    contagem de diretórios. Casa com as regras → `kind`. Mescla: `manual`/`env` intocados; `scan` atualizado; pasta que
    sumiu sai; dois clones → `ambiguous`. Semeia `env` das variáveis antes. Grava atômico (`jq` → tmp → `mv`).
    Windows: `cygpath -m`.
  - `repos_show`, `repos_set <repo> <dir>` (valida remote), `repos_unset <repo>`, `repos_path <repo>` (contrato acima).
- Comando: `repos [scan [--quiet] | set <repo> <pasta> | unset <repo> | path <repo>]`; incluir no uso/ajuda.
- Interativo (`-t 0`): no fim de `repos scan`, `install` e `agent install` — lista, pergunta pelos ambíguos (número do
  candidato) e oferece informar pasta de padrão sem clone; confirmado → `confirmed:true`, `source:manual` no que o
  usuário digitou.
- `agent install`: faz o `repos scan` (se ainda não houver mapa) antes do `register`. **Não** passa `--workspace`: o
  executor calcula a pasta pelo mapa a cada execução (gravar congelaria a pasta e um clone novo em outro lugar ficaria
  de fora).
- Migração `.repos-v1` no `update` (inclusive `--quiet`): sem a marca, `( repos_scan --quiet && : > marca ) &`
  desacoplado (`nohup`/`disown`; no Git Bash, `&` simples). Pendências → uma linha no stdout (mensagem da spec).
- `status`/`doctor`: linha "repositórios: N mapeados, A ambíguos, F faltando (prmake-skills.sh repos)".
- `install.ps1`: só repassa (a ferramenta roda no Git Bash) — conferir que nada fixa caminho de repositório.

## E1 — Executor 1.0.7 (`tools/Cime.ExecutionAgent`)
- `Paths.PrmakeHome` (`PRMAKE_HOME` ou `~/.prmake`), `Paths.RepoMap` (`repos.json`); `RepoMap.Load()` (System.Text.Json,
  `AgentJson` source-gen — acrescentar os tipos) tolerante a arquivo ausente/inválido.
- `ConfigStore.ResolveWorkspace`: inserir, antes de `~/repos/solvace`, o ancestral comum das pastas do mapa (se não for a
  home nem a raiz do disco).
- `JobRunner`: `--add-dir` para cada pasta do mapa que não esteja sob o workspace nem sob outra já adicionada
  (deduplicar; no Windows comparar sem diferenciar maiúsculas).
- Partida (`Runner`) e depois de `update`: mapa ausente → `bash ~/.claude/skills/.prmake/prmake-skills.sh repos scan
  --quiet` (Git Bash no Windows: `CLAUDE_CODE_GIT_BASH_PATH`/descoberta já usada pelo executor); falha só loga.
- `Capabilities()`: mantém `repos`/`os`, acrescenta `repoMap` (contrato acima; `branch` por `git rev-parse
  --abbrev-ref HEAD` com limite de tempo, ou lendo `.git/HEAD`). `missing` exige as regras: usar o cache
  `$PRMAKE_HOME/.skills-config.json` da S1 (sem cache → `missing` vazio).
- `Doctor` (`Maintenance.cs`): troca "Repositório legado"/"Repositórios revamp" por "Mapa de repositórios" (aviso quando
  sem mapa, ambíguo ou faltando, com o comando `prmake-skills.sh repos`); mantém "Workspace".
- `Service.ExtraEnv`: acrescentar `PRMAKE_HOME`, `PRMAKE_REPOS_ROOTS`, `CARDS_DIR`.
- `<Version>1.0.7</Version>` no csproj (atualização automática).

## F1 — Front: "Meus executores" (`prform-app-0048`)
- `execution-queue.model.ts`: `capabilities.repoMap?: { items, ambiguous, missing, updatedAt }` (tipos do contrato).
- `executors-dialog.component.ts`: por máquina, seção recolhível "Repositórios (N)" — tabela nome · tipo · branch ·
  pasta (com `title` do caminho completo) · origem (busca/manual/variável); chips de aviso para ambíguos (lista os
  candidatos) e faltando, com o comando `bash ~/.claude/skills/.prmake/prmake-skills.sh repos` copiável. Sem `repoMap`
  → "executor antigo — atualize" (só texto). Responsivo; tema claro/escuro como o resto do diálogo.
- Build (`ng build`) com `node_modules` de `../prform-app-0019` (link ignorado pelo git).

## S2 — Skills usam o mapa
- `skills/analisar-bug/scripts/revamp-repos.sh`:
  - Resolver pasta por `prmake-skills.sh repos path` quando a ferramenta existir; sem ela, comportamento atual.
  - `list`: do mapa (nome pelo remote, `kind`, branch, pasta) + variáveis; sem mapa, como hoje.
  - `where <repo>`: nome pelo remote (curto/longo) ou nome da pasta; exit 2/3 propagados com mensagem
    (`repos set <repo> <pasta>`).
  - `grep`: escopos `all | legacy | revamp | <repo>` pelo `kind` (`legacy`/`integration-api` = legado;
    `revamp-backend`/`revamp-frontend` = revamp).
- `skills/analisar-bug/scripts/prmake-plan.sh branches`: depois do `KIND`, `repos path "$REPO"` → `pasta=<dir>` e a
  linha do `worktree` com a pasta real; ausente/ambíguo → mensagem + **exit 4** (a skill pergunta). `worktree` aceita
  o nome do repositório no lugar da pasta (resolve pelo mapa).
- `skills/base-solvace/scripts/arch.sh` (`stale`/`refresh`): `repoDir` → `repos path <nome>` antes de `SOLVACE_REPOS`.
- `gerar-prmake`: sem mudança (trabalha no diretório atual).

## S3 — Textos das skills
- `analisar-bug/SKILL.md`: regra curta — achar o código com `revamp-repos.sh where <repo>`; ausente/ambíguo (exit 2/3,
  `branches` exit 4) → `ask` ao usuário (pasta ou clonar); resposta com pasta → `prmake-skills.sh repos set` e segue;
  nunca concluir "sem o código" sem o usuário dizer que pode.
- `references/consultas.md`: trocar os caminhos fixos (`~/repos/solvace/revamp_separado`) pelo mapa; manter as
  variáveis como sobrescrita.
- `references/correcao.md`: `branches` já traz a pasta; exit 4.
- `skills/README.md`: seção "Repositórios da máquina (0048)".

## T1 — Teste local integrado
- Árvore falsa em `$TMPDIR/0048-home` (`HOME` trocado): repositórios `git init` com remotes `edv-solvace`,
  `edv-solvace-api`, `revamp-Users` (pasta `Solvace.Users`), `revamp-BOS` em duas pastas (ambíguo), um worktree,
  `kb-mirror/revamp-X`, `node_modules/x/.git`, repo a 5 níveis (fora). `/Skills/config` servido pelo Postgres local
  (docker) com a migração B1 aplicada.
- Esperado: mapa com edv-solvace, edv-solvace-api (`integration-api`), revamp-Users; revamp-BOS ambíguo; worktree,
  kb-mirror, node_modules e o fundo ignorados; `manual` não sobrescrito numa segunda busca; pasta apagada sai.
- `repos path`: curto/longo/maiúsculas, exit 2/3; variável por cima do mapa.
- Migração `.repos-v1`: `update --quiet` não bloqueia (tempo do hook), cria o mapa e a marca; sem rede não cria marca.
- `revamp-repos.sh list/where/grep` e `branches` (pasta real; exit 4 sem mapa da entrada).
- Executor 1.0.7 + Claude falso (como na 0047): workspace = ancestral comum, `--add-dir` das pastas fora dele, busca
  quando o mapa falta, `repoMap` no report, `doctor` com o novo item.
- Migração B1: config semeada intacta recebe a regra; config editada (regra já existe/fluxo próprio) fica igual;
  `Down`.
- Tela: executor com `repoMap` (ambíguo + faltando) e sem `repoMap`.
- Windows: rodar `repos scan`/`path` no Git Bash se houver máquina disponível; senão, registrar como pendente.
