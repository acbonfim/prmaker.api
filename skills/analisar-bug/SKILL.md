---
name: analisar-bug
description: Faz uma analise inicial (triagem tecnica) de um bug a partir do card no PRMake/Azure DevOps e do codigo do repositorio, e publica a analise completa na Timeline do card no PRMake. Registra um plano de execucao no PRMake e manda o andamento e os arquivos em tempo real (o usuario acompanha, pausa, continua ou cancela pela tela do card; se a sessao cair, retoma de onde parou). Descobre o card pela branch atual (hotfix/<card> ou bugfix/<card>) ou por um numero informado, le os repro steps, investiga o codigo em busca da causa raiz provavel, e registra tudo na timeline. Use quando o usuario pedir para "analisar bug", "fazer analise inicial", "triagem de bug", "investigar o card" ou similar.
---

# analisar-bug

Faz a **analise inicial de um bug** (triagem tecnica) e **publica essa analise na Timeline do card**
no PRMake. Serve para, logo ao pegar um card, entender o problema pelos repro steps, investigar o
codigo do repositorio, levantar a causa raiz provavel e os pontos suspeitos, e deixar tudo registrado
no card para consulta futura.

> **Esta skill vive no nivel do usuario** (`~/.claude/skills/analisar-bug`), portanto esta disponivel
> em qualquer projeto/repositorio da maquina. Reusa o mesmo token do `gerar-prmake` / `prmake-timeline`
> (`~/.claude/prmake-token.txt` ou env `PRMAKE_TOKEN`).

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

Quem acompanha o card no PRMake ve, ao vivo, o plano (etapas), o que voce esta fazendo e os arquivos.
Por isso: **nada fica so na memoria/na maquina** — tudo o que voce produz vai para o plano em pedacos.

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
bash $PLAN status <card> completed "" "$CARD_DIR/analises/analise-inicial.md"   # fim (resumo = analise)
```

Regras:
- **Comece por ele.** Assim que souber o numero do card (passo 1), rode `start`. Se ele responder
  `PLANO INDISPONIVEL` (PRMake fora do ar ou sem o recurso), siga a analise normalmente: os comandos do plano
  viram no-op, e a analise vai para a timeline como antes. Se ja existir um plano
  aberto (pendente, em andamento, pausado ou com falha), o `start` **retoma** esse plano e imprime as etapas
  com status, checkpoints e arquivos: continue da **primeira etapa nao concluida**, usando o checkpoint, e
  rode `pull` se a pasta do card nao tiver os arquivos (outra maquina/sessao). So use `start ... --new` se o
  usuario pedir uma analise nova do zero.
- **Refine o plano** depois de ler o card (passo 2): ajuste titulos/descricoes das etapas ao caso concreto
  (a descricao diz *o que vai ser feito*), remova as que nao se aplicam (ou marque `cancelled` com motivo) e
  acrescente as que surgirem — ex.: se o usuario pedir a **correcao**, inclua etapas como `corrigir-codigo`,
  `validar-correcao`, `gerar-pr`. Keys: minusculas, numeros, `-`/`_`, estaveis (nunca renomeie uma key).
- **Em cada etapa:** `step running` → `log` a cada acao relevante (arquivo lido, hipotese, achado, decisao,
  consulta feita — frases curtas, markdown ok; `finding` para achados, `decision` para escolhas, `warning`/
  `error` para problemas) → salve os arquivos na pasta do card e rode `sync <card> <key>` → `checkpoint`
  (o suficiente para outra sessao continuar) → `step completed`.
- **Entre etapas (e antes de qualquer coisa demorada):** `control`. Exit **10** = o usuario pausou: rode
  `wait` (bloqueia ate ~9 min; repita enquanto devolver 10, ou avise o usuario que ficou pausado e pare).
  Exit **11** = cancelado/concluido pela tela: pare, diga ao usuario e nao envie mais nada. Etapas listadas
  em "etapas canceladas (pular)" foram canceladas pelo usuario: **pule-as**.
- **Falha de rede** nao interrompe a skill: o script guarda o envio numa fila local e reenvia na proxima
  chamada (`flush` forca). Erro 400 (ex.: plano cancelado) interrompe — leia a mensagem.
- **Fim:** `status completed` com o resumo (a analise). Em erro que impede continuar: `status failed "motivo"`
  (retomavel depois com `start`).
- PII: o que vai para o plano fica visivel no PRMake — mesmo cuidado da timeline (sem dados pessoais
  desnecessarios; evidencias de consulta resumidas).

Etapas padrao (o `start` cria estas se voce nao passar outras): `identificar-card`, `coletar-dados`,
`investigar-codigo`, `consultar-ambiente` (opcional — cancele com motivo se nao precisar), `causa-raiz`,
`montar-analise`, `publicar`.

## Fluxo

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

O manifesto informa `title`, `state`, `workItemType` e `isBug`. Leia `description.txt` para entender
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

Plano: etapa `publicar` (running → completed) e, por fim, conclua o plano com o resumo:
```bash
bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh status <card> completed "" "$CARD_DIR/analises/analise-inicial.md"
```

### 6. Reportar
Informe ao usuario: card, tipo (Bug/US), titulo, que o plano de execucao esta no card no PRMake, um resumo curto da causa raiz provavel e dos pontos
suspeitos, e que a analise foi publicada na timeline. Em caso de HTTP 401/403, avise que o token
PRMake expirou (`~/.claude/prmake-token.txt` ou `PRMAKE_TOKEN`).

## Notas

- A analise e **somente leitura** no codigo — esta skill nao altera arquivos nem cria PR. Se o usuario
  pedir para seguir com a **correcao** na mesma sessao, acrescente as etapas de correcao ao mesmo plano
  (`steps`) e continue mandando o andamento — o plano volta para "em andamento" sozinho. Para gerar
  o PR/RCA depois da correcao, use a skill `gerar-prmake`.
- Se, durante a investigacao, voce quiser registrar marcos intermediarios (hipotese levantada, causa
  encontrada), pode postar entradas curtas adicionais na timeline com `prmake-timeline` antes da analise
  final — esta skill se integra com `prmake-timeline`.
- `card.json` traz mais campos (severidade, area, assigned to) caso precise de contexto extra.
