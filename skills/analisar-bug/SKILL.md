---
name: analisar-bug
description: Faz a analise (triagem tecnica) de um bug a partir do card no PRMake/Azure DevOps e do codigo do repositorio, publica a analise na Timeline, propoe solucoes perguntando ao usuario (responde no PRMake ou no Claude) e monta e executa o plano de correcao (codigo, PRs por repositorio seguindo o fluxo de branches Solvace, chamados) — so abre PRs, nunca faz merge. Tudo vai para o plano de execucao no PRMake em tempo real (o usuario acompanha, pausa, continua ou cancela pela tela do card; se a sessao cair, retoma de onde parou). Descobre o card pela branch atual (hotfix/<card> ou bugfix/<card>) ou por um numero informado, le os repro steps, investiga o codigo em busca da causa raiz provavel, e registra tudo na timeline. Use quando o usuario pedir para "analisar bug", "fazer analise inicial", "triagem de bug", "investigar o card" ou similar.
---

# analisar-bug

Leva um bug **da analise a correcao**, em duas fases, cada uma com o seu plano de execucao no PRMake:
1. **Analise** — entende o problema pelos repro steps, investiga o codigo, levanta a causa raiz provavel,
   publica a analise na Timeline e **propoe solucoes perguntando ao usuario**.
2. **Correcao** — com as respostas, monta o **plano de correcao** (nao fixo: codigo, PRs por repositorio,
   chamados de script, validacoes; cada etapa feita por voce ou pelo usuario) e executa a sua parte.
   **Voce so abre PRs — nunca faz merge.** O plano termina quando os PRs forem mesclados (o PRMake detecta).

> **Instalada pelo PRMake** (tela *Skills*) em `~/.claude/skills/analisar-bug` e **atualizada sozinha** (hook
> `SessionStart` + passo 0). A fonte fica no repositorio do PRMake (`skills/analisar-bug`) — nao edite a copia
> instalada (a atualizacao nao sobrescreve arquivos editados a mao e avisa). Reusa o token do `gerar-prmake` /
> `prmake-timeline` (`~/.claude/prmake-token.txt` ou env `PRMAKE_TOKEN`).

## Configuracao e token

- **Plano de execucao (PRMake, feature 0023):** `scripts/prmake-plan.sh` — cria/retoma o plano do card,
  manda o andamento em pedacos, envia os arquivos e le pausar/continuar/cancelar da tela. Mesma api-key.
  Ver a secao **Plano de execucao no PRMake** abaixo — ela vale para TODO o fluxo.

- **Dados do card:** `GET https://api.softhouse.app.br/api/v1/Azure/card/<card>` — header `x-api-key`.
- **Timeline:** `POST https://api.softhouse.app.br/api/v1/Timeline` — header `x-api-key` (via a skill
  `prmake-timeline`).
- **Token:** resolvido nesta ordem: env `PRMAKE_TOKEN`, senao `~/.claude/prmake-token.txt`, senao
  `.claude/prmake-token.txt` do projeto. Os scripts ja fazem essa resolucao. HTTP 401/403 = token expirou.
- **AWS Cognito (opcional, ver passo 3b):** usa o AWS CLI ja autenticado na maquina (`aws sts
  get-caller-identity` deve funcionar). Regiao default `us-east-1` (env `AWS_REGION`); profile via
  `AWS_PROFILE`. Cada ambiente/tenant Solvace tem **seu proprio user pool, cujo nome == nome do
  ambiente** (ex.: `demo`, `qa`, `takeda`, `sandboxtakeda`).
- **Pasta do card (artefatos):** cada card ganha uma pasta em `~/.claude/cards/<card>/` (raiz
  sobrescrivivel por env `CARDS_DIR`), criada pelo `scripts/card-init.sh`. Subpastas: `scripts/`
  (scripts executaveis — SQL etc.), `analises/` (textos da analise em markdown), `dados/`
  (evidencias/saidas de consulta). Guarde ali a analise, os scripts e as evidencias do card.
- **SQL Server / RDS (opcional, ver passo 3c):** consultas **SOMENTE LEITURA** via
  `scripts/sql-query.sh` (driver `python-tds` no venv `~/.claude/skills/analisar-bug/.venv`).
  Credenciais por host em `~/.claude/sqlserver-credentials.json` (perm `600`, nunca impressas), com
  aliases `prod` / `prod3` / `prod4`. **IMPORTANTE: a maquina precisa estar conectada a VPN** para
  alcancar os hosts RDS — sem VPN a conexao falha por timeout.

## Plano de execucao no PRMake (obrigatorio em todo o fluxo)

