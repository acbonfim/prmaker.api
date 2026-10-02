# 0048 — Repositórios do usuário encontrados em qualquer máquina (mapa por remote, sem caminho fixo)

## Contexto
As skills e o executor só acham o código nos caminhos padrão da máquina de quem criou as skills:

- `revamp-repos.sh` (analisar-bug) usa `EDV_SOLVACE_DIR` → `~/repos/solvace/edv-solvace` e `REVAMP_DIR` →
  `~/repos/solvace/revamp_separado`; se a pasta não existe, só imprime um `AVISO` e a análise segue **sem código**.
- O executor abre o Claude no workspace = `config.workspace` → `PRMAKE_WORKSPACE` → pai do `EDV_SOLVACE_DIR` →
  `~/repos/solvace` → home. A instalação (`prmake-skills.sh agent install`) roda `register` **sem `--workspace`** e não
  pergunta nada; as variáveis só chegam ao serviço (launchd/systemd/tarefa) se estiverem no terminal na hora do
  `install` — mudou depois, precisa reinstalar.
- O `doctor` marca legado/revamp não encontrados como **aviso**, e o executor segue trabalhando assim.
- Mesmo na máquina do autor, o nome da pasta não é o nome do repositório: `revamp_separado/Solvace.Users` é
  `revamp-Users`, `authentication` é `revamp-Authentication`, `checklist` é `revamp-Checklist`. O `where <modulo>` casa
  pelo nome da pasta e já erra parte dos módulos.
- Ninguém garante que os módulos revamp de outra pessoa estejam numa pasta só.

O PRMake já identifica os repositórios pelo **nome do remote** (`BranchStrategy.repositories[].match` nas Skills
Configurations: `edv-solvace-apps`, `edv-solvace`, `revamp-*`; `repository` das etapas `code`/`pr`; `open-pr`). Falta a
máquina saber onde cada um está.

## Objetivo
1. **Mapa de repositórios por máquina**: `~/.prmake/repos.json` (fora de `~/.claude`, como os cards na 0046), chave =
   nome do repositório pelo remote (`git remote get-url origin`, último segmento sem `.git`), valor = pasta local. As
   skills (terminal) e o executor leem o mesmo arquivo — é arquivo, não variável: mudou, vale na hora, sem reinstalar
   o serviço.
2. **Descoberta automática pelo remote**: uma busca acha os clones de trabalho e guarda os que casam com
   `BranchStrategy.repositories` (nada fixo no script — os padrões vêm do `GET /Skills/config`).
3. **Instalação nova**: `install` e `agent install` fazem a busca, mostram o resultado e deixam confirmar/corrigir
   (com terminal interativo). O workspace do executor sai do mapa a cada execução (não é gravado no `register`).
4. **Quem já instalou**: migração única (`.repos-v1`) no `update --quiet` do hook — monta o mapa sozinha, em segundo
   plano, sem perguntar, e avisa numa linha. O executor novo também monta o mapa se ele não existir.
5. **Uso pelas skills**: `revamp-repos.sh` (`list`/`where`/`grep`) e o fluxo de correção (`branches`/`worktree`) usam o
   mapa; repositório do card fora do mapa → a skill pergunta (`ask`) em vez de seguir sem código.
6. **Visível no PRMake**: o executor manda o mapa (e o que está ambíguo/faltando) em "Meus executores"; o `doctor`
   aponta o comando para corrigir.
7. Compatível: `EDV_SOLVACE_DIR`, `REVAMP_DIR`, `PRMAKE_WORKSPACE` e `register --workspace` continuam valendo, por cima
   do mapa. Sem mapa, tudo funciona como hoje.

## Detalhes

### Formato do mapa
```json
{
  "version": 1,
  "updatedAt": "2026-10-02T12:00:00-03:00",
  "roots": ["/Users/x/repos"],
  "repos": {
    "edv-solvace":  { "path": "/Users/x/repos/solvace/edv-solvace", "kind": "legacy", "source": "env", "confirmed": true },
    "revamp-Users": { "path": "/Users/x/repos/solvace/revamp_separado/Solvace.Users", "kind": "revamp-backend", "source": "scan", "confirmed": false }
  },
  "ambiguous": { "revamp-BOS": ["/Users/x/a/revamp-BOS", "/Users/x/b/BOS"] },
  "missing": ["edv-solvace-api"]
}
```
- `source`: `env` (veio de variável), `scan` (busca), `manual` (`repos set` ou confirmação). A busca **nunca** sobrescreve
  `manual`/`env`; só atualiza/remove entradas `scan` (pasta que sumiu sai do mapa).
