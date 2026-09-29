---
name: gerar-prmake
description: Gera e publica um Pull Request no sistema PRMake a partir da branch atual (hotfix/<card> ou bugfix/<card>), montando a descrição e o RCA via IA com base nos dados do card no Azure DevOps, nos commits/diff da branch e no prompt configurado; ao final insere o Root Cause no card do DevOps e posta um resumo não-técnico (PT/EN) na discussion do card. Use quando o usuário pedir para gerar/criar um PR no PRMake, "gerar pr", "montar pull request" ou publicar o root cause de um card.
---

# gerar-prmake

> **Atualização:** instalada e atualizada pelo PRMake (tela *Skills*). Antes de usar, rode
> `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet gerar-prmake 2>/dev/null || true` — se imprimir
> "Skills do PRMake atualizadas", releia esta SKILL.md. A fonte fica no repositório do PRMake (`skills/gerar-prmake`).

Automatiza a criação de um Pull Request no PRMake (`https://api.softhouse.app.br`), a inserção do
Root Cause Analysis (RCA) no card do Azure DevOps e a postagem de um resumo **não-técnico** (PT-BR +
EN-US) na **discussion** do card, a partir da branch atualmente em checkout.

> **Esta skill vive no nível do usuário** (`~/.claude/skills/gerar-prmake`), portanto está disponível
> em qualquer projeto/repositório da máquina. Os tokens também ficam no nível do usuário (ver abaixo).

## Configuração e tokens

### PRMake
- **API base:** `https://api.softhouse.app.br/api/v1` (Cloud Run) — autenticação via header `x-api-key: <TOKEN>`.
  Sobrescreva com env `PRMAKE_BASE`. **Não use `prformapi.runasp.net`**: é a API antiga (MonsterASP), com código
  anterior à feature 0001, apontando para o mesmo banco já migrado — o `POST /PullRequest` dela dá **HTTP 500**.
- **Token:** resolvido nesta ordem: variável de ambiente `PRMAKE_TOKEN`, senão `~/.claude/prmake-token.txt`
  (nível de usuário), senão `.claude/prmake-token.txt` do projeto. Para trocar, edite `~/.claude/prmake-token.txt`
  ou exporte `PRMAKE_TOKEN`. Os scripts já fazem essa resolução automaticamente.
- **`userId`:** extraído do claim `ExternalId` de dentro do próprio JWT (feito pelo script de publicar).
- **`repositoryId`** (usado nos endpoints de GitHub — commits, diff e abertura do PR): por padrão é derivado dinamicamente do `git remote origin` do repositório de onde a skill é chamada (nome após a última `/`, sem `.git` — ex.: `revamp-BOS`). Se não houver remote, cai no fallback `edv-solvace`. Passe o argumento `[repository]` explicitamente apenas para sobrescrever.
- **`formId`:** `1` por padrão (env `FORM_ID` para sobrescrever).

### Azure DevOps (comentário na discussion)
- **API:** chamada **direta** ao Azure DevOps (`https://dev.azure.com/<org>/<project>/_apis/...`).
- **Autenticação:** Personal Access Token (PAT) via `Authorization: Basic base64(":<PAT>")`. O comentário
  é escrito **como o dono do PAT** (ou seja, explicitamente como o usuário).
- **PAT:** resolvido nesta ordem: env `AZURE_DEVOPS_TOKEN`, senão `~/.claude/azure-devops-token.txt`
  (nível de usuário). Para trocar, edite esse arquivo ou exporte `AZURE_DEVOPS_TOKEN`.
- **Defaults** (sobrescreva por env se necessário): `AZURE_ORG=solvacelabs`,
  `AZURE_PROJECT=Solvace Product Improvement`, `AZURE_API_VERSION=7.1-preview.4`.

## Fluxo

### 1. Identificar o card pela branch
Rode `git rev-parse --abbrev-ref HEAD`. A branch deve ser `hotfix/<numero>` ou `bugfix/<numero>`:
- `branchPrefix` = `hotfix/` ou `bugfix/` (com a barra); `card` = número após o prefixo.
- Se a branch não seguir o padrão (ex.: `master`), **pare** e peça ao usuário a branch correta ou o
  número do card + prefixo. Se o usuário passar um número em ``, use-o.