Quem acompanha o card no PRMake ve, ao vivo, os planos (etapas), o que voce esta fazendo, as perguntas e os
arquivos. Por isso: **nada fica so na memoria/na maquina** — tudo o que voce produz vai para o plano em pedacos.
O PRMake tambem escreve sozinho na Timeline os marcos (perguntas, respostas, plano de correcao, etapas, chamados,
PRs mesclados, conclusao) — **nao duplique** esses registros com a `prmake-timeline`.

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh
bash $PLAN start <card> "Analise do bug <card>: <titulo curto>"      # cria OU retoma o plano aberto do card
bash $PLAN steps <card> - <<< '[{"key":"...","title":"...","description":"..."}, ...]'  # refina as etapas
bash $PLAN step <card> <key> running                                  # comecou a etapa
bash $PLAN log <card> <key> progress "Lendo OrderService.cs (fluxo de aprovacao)"   # pedaco de andamento
bash $PLAN log <card> <key> finding  "UserStatus = FORCE_CHANGE_PASSWORD no pool demo"
bash $PLAN log <card> <key> decision "Nao vou consultar o SQL: o erro e de validacao no front"
bash $PLAN checkpoint <card> <key> "Onde parei / o que falta nesta etapa"   # para retomar
bash $PLAN sync <card> <key>                                          # envia arquivos novos/alterados da pasta do card
bash $PLAN step <card> <key> completed                                # terminou
bash $PLAN step <card> <key> cancelled "Motivo (ex.: nao necessario — bug so no front)"  # pulou
bash $PLAN control <card>                                             # entre etapas: 0 segue, 10 pausado, 11 parar
bash $PLAN wait <card>                                                # pausado: espera o "Continuar" da tela
bash $PLAN watch <card>                                               # VIGIA em segundo plano: acorda voce quando algo muda no PRMake
bash $PLAN ask <card> - <<< '[{"stepKey":"propor-solucoes","text":"...","options":[...]}]'  # pergunta
bash $PLAN wait-answers <card>                                        # espera as respostas (tela ou terminal)
bash $PLAN answer <card> <n> "resposta dada aqui no terminal"         # grava no PRMake (via claude)
bash $PLAN correction <card> "Correcao do bug <card>: <solucao>" - <<< '[...etapas...]'  # plano de correcao
bash $PLAN use <card> analysis|correction                             # em qual plano os comandos agem
bash $PLAN link <card> <key> <url> "titulo" [--blocks]                # anexa link (chamado/doc) a etapa
bash $PLAN settings <card>                                            # configuracao das skills no PRMake
bash $PLAN branches <card> <repo> [--flow f] [--base b]               # branches/PRs/titulos do repo (da configuracao)
bash $PLAN pr-text <card> <repo> <branch-correcao>                    # prompt configurado + diff (layout padrao)
bash $PLAN save-pr-text <card> desc.md rca.md <key>                   # salva descricao/RCA no card do PRMake
bash $PLAN open-pr <card> <repo> <branch> <destino> "<titulo>" desc.md   # abre PR (nunca merge) — linha pronta no `branches`
bash $PLAN status <card> completed "" "$CARD_DIR/analises/analise-inicial.md"   # fim do plano ativo
```

Regras:
- **Comece por ele.** Assim que souber o numero do card (passo 1), rode `start`. Se ele responder
  `PLANO INDISPONIVEL` (PRMake fora do ar ou sem o recurso), siga normalmente: os comandos do plano viram
  no-op. Se ja existir um plano aberto (analise **ou correcao**), o `start` **retoma** o mais recente e imprime
  as etapas com status, checkpoints, perguntas, links e arquivos: continue da **primeira etapa pronta**, usando
  o checkpoint; rode `pull` se a pasta do card nao tiver os arquivos. **Analise ja concluida sem plano de
  correcao** (inclusive a feita na versao anterior da skill, que parava em `publicar`): o `start` a reabre para
  o passo 6 (acrescenta `propor-solucoes` se faltar) — **nao refaca a analise**; use a analise publicada
  (`analises/analise-inicial.md`) para propor as solucoes. So use `start ... --new` se o usuario pedir uma
  analise nova do zero.
- **Refine o plano de analise** depois de ler o card (passo 2): ajuste titulos/descricoes ao caso concreto (a
  descricao diz *o que vai ser feito*), remova/cancele (com motivo) o que nao se aplica e acrescente o que
  surgir. Keys: minusculas, numeros, `-`/`_`, estaveis (nunca renomeie uma key).
- **Em cada etapa:** `step running` → `log` a cada acao relevante (arquivo lido, hipotese, achado, decisao,
  consulta — frases curtas, markdown ok; `finding` para achados, `decision` para escolhas, `warning`/`error`
  para problemas) → salve os arquivos na pasta do card e rode `sync <card> <key>` → `checkpoint` → `step completed`.
- **Entre etapas (e antes de qualquer coisa demorada):** `control`. Exit **10** = pausado pelo usuario: rode
  `wait <card> 3600` **em segundo plano** (Bash com `run_in_background: true`), avise que ficou pausado e encerre a
  sua vez — quando o usuario clicar *Continuar* no PRMake o comando termina e te acorda (exit 0 = continue;
  11 = cancelado: pare). Exit **11** = cancelado/concluido pela tela: pare e
  nao envie mais nada. O `control` tambem lista: etapas **canceladas** (pule), **prontas** (pode comecar),
  **aguardando** (resposta/chamado/merge — nao mexa) e **perguntas sem resposta**.
- **Etapas do usuario** (`executor: user`, ex.: abrir o chamado, validar em QA): diga ao usuario o que fazer
  (com o texto/arquivos prontos) e **siga com as outras etapas prontas** — o usuario conclui a dele na tela.
- **Nunca fique parado esperando o PRMake sem o vigia.** O PRMake nao consegue chamar esta sessao; quem te
  acorda e um comando rodando em segundo plano. Sempre que a sua vez for terminar com algo pendente de fora
  (etapa do usuario, chamado, PR aguardando merge, perguntas, plano pausado), rode
  `bash $PLAN watch <card>` **em segundo plano** (Bash com `run_in_background: true`) e so entao encerre a vez
  dizendo o que esta aguardando. Quando o vigia terminar, a saida diz o que mudou (etapa concluida pelo
  usuario, chamado resolvido, PR mesclado → etapa de PR concluida, respostas, pausa/continuar, plano
  concluido/cancelado): rode `control` e **continue sozinho** com a proxima etapa pronta — sem pedir ao usuario
  para escrever nada. Se ainda restar espera, rode o vigia de novo. Exit 11 = plano concluido/cancelado: faca o
  relatorio final (passo 9) e pare. O usuario nao precisa clicar em nada nem avisar no chat.
  (Se a sessao for fechada, o vigia morre junto — `/analisar-bug <card>` retoma depois.)
- **Falha de rede** nao interrompe a skill: o envio vai para uma fila local e e reenviado na proxima chamada
  (`flush` forca). Erro 400 (ex.: plano cancelado) interrompe — leia a mensagem.
- PII: o que vai para o plano fica visivel no PRMake — mesmo cuidado da timeline.

Etapas padrao da analise (o `start` cria se voce nao passar outras): `identificar-card`, `coletar-dados`,
`investigar-codigo`, `consultar-ambiente` (opcional — cancele com motivo se nao precisar), `causa-raiz`,
`montar-analise`, `publicar`, `propor-solucoes`.

## Catalogo de tratamentos (aprendido dos cards reais)

Estudo de 300 bugs resolvidos (ultimos 120 dias, 2026-09) e das timelines do PRMake: **58% foram resolvidos sem
codigo**. Cada card e um caso: use o catalogo para **reconhecer o(s) padrao(oes)** e montar **etapas proprias
do caso** — nunca uma lista fixa, e combine padroes quando for o caso (ex.: codigo + correcao de dados; script +
orientacao ao cliente).

| Padrao | ~% | Sinais tipicos | Diagnostico | Etapas tipicas (adapte) | `devops classify` |
|---|---|---|---|---|---|
| **A. Defeito de codigo** | 32 | reproduz em qa/sandbox; comportamento contradiz a regra; erro identificavel no codigo | codigo (legado/revamp), reproducao | `corrigir-<repo>` → validar (build/local) → `pr-<repo>` (com `pr-text`/`save-pr-text`) → `validar-qa` (usuario) → fechamento | `code-fix` (ou `code-data-fix` se tambem corrigiu dados causados pelo defeito) |
| **B. Correcao de dados (script via chamado)** | 11 | registro excluido/estado inconsistente, preferencia ausente, flag/menu errado, `LAST_SITE_ID`/site/area errados | SQL **somente leitura** (`sql-query.sh`) | `montar-script` (voce: `scripts/01_*.sql` + `99_rollback_*.sql` + texto do chamado) → `chamado-script` (usuario, link com `--blocks`) → `validar-dados` (voce, SQL somente leitura depois do script) → fechamento | `script-defect` (causa foi defeito) · `script-user-action` (causa foi acao do usuario) · `script-environment` |
| **C. Acesso / Cognito / SSO** | (parte de B, D, E) | loop de login, usuario desabilitado/deslogado no Cognito, SSO trocou o e-mail, usuario nativo + federado duplicados, usuario sem planta/area | `cognito-query.sh` + SQL | ajuste no Cognito ou no cadastro → **etapa do usuario** (voce nao escreve no Cognito) com o passo a passo; se precisar de banco → padrao B; confirmar o login | conforme o que foi feito: `script-*`, `configuration` ou `user-education` |
| **D. Configuracao / ambiente / plataforma** | 11 | parametro de ambiente, WAF/instancia, habilitar modulo, criar usuarios de treino, configuracao de tela | codigo + ambiente + dados | `configurar-<o que>` (usuario ou time de infra — voce redige o passo a passo exato em `analises/configuracao.md`) → validar → fechamento | `configuration` (ou `configuration-change-request`) |
| **E. User education (nao e defeito)** | 28 | funciona conforme a regra de negocio; filtro/uso incorreto; expectativa diferente do produto | provar pelo codigo/dados que o comportamento e o esperado | `orientar-cliente` (usuario; voce redige a orientacao PT/EN em `analises/orientacao-cliente.md`, sem jargao) → fechamento | `user-education` (ou `user-education-change-request`) |
| **F. Change request / requisito nao mapeado** | 5 | pedido de algo que o produto nao faz | confirmar que nao e defeito | redigir a justificativa e o encaminhamento (usuario) → fechamento | `change-request` · `not-mapped-requirement` |
| **G. Nao reproduz / sem retorno** | 10 | sem evidencia do erro; instabilidade passada; cliente nao responde | tentativas em qa/sandbox, logs, dados | registrar as tentativas (voce) → `pedir-informacoes` ao cliente (usuario) → fechamento | `cannot-reproduce` · `no-user-feedback` |
| **H. Duplicado** | 3 | mesmo problema de outro card | buscar o card original | vincular/avisar (usuario) → fechamento | `duplicated` |

**Quem grava e o PRMake — sempre.** Voce le o card (pelo PRMake), investiga e **gera os textos** (analise, root
cause, resumo nao tecnico PT/EN, descricao do PR, passo a passo, orientacao ao cliente), mas **nada e gravado
direto no Azure DevOps**: toda gravacao (root cause, resumo na discussion, classificacao, estimativa, Remaining,
mudanca de estado, registro do card, PRs, timeline) vai pelos endpoints do PRMake — ele usa a integracao do
Azure/GitHub que o usuario configurou, registra na Timeline e guarda as configuracoes por usuario. Se faltar um
endpoint para algo que precisa gravar, **pare e avise** (nao contorne com PAT/API direta).

**Configuracao vem do PRMake — nada presumido.** Tudo o que pode mudar fica configurado no PRMake (tela de
plugins) e muda sem atualizar a skill:
- **Skills Configurations** (`bash $PLAN settings <card>`): fluxo de branches pela area do card
  (`BranchFlowByArea`), estrategia por tipo de repositorio e fluxo (`BranchStrategy`: base, branches derivadas,
  cherry-picks, destinos dos PRs), nome da branch (`BranchNamePattern`), mensagem de commit
  (`CommitMessagePattern`), titulo do PR (`PrTitlePattern`), repositorio padrao e o sistema de chamados
  (`TicketSystem`). O `branches <card> <repo>` ja aplica tudo isso e imprime os comandos prontos.
- **Acoes DevOps** (`bash $PLAN devops <card> config` e `devops <card> classifications`): estados e areas de
  destino, area exigida, comentario, estimativa inicial, prompt do resumo nao tecnico e opcoes de classificacao.
- **Campos do DevOps** (nomes dos campos de classificacao, Remaining, estimativa, root cause) e prompts do PR:
  usados pelo PRMake ao gravar; aparecem no `settings`.
Leia antes de perguntar ou de gravar e use os valores exatamente como vierem (nunca escreva de memoria nomes de
estado, area, branch ou titulo). Faltou uma regra (ex.: repositorio sem tipo, fluxo sem regra) → **pergunte ao
usuario** e sugira que o admin configure no PRMake. O que o card tem hoje (estado, area, tipo) vem do card lido
pelo PRMake (`bug-fetch.sh` → `state`/`area`).

**Fechamento (todo card, com ou sem codigo)** — o que os cards reais sempre tem no fim, via `devops` (endpoints
das "Acoes DevOps" do PRMake). Comece por `bash $PLAN devops <card> config`:
1. root cause no DevOps: `bash $PLAN devops <card> rootcause rca.md` (com codigo, a `gerar-prmake` ja faz);
2. classificacao: escolha pela evidencia a opcao correta entre as que o PRMake devolve em
   `devops <card> classifications` (a coluna da tabela acima mostra as usuais, mas o admin pode ajusta-las) e
   grave com `bash $PLAN devops <card> classify <opcao>`;
3. resumo nao tecnico PT/EN na discussion: se o `config` trouxe `summary-prompt`, **siga esse prompt** (arquivo
   `analises/summary-prompt.txt` da pasta do card; mesmos placeholders da `gerar-prmake`) mantendo o formato
   `**PT**` `---` texto `**EN**` `---` texto; senao use esse formato direto. Sem codigo, nao diga que houve
   correcao de codigo. Grave com `bash $PLAN devops <card> summary resumo.md`;
4. `bash $PLAN devops <card> zero-remaining` e, **se o usuario confirmou**, a mudanca de estado configurada:
   `devops <card> test-in-production` (so se o `config` a mostrar configurada e o card estiver na area exigida —
   senao o PRMake devolve 409) ou `devops <card> ready-for-qa`. Acao "NAO configurado" no `config` → nao ofereca;
   avise que o admin pode configura-la no PRMake (AI Configurations).

**Mover o card e sempre pelo PRMake — voce chama, o usuario nao move na mao.** `devops <card> ready-for-qa` e
`devops <card> test-in-production` chamam o endpoint do PRMake `POST Azure/card/<card>/actions/<acao>`; o PRMake
move o card com a integracao do Azure do usuario e registra na Timeline. A resposta do usuario a pergunta de
fechamento (passo 6, ou a pergunta que voce fizer no fim) **e a autorizacao**: assim que ela chegar (pela tela ou
pelo chat), rode o comando da acao escolhida — sem perguntar de novo e **sem pedir ao usuario para mover o card na
tela do DevOps/PRMake**. "Nao mover agora" → nao chame e registre no plano.
- Se o Claude Code barrar o comando (permissao/auto mode), explique que ele e o endpoint do PRMake (nao uma
  escrita direta no DevOps), peca para o usuario autorizar e rode de novo. Nao ofereca mover na mao como saida.
- `HTTP 502/503` com `TF10216`/"Azure DevOps services are currently unavailable" = DevOps fora do ar: o script ja
  tenta de novo por alguns minutos; se ainda falhar, rode de novo **em segundo plano** (Bash com
  `run_in_background: true`), ex.: `for i in 1 2 3 4 5 6; do sleep 120; bash $PLAN devops <card> <acao> && exit 0; done; exit 1`,
  registre um `log warning` e conclua o fechamento quando ele terminar com sucesso.
- `HTTP 409` = o card nao esta na area exigida pela acao: diga qual e a area e ofereca as outras acoes configuradas.
Sem codigo, antes do passo 1 salve o texto do tratamento no card com `save-pr-text` (descricao do que foi feito +
RCA) — o registro do card no PRMake passa a existir e o resumo fica vinculado.

## Fluxo

### 0. Manter a skill atualizada
```bash
bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet analisar-bug 2>/dev/null || true
```
Se imprimir "Skills do PRMake atualizadas", **releia esta SKILL.md** antes de continuar (a versao mudou).
Sem o arquivo (skill instalada a mao), ignore.

### 1. Identificar o card
Rode `git rev-parse --abbrev-ref HEAD`. A branch deve ser `hotfix/<numero>` ou `bugfix/<numero>`:
- `card` = numero apos o prefixo.
- Se o usuario passar um numero em ``, use-o.
- Se a branch nao seguir o padrao e nenhum numero for informado, **pare** e peca o numero do card.

### 1b. Criar a pasta do card
Crie (idempotente) a pasta de artefatos do card e use-a ao longo da analise:
```bash
CARD_DIR="$(bash ~/.claude/skills/analisar-bug/scripts/card-init.sh <card>)"
# ex.: ~/.claude/cards/<card>/ com scripts/ analises/ dados/
```
Em seguida **crie ou retome o plano** (ver *Plano de execucao no PRMake*) e registre esta etapa:
```bash
bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh start <card> "Analise do bug <card>"
bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh step <card> identificar-card running
bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh log <card> identificar-card info "Card <card> (branch <branch>)"
bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh step <card> identificar-card completed
```
Imagens/prints que o usuario mandar ou que voce gerar vao em `$CARD_DIR/imagens/` (a tela mostra a previa);
outros anexos em `$CARD_DIR/anexos/`.
Ao longo do trabalho, salve nela: a analise em `analises/`, scripts em `scripts/`, evidencias
(saidas de consulta) em `dados/`. Opcional: um `README.md` na raiz resumindo card, causa e conteudo.

### 2. Buscar os dados do bug (somente leitura)
```bash
bash ~/.claude/skills/analisar-bug/scripts/bug-fetch.sh <card>
# ex.: bash ~/.claude/skills/analisar-bug/scripts/bug-fetch.sh 72517
```
O script grava em `/tmp/bug-analysis/`:
- `description.txt` — `ReproSteps` (se for **Bug**) ou `System.Description` (US), ja sem HTML;
- `card.json` — dados brutos do card (titulo, estado, tipo) para conferencia.

O manifesto informa `title`, `state`, `workItemType`, `isBug`, `area` e `fluxo` (calculado pela area do card com
o `BranchFlowByArea` do PRMake; `perguntar` quando nenhuma regra casa — usado no fluxo de branches do passo 7). Leia `description.txt` para entender
o problema relatado.

Plano: etapa `coletar-dados` (running → `log` com um resumo curto do relato → completed). Copie o
`description.txt` para `$CARD_DIR/dados/` e rode `sync`. **Agora refine as etapas** (`steps`) com o que
voce vai fazer neste caso concreto.

### 3. Investigar o codigo (a analise em si)
Voce (Claude) faz a triagem tecnica. Com base nos repro steps e no titulo do card:
- **Localize o codigo relevante** (use Grep/Glob/Read e o agente `Explore`). Procure telas, endpoints,
  handlers, servicos, validacoes e queries mencionados ou implicados pelo comportamento descrito.
- **Reconstrua o fluxo** do que o usuario faz ate onde o erro provavelmente ocorre.
- **Levante hipoteses de causa raiz**, priorizando as mais provaveis, e aponte os **arquivos/metodos
  suspeitos** (`caminho:linha`).
- **Aponte o que ainda falta confirmar** (dados, ambiente, logs, reproducao local) e um **proximo passo**.

> **Onde o codigo pode estar — legado vs. revamp.** O Solvace tem dois mundos de codigo, e o bug pode
> estar em qualquer um:
> - **Legado — `edv-solvace`** (este repo, quando a skill roda daqui): modulos **.NET Core** e **ASP
>   Classic**. E o ponto de partida natural.
> - **Revamp — `~/repos/solvace/revamp_separado`**: reescrita dos modulos legados. **Cada modulo e um
>   repositorio git proprio** (micro-monolito, clean architecture: `Domain`/`Application`/`Infra.Data`/
>   `API`), ex.: `revamp-BOS`, `revamp-CIL`, `revamp-Complaint`, `revamp-ActionPlan`... Os **building
>   blocks compartilhados** vem como pacotes NuGet `Solvace.BuildingBlocks.*` (CodeArtifact) — o fonte
>   deles nao fica nesse diretorio.
>
> Se o card for de um modulo ja migrado (ou voce nao achar o codigo no legado), **procure tambem no
> revamp** com o script `revamp-repos.sh`:
> ```bash
> # listar repos disponiveis (legado + cada modulo revamp, com branch atual)
> bash ~/.claude/skills/analisar-bug/scripts/revamp-repos.sh list
>
> # caminho de um modulo revamp (aceita 'BOS' ou 'revamp-BOS')
> bash ~/.claude/skills/analisar-bug/scripts/revamp-repos.sh where BOS
>
> # buscar um padrao no codigo — escopo: all (default) | revamp | legacy | <modulo>
> bash ~/.claude/skills/analisar-bug/scripts/revamp-repos.sh grep "NomeDaClasseOuMetodo" revamp
> bash ~/.claude/skills/analisar-bug/scripts/revamp-repos.sh grep "PhysicalLayout" BOS
> ```
> Ao achar o modulo certo, use Grep/Glob/Read direto no caminho dele (`revamp-repos.sh where <mod>`)
> para aprofundar. Diga na analise **em qual repo/mundo** (legado ou revamp-<modulo>) esta o codigo.
> Caminhos default sobrescreviveis por env `REVAMP_DIR` e `EDV_SOLVACE_DIR`.

Plano: etapa `investigar-codigo` — mande um `log` a cada arquivo/fluxo relevante que ler e cada hipotese
(`finding`), e o `checkpoint` com os caminhos ja vistos e o que falta ver. Rode `control` antes de mergulhar
em outro modulo/repo.

Nao invente: se algo nao puder ser confirmado pelo codigo, marque como hipotese a validar. Esta e uma
analise **inicial** — o objetivo e direcionar a correcao, nao necessariamente fechar a causa.

> **Foco no bug do card (regra padrao).** Cada card e um ticket aberto pelo cliente, e tratamos
> **somente ele**. As sugestoes de solucao (correcao de dados, scripts, mudanca de codigo) miram
> **apenas o caso relatado**: o usuario, o registro ou o fluxo do ticket. Se a investigacao mostrar
> outros usuarios/registros com o mesmo problema, ou uma falha mais ampla, **apenas avise**, numa
> nota curta separada, sem propor correcao em lote nem transformar isso no foco da analise. Scripts
> de correcao cobrem so o caso do card.

### 3b. Consultar o AWS Cognito (quando o bug envolve um usuario/ambiente)

Plano: etapa `consultar-ambiente` (se nao for necessaria, `step ... cancelled "Nao necessario: <motivo>"`).
Salve as saidas das consultas (resumidas, sem PII desnecessaria) em `$CARD_DIR/dados/` e rode `sync`.
Muitos bugs dependem do **estado do usuario ou do ambiente** (usuario desabilitado, e-mail nao
verificado, status `FORCE_CHANGE_PASSWORD`, grupos/roles, atributo `custom:environment`). Use o script
`cognito-query.sh` para trazer esse contexto para a analise. Descubra o **ambiente/tenant** e o
**e-mail/username** do usuario a partir dos repro steps (ou peca ao usuario).

> **Quando falta o e-mail/identificador exato — busque por pedacos do nome.** Nao fique "chutando"
> e-mails inteiros. O jeito confiavel de descobrir o identificador e buscar por **fragmento do nome**,
> quebrando em pedacos pequenos (comece pelo **sobrenome**, que costuma ser mais distintivo, ou por um
> trecho incomum). Isso resolve nomes com apostrofo/acentos/prefixos (ex.: "O'Keefe" -> buscar `Keefe`;
> e-mail real era `Phil.OKeefe@...`, nao `Philip.OKeefe@...`).
> - **No banco (mais eficaz, substring real):** `TB_WCM_USER` com `LIKE` — via `sql-query.sh` (passo 3c,
>   somente leitura). Ex.: `SELECT TOP 50 USER_ID, USER_FULLNAME, USERNAME, EMAIL, SSO_ID, SSO_USERNAME,
>   ACTIVE, LAST_SITE_ID FROM TB_WCM_USER WHERE USER_FULLNAME LIKE '%Keefe%'`. Do resultado tira o
>   **e-mail / SSO_USERNAME / SSO_ID** exatos para entao consultar o Cognito com precisao.
> - **No Cognito (apenas prefixo):** o filtro do `list-users` so aceita **igualdade e prefixo** (`^=`),
>   nao substring — entao serve para prefixo de nome/e-mail (`family_name ^= "..."`), mas para nome com
>   apostrofo/parcial o `LIKE` no banco e mais garantido.

```bash
# achar o pool de um ambiente (aceita substring; sem filtro = todos)
bash ~/.claude/skills/analisar-bug/scripts/cognito-query.sh pools takeda