- `kind`: o da primeira regra de `BranchStrategy.repositories` que casa (mesma regra do `branches`).
- Chave comparada sem diferenciar maiúsculas (como o `branches` já faz).
- No Windows (Git Bash), caminhos gravados como `C:/...` (`cygpath -m`), legíveis pelo bash e pelo executor .NET.
- Gravação atômica (arquivo temporário + `mv`), com `jq`; scripts com o bloco "Windows/Git Bash (0035)".

### Busca (`prmake-skills.sh repos scan`)
- Raízes: `PRMAKE_REPOS_ROOTS` (separadas por `:`/`;`) ou, por padrão, as que existirem entre `~/repos`, `~/source`,
  `~/src`, `~/dev`, `~/projects`, `~/code`, `~/git`, `~/workspace`, `~/work`, `~/Documents` (fora do macOS) e, no
  Windows, `C:/repos`, `C:/dev`, `C:/projects`, `C:/src`, `C:/git`, `D:/repos`… + o pai de cada caminho já no mapa,
  o workspace configurado do executor e os pais das variáveis. A home (e o que está acima dela) nunca é raiz — só um
  nível dela (repositório direto em `~/edv-solvace`). Não varre a home inteira (lento no Windows e no macOS com
  `Library`).
- macOS: `Documents`/`Desktop`/`Downloads` ficam fora da busca padrão — listar essas pastas pede permissão do sistema
  (TCC), inclusive ao serviço do executor. Repositório lá: `repos set` ou `PRMAKE_REPOS_ROOTS`.
- Profundidade máxima 4 abaixo da raiz; não desce em ocultas, `node_modules`, `bin`, `obj`, `Library`, `.prmake-wt`,
  `dist`, `packages`, `kb-mirror`. Repositório dentro de repositório **conta** (na máquina do autor,
  `revamp_separado` é um repositório git com os módulos dentro). Tempo limite (padrão 60 s); estourou → grava o que
  achou, mantém as entradas antigas e avisa. Busca completa → as entradas `scan` antigas que não foram achadas saem.
- Ignora: **worktrees** (`.git` é arquivo — inclui os `.prmake-wt/<card>/...` do executor e worktrees de feature) e os
  clones de leitura da Base Solvace (`kb-mirror`, mapear.md — "nunca nas cópias de trabalho do usuário").
- `missing`: padrões das regras sem nenhum clone no mapa nem nos ambíguos — gravado no arquivo (o executor não
  precisa das regras para mostrar).
- Guarda só repositórios cujo nome pelo remote casa com `BranchStrategy.repositories` (glob, sem diferenciar
  maiúsculas). Sem acesso ao `/Skills/config` (sem token/rede) → não grava nada e tenta de novo na próxima vez.
- Dois clones de trabalho do mesmo remote → `ambiguous` com os candidatos (sem escolher sozinho, salvo se um deles já
  for `manual`/`env`).

### Comando `repos` (prmake-skills.sh)
- `repos` — mostra o mapa: repositório, tipo, branch atual, pasta, origem, e as pendências (ambíguos, regras sem
  nenhum clone encontrado).
- `repos scan [--quiet]` — busca e atualiza.
- `repos set <repo> <pasta>` — fixa à mão (valida que é um repositório git com aquele remote); resolve ambíguo.
- `repos unset <repo>` — tira do mapa.
- Com terminal interativo, ao fim do `scan` (e no `install`/`agent install`): lista o que achou, pergunta pelos
  ambíguos e oferece informar a pasta de um repositório não encontrado. Confirmado → `confirmed: true`.
- `status`/`doctor` do `prmake-skills.sh` incluem o resumo do mapa.

### Precedência (skills e executor)
1. Variável específica: `EDV_SOLVACE_DIR` (para `edv-solvace`), `REVAMP_DIR` (módulos revamp dentro dela — o
   comportamento de hoje).
2. Entrada do mapa (`manual`/`env` > `scan`).
3. Caminho padrão de hoje (`~/repos/solvace/...`).

### Skills
- `revamp-repos.sh`:
  - `list` — do mapa (repositório pelo remote, tipo, branch, pasta), mais o que vier das variáveis.
  - `where <repo>` — aceita o nome do remote (`revamp-Users`, `Users`, `users`) ou o nome da pasta; ambíguo/ausente →
    erro dizendo como resolver (`repos set`) — a skill então pergunta ao usuário.
  - `grep <padrao> [escopo]` — escopos `all | legacy | revamp | <repo>`, com `legacy`/`revamp` pelo `kind` do mapa.
- `prmake-plan.sh branches <card> <repo>` imprime `pasta=<caminho>` e a linha do `worktree` já com a pasta real (hoje
  sai `<pasta-do-repo>` para o Claude adivinhar). Repositório sem pasta no mapa → sai com código próprio (ex.: 4) e a
  mensagem para perguntar ao usuário.