### 2. Buscar dados (somente leitura) com o script de fetch
```bash
bash ~/.claude/skills/gerar-prmake/scripts/prmake-fetch.sh <card> <branchCompleta> [repository]
# ex.: bash ~/.claude/skills/gerar-prmake/scripts/prmake-fetch.sh 54969 hotfix/54969
```
O script busca o card no DevOps, os prompts (id=3), os commits da branch e os diffs, e grava em
`/tmp/prmake/`:
- `prompt.txt` — `PromptBug` (se o card for **Bug**) ou `PromptUS` (caso contrário);
- `description.txt` — `ReproSteps` (Bug) ou `System.Description` (US), já sem HTML;
- `diff.txt` — diff concatenado dos commits do card (`githubCommitDiff`);
- `summary_prompt.txt` — prompt do resumo não-técnico definido pelo admin (`BugSummaryPrompt` do plugin
  AI Configurations, feature 0011 do CIME). Vazio para US ou se o plugin ainda não tiver o campo;
- `card.json`, `commits.json` — dados brutos para conferência.

O manifesto impresso informa `isBug=1|0` e quantos commits foram selecionados. Se `isBug=0`, não
haverá RCA e o passo 6 (root cause) é pulado — mas o comentário PT/EN ainda é gerado.

### 3. Gerar o texto do PR seguindo o prompt
Leia `/tmp/prmake/prompt.txt` e substitua:
- `{cardNumber}` → o número do card;
- `{description}` → conteúdo de `/tmp/prmake/description.txt`;
- `{githubCommitDiff}` → conteúdo de `/tmp/prmake/diff.txt`.

Você (Claude) é o gerador de IA. Siga **exatamente** as regras do prompt:
- Texto **100% em inglês**, **Markdown**, títulos em **negrito**, linha em branco entre seções.
- Nenhuma menção de que foi gerado por IA — apenas o conteúdo, direto ao ponto.
- **Bug:** inclua a seção de Pull Request **e** a de RCA, com o RCA delimitado por `<RCA>` ... `</RCA>`.
- **User Story:** apenas a seção de Pull Request (sem RCA).

Escreva o texto completo em `/tmp/prmake/pr_generated.md`.

> **Foco no bug do card.** Cada card e um ticket aberto pelo cliente, e tratamos somente ele. A
> **descricao do PR**, o **RCA** e o **resumo nao-tecnico** (passo 4b) falam **apenas do caso
> relatado**. **Nao mencione** outros usuarios/registros afetados, contagens de casos no ambiente,
> problemas sistemicos nem correcoes em lote, mesmo que a analise do card tenha encontrado isso.
> Se houver algo assim, avise o usuario so no chat.

### 4. Separar descrição e root cause
```bash
# rootCause = conteudo entre <RCA> e </RCA>
awk '/<RCA>/{f=1;next} /<\/RCA>/{f=0} f' /tmp/prmake/pr_generated.md > /tmp/prmake/rca.txt
# description = tudo menos o bloco <RCA>...</RCA>
awk 'BEGIN{s=1} /<RCA>/{s=0} s==1{print} /<\/RCA>/{s=1}' /tmp/prmake/pr_generated.md | sed '/<\/RCA>/d' > /tmp/prmake/desc.txt
```
Para User Story, `rca.txt` fica vazio.

### 4b. Gerar o resumo não-técnico (PT-BR + EN-US) para a discussion
Se `/tmp/prmake/summary_prompt.txt` **não estiver vazio**, ele é o prompt oficial (o mesmo usado pela
tela do PRMake): substitua `{cardNumber}`, `{title}` (título do card), `{reproSteps}` (`description.txt`),
`{description}` (`desc.txt`), `{rootCause}` (`rca.txt`) e `{context}` e siga-o, mantendo o formato abaixo.
`{context}` = markdown com as seções `## Situação do card` (número, título, tipo, estado e área de
`card.json`, e `Evidência de solução: SIM (...)` citando o que existe entre PR/descrição/RCA/commits),
`## Problema relatado` (`description.txt`), `## Descrição do PR` (`desc.txt`), `## Root cause` (`rca.txt`)
e `## Alterações de código` (`diff.txt`). Vazio: use as regras desta seção.