# consultar um usuario por e-mail (ou username) no pool do ambiente:
# retorna Username, UserStatus, Enabled, datas, Groups e Attributes (incl. custom:*)
bash ~/.claude/skills/analisar-bug/scripts/cognito-query.sh user takeda usuario@cliente.com

# listar grupos/roles do usuario
bash ~/.claude/skills/analisar-bug/scripts/cognito-query.sh groups takeda <username>

# so o UserPoolId resolvido para o ambiente
bash ~/.claude/skills/analisar-bug/scripts/cognito-query.sh pool-id takeda
```

O script resolve o pool pelo **nome exato** do ambiente; se nao houver exato, tenta substring unico
(se ambiguo, lista candidatos). Requer AWS CLI autenticado (HTTP/erro de credencial = rode
`aws sso login`/configure o profile). Estas consultas sao **somente leitura**. Trate os dados do
usuario como sensiveis (PII): use o que for relevante para a analise e evite despejar dados pessoais
desnecessarios na timeline.

### 3c. Consultar o banco SQL Server (quando o bug depende de dados)
Quando a causa provavel envolve **estado dos dados** (registro faltando, flag/status inesperado,
inconsistencia, config por tenant), consulte o banco em **modo somente leitura** com `sql-query.sh`.

> **Pre-requisito: VPN.** Os hosts RDS so sao alcancaveis com a **VPN conectada**. Se der timeout de
> conexao, o mais provavel e que a VPN esteja desligada — avise o usuario.

```bash
# aliases de host: prod | prod3 | prod4 (ou o hostname RDS completo)
bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod3 -d <database> \
  -q "SELECT TOP 20 Id, Name, Status FROM dbo.SomeTable WHERE ... "

