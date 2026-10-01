# Consultas: codigo legado x revamp, Cognito e SQL Server (somente leitura)

Lido quando a investigacao precisar ir alem da Base Solvace: localizar codigo, consultar o Cognito ou o banco.

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
inconsistencia, config por tenant, usuario/planta/area), consulte o banco em **modo somente leitura** com
`sql-query.sh`.

**1. Teste o acesso cedo** — logo que souber o host/banco do cliente, antes de investigar a fundo (assim a falta de
VPN/permissao aparece no comeco, nao no fim):
```bash
bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod -d <database> --ping
```
Exit 0 = ok · 2 = sem conexao (**VPN desligada**, o mais comum) · 3 = login recusado (credencial) · 4 = sem
`~/.claude/sqlserver-credentials.json`. **O Claude Code barrou o comando** (permissao/auto mode) = falta a regra de
permissao: a autorizacao dada no PRMake nao vale para o Claude Code. Em qualquer falha:
`bash $PLAN block <card> consultar-ambiente "<o que fazer>"` — ex.: "Ligar a VPN e clicar em *Ja resolvi*", "Liberar
no Claude Code a regra `Bash(bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh:*)` (ou rodar
`bash ~/.claude/skills/.prmake/prmake-skills.sh permissions`) e clicar em *Ja resolvi*", sempre com a alternativa
"ou rode `scripts/00_consulta.sql` e cole o resultado num comentario do plano". Diga o mesmo no chat e rode o vigia.

**2. Consulte** — sempre com o **caminho literal** e **num comando so** (sem pipe, `&&`, `;` ou variavel antes): e
assim que a regra de permissao do Claude Code casa e a consulta roda sem prompt. SQL com mais de uma linha vai em
arquivo (`-f`), salvo em `$CARD_DIR/scripts/00_*.sql` (o usuario tambem consegue rodar e colar o resultado):
```bash
# aliases de host: prod | prod3 | prod4 (ou o hostname RDS completo)
bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod3 -d <database> -q "SELECT TOP 20 Id, Name, Status FROM dbo.SomeTable WHERE ..."
bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host prod -d <database> -f <card-dir>/scripts/00_consulta_somente_leitura.sql
```

Opcoes: `-d/--database` (default `master`), `--max-rows` (default 1000), `--timeout` (s), `--json`, `--ping`.

**3. Sem o banco, a analise nao fecha.** Se a hipotese depende de dados (o que confirma a causa ou decide a solucao
esta no banco), a etapa `consultar-ambiente` **nao pode ser cancelada** e a analise **nao pode ser concluida** nem
virar plano de correcao com a verificacao empurrada para depois (ex.: uma etapa `confirmar-dados` na correcao): a
etapa fica `block`eada esperando o usuario. So siga sem o banco se o usuario responder **explicitamente** que e para
seguir assim (`ask` com as opcoes "Vou liberar o acesso" / "Seguir sem o banco") — e entao a analise diz, na secao
**Banco de dados**, que nao foi consultado e o que ficou como hipotese.

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