**Nunca invente**: use só o que está nos arquivos do passo 2 e no texto gerado no passo 3. Se não houver
evidência de solução (sem diff/descrição/RCA), não diga que o problema foi corrigido.

Além do texto técnico, **gere você (Claude) um resumo não-técnico** que dê ao usuário um panorama
do **problema e da solução adotada** em linguagem de negócio/usual — sem jargão técnico, sem nomes
de arquivos/métodos, sem código. Foque no impacto para o usuário e no que passou a funcionar.

Escreva em `/tmp/prmake/comment.md` seguindo **exatamente** este padrão (divisor MD = `---`):

```markdown
**PT**

---

<texto não-técnico em português do Brasil, foco em negócio/usuário>

**EN**

---

<less technical text in en-US, business/user focused — same message as the PT block>
```

Regras do resumo:
- Dois blocos: **PT** (português BR) e **EN** (en-US), com o mesmo conteúdo em cada idioma.
- Cada bloco começa com o rótulo em negrito, depois um divisor `---`, depois o texto.
- Linguagem simples e orientada ao usuário/negócio; explique o que acontecia de errado (ou o que
  foi entregue, no caso de US) e o que muda para o usuário agora. Nada técnico.
- Markdown válido (o script converte para HTML antes de postar na discussion).

### 5. Confirmar com o usuário
Mostre a `desc.txt`, a `rca.txt` e o `comment.md` gerados e **peça confirmação** antes de publicar —
o passo 6 grava o card no PRMake, escreve o root cause no card e posta o comentário na discussion do DevOps
(ações difíceis de reverter). Nunca publique sem o "ok".

Na mesma confirmação, **pergunte se deve abrir também o PR no GitHub** e para qual branch de destino
(ex.: `qa`, `master`). Só abra se o usuário disser que sim — cria um PR real no GitHub.

### 6. Publicar (após confirmação)
```bash
COMMENT_FILE=/tmp/prmake/comment.md \
  bash ~/.claude/skills/gerar-prmake/scripts/prmake-publish.sh <card> <branchPrefix> /tmp/prmake/desc.txt /tmp/prmake/rca.txt [repository]
# US (sem RCA): passe "" no lugar do arquivo de rca
# Abrir também o PR no GitHub (se o usuário pediu):
#   OPEN_GITHUB_PR=1 TARGET_BRANCH=qa COMMENT_FILE=... bash .../prmake-publish.sh ...
```
O script:
1. faz `POST /PullRequest` com **só os dados do card** (`cardNumber`, `userId`, `formId`, `description`,
   `rootCause`) — desde a feature 0001 há **um registro por card**; branch e repositório pertencem a cada PR
   do GitHub, não ao registro;
2. com `OPEN_GITHUB_PR=1`, faz `POST /PullRequest/<card>/github` (`repositoryId`, `branchPrefix`,
   `branchName`=`<card>`, `targetBranch`=`TARGET_BRANCH`, `title`, `description`, `draft`, `userId`): abre o PR
   `<prefix><card> → TARGET_BRANCH`, registra no card e na timeline. Título padrão `AB#<card> <DESTINO>` (igual
   à tela; sobrescreva com `PR_TITLE`), `PR_DRAFT=true` abre como rascunho. Se já houver PR aberto para o mesmo
   head→base, a API devolve o existente (`alreadyExisted: true`);