# SQL longa por arquivo ou STDIN; saida em JSON com --json
bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod -d <database> -f consulta.sql
echo "SELECT ..." | bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod4 -d <database>
```

Opcoes: `-d/--database` (default `master`), `--max-rows` (default 1000), `--timeout` (s), `--json`.

**Garantias de somente-leitura (nao contornar):**
- Apenas statements que comecam com `SELECT`/`WITH` sao aceitos; qualquer `INSERT/UPDATE/DELETE/
  MERGE/DROP/ALTER/CREATE/TRUNCATE/EXEC/INTO/sp_*/xp_*` (etc.) e **recusado** antes de conectar
  (comentarios sao removidos, entao nao da para esconder keyword em `--`/`/* */`).
- A execucao roda em transacao com `autocommit` desligado e **sempre faz ROLLBACK** — nada e
  persistido mesmo que algo escape da validacao.
- Isolamento `READ UNCOMMITTED` para nao bloquear a producao.

**Nunca** tente escrever no banco por esta skill — se precisar de escrita, isso e responsabilidade do
usuario por outra via. Trate os dados retornados como sensiveis; leve para a timeline so o que for
relevante para explicar o bug (sem PII desnecessaria).

### 4. Montar o texto da analise

Plano: etapa `causa-raiz` (log `finding` por hipotese, com o porque) e depois `montar-analise`: ao gravar
a analise e os scripts na pasta do card, rode `sync <card> montar-analise` — o usuario le a analise e baixa os
`.sql` direto na tela do card.
Monte a analise em `$CARD_DIR/analises/analise-inicial.md` (a pasta do passo 1b) seguindo esta
estrutura (Markdown, objetivo e claro, em portugues). Se gerar scripts (ex.: SQL de correcao),
salve-os em `$CARD_DIR/scripts/` — quando a **ordem de execucao importa**, prefixe `01_nome.sql`,
`02_nome.sql`, ...; use `99_rollback_*.sql` para rollback (ou embuta o rollback como bloco comentado
no proprio script). Evidencias/saidas de consulta vao em `$CARD_DIR/dados/`.

```markdown
**Analise inicial — Card <card>: <titulo>**