- `analisar-bug` (SKILL.md e `references/consultas.md`/`correcao.md`): achar o código pelo `where`; repositório do card
  fora do mapa → `ask` ao usuário (pasta ou clonar), nunca concluir a análise "sem o código" sem ele dizer que pode.
  Resposta com uma pasta → `repos set` e segue.
- `arch.sh` (base-solvace): `repoDir` resolvido pelo mapa antes de `SOLVACE_REPOS`.

### Executor (`tools/Cime.ExecutionAgent`)
- `ResolveWorkspace`: `config.workspace` → `PRMAKE_WORKSPACE` → pai do `EDV_SOLVACE_DIR` → **ancestral comum das pastas
  do mapa** (se não for a própria home) → `~/repos/solvace` → home.
- Cada pasta do mapa fora do workspace entra como `--add-dir` (em `dontAsk` o Claude não lê fora das pastas liberadas).
- Na partida e após atualizar: mapa inexistente → roda `prmake-skills.sh repos scan --quiet` (cobre quem atualizou o
  executor sem abrir o Claude Code). Lê o mapa a cada execução (sem cache longo).
- `Capabilities` (report) passa a mandar o mapa: `repos` = lista de `{ name, path, kind, branch, source }`, mais
  `ambiguous` e `missing` (regras sem clone). Mantém o campo atual compatível para a tela antiga.
- `doctor`: troca as checagens "Repositório legado"/"Repositórios revamp" por "Mapa de repositórios": quantos, ambíguos e
  faltando, com o comando (`prmake-skills.sh repos`). Continua aviso, não erro.
- Sobe a `Version` (atualização automática).

### Frontend (Meus executores)
- Mostra o mapa de cada máquina: repositório, tipo, pasta, branch; destaca ambíguos e faltando com o comando para
  resolver. Só leitura.

### Migração de quem já instalou (`.repos-v1`)
- No `update --quiet` (hook `SessionStart`), sem a marca `$TOOL_DIR/.repos-v1`: dispara o `repos scan --quiet` **em
  segundo plano** (não atrasa o início da sessão), semeando primeiro as entradas `env` das variáveis existentes e o
  workspace configurado do executor. Concluiu e gravou o mapa → cria a marca. Falhou (sem token/rede) → sem marca, tenta
  na próxima sessão.
- Mensagem de uma linha quando houver pendência (o Claude da sessão vê a saída do hook), ex.:
  `PRMake: mapeei 14 repositórios (2 ambíguos) — confira com: bash ~/.claude/skills/.prmake/prmake-skills.sh repos`.
- Ordem de publicação livre: skill nova + executor antigo funciona (os scripts leem o mapa sozinhos); executor novo +
  skill antiga funciona (o executor monta o mapa); sem mapa, igual a hoje.

## Fora do escopo
- Clonar automaticamente repositórios que faltam.
- Editar o mapa pela tela do PRMake (a tela só mostra; corrigir é pela máquina).
- Mapear repositórios sem regra em `BranchStrategy.repositories` (pedir ao admin para cadastrar a regra) — exceto o
  `edv-solvace-api`, que ganha regra nesta feature.
- Escolher sozinho entre dois clones de trabalho do mesmo repositório.

### Regra do `edv-solvace-api` (API de integrações)
Hoje ele não casa com nenhuma regra de `BranchStrategy` (o `edv-solvace` é exato, sem glob) — a busca não o acharia e o
`branches` pararia com "sem regra". Migração das Skills Configurations acrescenta, **só se ainda não houver** regra com
esse `match` (valores editados pelo admin preservados):
- `repositories`: `{ "match": "edv-solvace-api", "kind": "integration-api" }`.
- `flows.producao["integration-api"]` e `flows.release["integration-api"]` (só se faltarem): `askBase: true`,
  `baseOptions: ["master", "release-version"]`, um PR `{ "suffix": "", "target": "{base}" }` — o repositório não tem
  `development`; os hotfix/bugfix recentes foram mesclados direto na `master` (ex.: PRs #2469 `hotfix/61770`, #2490
  `bugfix/61983`). Como o fluxo exato não está documentado, a base é perguntada ao usuário; o admin ajusta na tela
  (ex.: fixar `base: master` ou acrescentar o PR `-qa` a partir de `qa`).

## Decisões
- Regra própria para o `edv-solvace-api` no `BranchStrategy` (acima), em vez de uma configuração só para a descoberta.
- Sem confirmação obrigatória dos repositórios achados pela busca: a mensagem do hook e o `doctor` mostram as
  pendências; a skill só pergunta quando o repositório do card está ausente ou ambíguo no mapa.