3. havendo RCA, converte-o para HTML5 e faz `POST /Azure/card/<card>/rootcause`;
4. havendo `COMMENT_FILE`, converte-o para HTML5 e faz `POST /PullRequest/<card>/summary`
   (`summary` em Markdown + `html`): a API publica na **discussion** do card — criando o comentário ou
   **atualizando o mesmo** já publicado (pela skill ou pela tela) — e grava o resumo no PRMake, onde ele
   fica visível/editável em **Ações DevOps → Resumo não técnico**. Se a API responder 404 (versão sem a
   feature 0011), cai no `azure-comment.sh` (API direta do DevOps, comentário novo); `LEGACY_COMMENT=1`
   força esse caminho antigo. (A tela também permite só **salvar** sem publicar — `publish:false`; a skill
   sempre publica.)
5. **sempre** preenche os campos custom de classificação no card via `azure-fields.sh`
   (`PATCH .../workitems/<card>`, `application/json-patch+json`), a menos que `SKIP_FIELDS=1`.

Ele imprime os HTTP codes, o `id` do registro do card, o número/URL do PR no GitHub (se aberto), o
`commentId` da discussion (`summaryCommentId`) e o `rev` do card após os campos.

> **Campos custom de classificação (sempre preenchidos).** Ao publicar, além do root cause e do
> comentário, o card recebe (JSON Patch) os campos abaixo — reference names confirmados via API,
> valores validados contra as picklists do tipo Bug:
> - `Custom.ResolutionType` (**Resolution Type**) = `Code Fix`
> - `Custom.GeneralClassification` (**General Classification**) = `Code`
> - `Custom.Classification` (**Classification**) = `Code Required - Code Defect`
>
> Sobrescreva qualquer um por env (`RESOLUTION_TYPE`, `GENERAL_CLASSIFICATION`, `CLASSIFICATION`);
> defina um deles como `""` para não tocar naquele campo, ou `SKIP_FIELDS=1` para pular a etapa toda.
> Os valores precisam existir na picklist do campo (do contrário o Azure recusa o PATCH).

> **Textos enviados ao DevOps vão em HTML5.** Tanto o root cause quanto o comentário da discussion
> são convertidos de Markdown para HTML5 por `scripts/md2html.py` antes do POST (com estilos inline:
> espaçamento entre parágrafos, divisores com respiro e trechos em `código` com fundo cinza). A
> **descrição do PR no PRMake continua em Markdown** — a conversão vale só para o que vai ao DevOps.

> **Iterar sem poluir o card:** para *atualizar* um comentário já postado em vez de criar outro, passe
> o `commentId` como 3º argumento (ou env `COMMENT_ID`) para `azure-comment.sh` — ele faz `PATCH` no
> lugar de `POST`. Ex.: `bash ~/.claude/skills/gerar-prmake/scripts/azure-comment.sh <card> /tmp/prmake/comment.md <commentId>`.

### 7. Reportar
Informe: card, tipo (Bug/US), branch, `id` do registro no PRMake, o link do PR no GitHub (se aberto), se o RCA foi gravado no DevOps, se o
comentário PT/EN foi postado na discussion (`commentId`) e se os campos custom de classificação
foram preenchidos (Resolution Type / General Classification / Classification). Em caso de HTTP 401 no PRMake, o token
PRMake expirou (`~/.claude/prmake-token.txt` ou `PRMAKE_TOKEN`); HTTP 403 com `PERSONAL_INTEGRATION_REQUIRED`
nas chamadas de GitHub/Azure = o usuário precisa salvar a própria chave em **Minhas integrações** no app;
HTTP 500 no `POST /PullRequest` = confira se está usando o host novo (`api.softhouse.app.br`); no Azure, o PAT expirou/sem escopo
(`~/.claude/azure-devops-token.txt` ou `AZURE_DEVOPS_TOKEN`).

## Notas de robustez
- O endpoint de diff é lento: o script busca um commit por vez com `--max-time` e concatena via `jq`
  (evita loops de shell que travam).
- Seleção de commits: por padrão os que têm o número do card no título; se nenhum, os commits do topo
  até o primeiro merge.
- Os tokens (`~/.claude/prmake-token.txt`, `~/.claude/azure-devops-token.txt`) ficam no nível de
  usuário, fora de qualquer repositório, com permissão `600`.