**Problema relatado**
<resumo do comportamento descrito nos repro steps>

**Fluxo/Reproducao provavel**
<o caminho do usuario ate o erro, do que se entende>

**Investigacao no codigo**
<em qual mundo/repo esta o codigo (legado edv-solvace ou revamp-<modulo>); arquivos/metodos relevantes em `caminho:linha`>

**Contexto do usuario/ambiente (Cognito)** *(incluir so se consultado no passo 3b)*
<achados relevantes: status/enabled/grupos/atributos que expliquem o comportamento; sem PII desnecessaria>

**Dados relevantes (SQL)** *(incluir so se consultado no passo 3c)*
<achados no banco que expliquem/confirmem o bug: registro faltando, status inesperado, inconsistencia — resumido, sem PII desnecessaria>

**Causa raiz provavel (hipoteses)**
<hipotese(s) priorizada(s), com o porque; deixe claro o que e hipotese vs. confirmado>

**Pontos suspeitos**
<lista de arquivos/metodos a investigar/corrigir — `caminho:linha`>

**Padrao de tratamento provavel** *(catalogo: A codigo · B dados/script · C acesso/Cognito · D configuracao · E user education · F change request · G nao reproduz · H duplicado)*
<padrao(oes) e por que; o que isso implica (ex.: sem PR; chamado de script; orientar o cliente)>

**Proximos passos / a confirmar**
<o que falta validar e a correcao proposta PARA O CASO DO CARD (usuario/registro/fluxo relatado)>

**Observacao: outros casos** *(opcional; so se a investigacao encontrou outros afetados)*
<uma ou duas linhas, apenas como aviso — sem plano de correcao em lote>
```

### 5. Publicar na Timeline
Poste a analise completa na timeline via a skill `prmake-timeline` (acao aditiva, baixo risco — nao
precisa de confirmacao previa; publique direto apos montar):
```bash
bash ~/.claude/skills/prmake-timeline/scripts/prmake-timeline.sh <card> < "$CARD_DIR/analises/analise-inicial.md"
```
(A descricao vai via STDIN, entao textos longos/multilinha passam inteiros.)

O script imprime o HTTP code e a resposta; `OK` (HTTP 2xx) significa que a entrada foi gravada.

Plano: etapa `publicar` (running → completed). O plano de analise **ainda nao termina** — segue para
`propor-solucoes` (passo 6), que o conclui depois das respostas.

### 6. Propor solucoes e perguntar (etapa `propor-solucoes`)
Com a analise publicada, a analise ainda nao terminou: **proponha as solucoes** e **decida com o usuario**.
1. `step propor-solucoes running`. Escreva `$CARD_DIR/analises/solucoes.md`: 1 a 3 opcoes, cada uma com o que
   muda (repositorios/arquivos), riscos, se precisa de script de dados (chamado) e o esforco; marque a
   recomendada. `sync <card> propor-solucoes` e `log ... decision` com o resumo.
2. **Pergunte** (`ask`) tudo o que decide o plano — a etapa fica *aguardando* e o PRMake mostra as perguntas
   em destaque. Sempre que se aplicar:
   - Qual solucao seguir (opcoes das solucoes, com a recomendada).
   - **Padrao(oes) do caso** (catalogo acima) — proponha o que a analise indica, com a evidencia, e deixe o
     usuario confirmar/ajustar: codigo (A), dados via script/chamado (B), acesso/Cognito (C), configuracao/
     ambiente (D), user education (E), change request (F), nao reproduz/sem retorno (G), duplicado (H). Pode ser
     mais de um. Nem todo card tem codigo — nao presuma PR.
   - **Fechamento**: com o `devops <card> config` em maos, pode mover o card ao final (cite o estado e a area
     exatamente como configurados, so as acoes configuradas e aplicaveis a area atual do card, mais "Nao mover")?
     A resposta autoriza voce a mover pelo PRMake no `fechar-card` (ver **Mover o card e sempre pelo PRMake**). E a
     classificacao sugerida (uma das opcoes de `devops <card> classifications`, com o motivo) esta certa?
   - **Fluxo de branches**: rode `branches <card> <repo>` para cada repositorio; se ele sair com exit 3, pergunte
     o que ele pedir (o fluxo, quando a area nao tem regra, ou a branch base, quando a regra manda perguntar —
     com as opcoes que ele listar).
   - Se ha script de dados: vai por **chamado** (o usuario abre no sistema de chamados configurado — `TicketSystem`
     do `settings`) ou nao e necessario.
   - Se pode seguir com a correcao agora (ou so deixar o plano pronto).
   Use opcoes objetivas + texto livre. Mostre as mesmas perguntas no terminal.
3. **Espere as duas pontas ao mesmo tempo** — o usuario pode responder pela tela (PRMake) ou aqui:
   - Rode `bash $PLAN wait-answers <card> 3600` **em segundo plano** (ferramenta Bash com
     `run_in_background: true`) e so entao mostre as perguntas no terminal e encerre a sua vez esperando o chat.
     Voce **nao ve** respostas dadas no PRMake enquanto esta parado esperando o chat — e o comando em segundo
     plano que as detecta: quando todas forem respondidas pela tela ele termina e voce e acordado com as
     respostas na saida.
   - Se o usuario responder aqui no chat: grave cada resposta com `answer <card> <n> "..."` (o PRMake mostra
     "pelo Claude") e siga — o comando em segundo plano termina sozinho.
   - Se o usuario disser que respondeu no PRMake (ou voce voltar a sessao depois): rode `answers <card>`.
   - Exit 10 (1 h sem todas as respostas): avise e pare — o `start` retoma depois.
   Com as respostas: `step propor-solucoes completed` e `status <card> completed "" .../solucoes.md`
   (fim do plano de **analise**).

### 7. Montar o plano de correcao
Monte as etapas **a partir do(s) padrao(oes) confirmados e das especificidades deste card** — o catalogo da
as etapas tipicas, voce adapta (nomes, descricoes com o que exatamente sera feito neste card, quem executa,
dependencias) e **sempre termina com o fechamento** (`fechar-card`, executor claude, com os comandos `devops`).
So tem etapas de codigo/PR se houver codigo. Crie com `correction` — ele vira o
plano ativo e a tela mostra as abas *Analise* e *Correcao*. Cada etapa tem `executor` (`claude` ou `user`),
`kind` e, quando for o caso, `repository` e `dependsOn`:

| Etapa | kind | executor | Observacao |
|---|---|---|---|
| Corrigir o codigo — **uma por repositorio** (`corrigir-<repo>`) | `code` | claude | branches e commits do fluxo abaixo |
| Validar (build/testes/reproducao) | `validation` | claude ou user | depende da correcao |
| **PRs — uma por repositorio** (`pr-<repo>`) | `pr` | claude | todos os PRs daquele repositorio; conclui sozinha quando **todos** forem mesclados |
| Chamado de script de dados (`chamado-<nome>`) | `ticket` | **user** | voce prepara o `.sql` + texto do chamado (em `scripts/`); o usuario abre no sistema de chamados (`TicketSystem`) e anexa o link na tela (etapa fica *aguardando* ate o chamado ser marcado resolvido) |
| Configuracao na tela do sistema (`configurar-<o que>`) | `task` | **user** | voce nao acessa o sistema do cliente: escreva o passo a passo exato (ambiente, tela, campo, valor antes/depois) em `analises/configuracao.md`; o usuario executa e conclui a etapa na tela |
| User education (`orientar-cliente`) | `task` | **user** | voce redige a orientacao ao cliente (o que aconteceu, o que fazer, por que nao e bug) em `analises/orientacao-cliente.md` (PT/EN se o card for em ingles); o usuario envia e conclui |
| Validar com o cliente/ambiente | `validation` | user | depois da configuracao/orientacao/script, quando fizer sentido |
| Gerar PRMake (RCA no DevOps, resumo, campos) — **com codigo** | `task` | claude | skill `gerar-prmake` **sem** abrir PR (`OPEN_GITHUB_PR` desligado), **reaproveitando** a descricao/RCA ja gerados e salvos na etapa de PR (nao gere de novo) |

**Tratamento sem codigo** (o plano e dinamico — monte so o que o caso pede): quando a solucao e user education,
configuracao direto na tela do sistema ou algo do tipo, **nao crie nem sugira** etapas de correcao de codigo,
branches, PRs, "Gerar PRMake", chamados ou scripts — a menos que o caso realmente precise (ex.: so um script de
dados → so a etapa de chamado). O plano pode ter apenas, por exemplo:

```json
[{"key":"configurar-permissao","title":"Ajustar a permissao do perfil no sistema","kind":"task","executor":"user",
  "description":"Passo a passo em analises/configuracao.md"},
 {"key":"orientar-cliente","title":"Orientar o cliente (user education)","kind":"task","executor":"user",
  "description":"Texto pronto em analises/orientacao-cliente.md","dependsOn":["configurar-permissao"]},
 {"key":"validar-cliente","title":"Confirmar com o cliente","kind":"validation","executor":"user","dependsOn":["orientar-cliente"]}]
```

Nesses casos o seu trabalho e redigir o passo a passo / a orientacao (e salvar com `sync`), deixar o vigia rodando
e acompanhar: o plano **conclui quando as etapas terminam**, sem PR.

**Fluxo de branches — sempre pelo `branches`** (regras no PRMake, `Skills Configurations`):
```bash
bash $PLAN branches <card> <repo>                       # usa o fluxo pela area do card
bash $PLAN branches <card> <repo> --flow <f> --base <b> # quando ele pedir (exit 3) ou o usuario escolher
```
Ele imprime o tipo do repositorio, o fluxo, a base, a branch de correcao, a mensagem de commit, os PRs (branch
derivada, de onde sai + cherry-pick, destino, titulo) e os **comandos prontos** (`git checkout`, `cherry-pick`,
`push` e as linhas de `open-pr`). Siga exatamente o que ele imprimir; o nome da branch nunca leva o prefixo do
commit/titulo. Antes de criar branches, confira com `git fetch origin && git ls-remote --heads origin <base>` que
as bases existem; se nao existirem ou o caso nao estiver claro, **pergunte**. Commits so com a mudanca
necessaria: **sem comentarios novos no codigo** (ver passo 8).

Depois do `push`: `pr-text` na branch de correcao → gere `desc.md`/`rca.md` (passo 8) → `save-pr-text` → as
linhas de `open-pr` que o `branches` imprimiu (com o `desc.md` do repositorio).
Conflito no `cherry-pick`: resolva mantendo a intencao da correcao, registre um `log warning` e mencione no PR.

### 8. Executar o plano de correcao
Repita: `control` → pegue a proxima etapa **pronta** do `executor: claude` → faca (com `log`/`sync`/`checkpoint`)
→ `step completed`. Regras:
- **Mudar codigo so depois da resposta do usuario** (passo 6) e so no escopo do card.
- **Nao adicione comentarios no codigo.** A correcao deve ser so a mudanca de codigo — sem comentarios
  explicando o bug, a correcao, o card (`// AB#...`, `// fix: ...`, `// antes era...`), sem blocos comentados
  e sem remover/alterar comentarios existentes que nao tenham relacao com a mudanca. O porque da correcao vai
  na mensagem do commit (`CommitMessagePattern`), na descricao do PR e no plano/Timeline — nunca no codigo.
  Excecao rara: so se o proprio arquivo exigir (ex.: doc obrigatoria de API publica) e, mesmo assim, o minimo.
- Etapa de PR — **descricao no layout padrao do PRMake, salva no card**, antes de abrir os PRs de cada repositorio:
  1. `bash $PLAN pr-text <card> <repo> <branch-correcao>` (a do `branches`, ja no GitHub): baixa o **prompt
     configurado** (o mesmo da `gerar-prmake`), os repro steps e o diff em `$CARD_DIR/pr/<repo>/`.
  2. Gere `pr_generated.md` seguindo o prompt **exatamente como a `gerar-prmake`** (ingles, markdown, titulos em
     negrito; Bug com o RCA entre `<RCA>`/`</RCA>`; foco so no caso do card) e separe `desc.md`/`rca.md` com os
     comandos que o `pr-text` imprime.
  3. `bash $PLAN save-pr-text <card> $CARD_DIR/pr/<repo>/desc.md $CARD_DIR/pr/<repo>/rca.md pr-<repo>`: salva a
     descricao e o root cause **no card do PRMake** (ficam prontos no "Abrir PR" para quem quiser abrir um PR por
     fora) e guarda os arquivos no plano. Com mais de um repositorio, o card fica com a descricao do ultimo
     salvo — gere a do repositorio principal por ultimo (ou uma que cubra todos).
  4. Abra os PRs com essa descricao: as linhas de `open-pr` que o `branches` imprimiu (branch, destino e titulo
     conforme a configuracao) com `$CARD_DIR/pr/<repo>/desc.md`. Sem titulo, o `open-pr` usa o `PrTitlePattern`.
- Etapa de PR: os PRs abertos com `open-pr` ficam no card, na Timeline e anexados a etapa. **Nunca faca merge,
  nem aprove** — a etapa fica aguardando e conclui sozinha quando outra pessoa mesclar.
- Etapa do usuario: diga o que fazer (ex.: "abra o chamado com o texto e o `.sql` de `scripts/`, e anexe o
  link na etapa no PRMake") e siga com as outras etapas prontas.
- Sem nada pronto do seu lado (so aguardando merge/chamado/usuario): resuma o que falta, **deixe o vigia
  (`watch`) rodando em segundo plano** e encerre a vez — ele te acorda quando algo mudar e voce segue sozinho.
  O PRMake acompanha os PRs no GitHub (e mostra na tela sem cliques); o plano de correcao **conclui sozinho**
  quando todas as etapas terminam (PRs mesclados, chamados resolvidos) — ai o vigia termina com exit 11 e voce
  faz o relatorio final.

### 8b. A correcao nao resolveu — nova rodada no mesmo plano
Se o usuario disser que a correcao nao resolveu (ou a validacao falhar), **nao crie outro plano**: acrescente
etapas ao **mesmo plano de correcao** (`use <card> correction` e `steps` com as etapas atuais **mais** as novas —
keys novas, ex.: `ajustar-edv-solvace-2`, `pr-edv-solvace-2` com `dependsOn` na de ajuste, e a validacao de novo).
Ao marcar a primeira etapa nova como `running`, o plano volta a "em andamento". Corrija nas mesmas branches do
fluxo (as do `branches` — novos commits no padrao configurado e cherry-pick) e abra os PRs novos com
`open-pr`: eles entram na etapa de PR nova (os PRs antigos continuam na etapa anterior) e o plano so conclui de
novo quando os PRs novos forem mesclados. Todo PR aberto pelo PRMake tambem aparece no card (painel de PRs e
Timeline).

### 9. Reportar
Informe ao usuario: card, tipo, titulo, a causa raiz provavel, a solucao escolhida, os PRs abertos (links),
o que ficou com ele (chamados, validacoes) e o que falta — e que tudo esta nos planos do card no PRMake. Em
HTTP 401/403, avise que o token PRMake expirou (`~/.claude/prmake-token.txt` ou `PRMAKE_TOKEN`).

## Notas

- A **analise** (passos 1–6) e **somente leitura** no codigo. A **correcao** (passos 7–8) so comeca depois das
  respostas do usuario, e **nunca** inclui merge: voce abre os PRs pelo PRMake e para. Para gerar
  o PR/RCA depois da correcao, use a skill `gerar-prmake`.
- Se, durante a investigacao, voce quiser registrar marcos intermediarios (hipotese levantada, causa
  encontrada), pode postar entradas curtas adicionais na timeline com `prmake-timeline` antes da analise
  final — esta skill se integra com `prmake-timeline`.
- `card.json` traz mais campos (severidade, area, assigned to) caso precise de contexto extra.
