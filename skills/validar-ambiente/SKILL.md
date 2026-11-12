---
name: validar-ambiente
description: Valida a demanda de um card num ambiente Solvace (feature 0092) — entra no ambiente pelo navegador da maquina (Chrome sem janela, com a senha guardada SO nesta maquina), segue o roteiro do que mudou e grava a sequencia de prints mostrando o funcionamento como evidencia na sub-tarefa `env-validation` do item de validacao no PRMake. Sabe achar a URL do ambiente (configuracao ValidationEnvironments e banco corporativo), as plantas e os bancos de cada planta (TB_WCM_SITE) e o admin da planta (admin<sigla>@solvacelabs.com); sem a senha, orienta a pessoa a grava-la no proprio terminal. DEMO e producao sao somente leitura (o navegador bloqueia gravacoes). Tambem REPRODUZ o bug no ambiente (feature 0093, sub-tarefa `env-reproduction`): por padrao no ambiente relatado pelo cliente no card (campos Environment/Site), ou noutro indicado, com os prints do erro. Use quando a sub-tarefa for `env-validation` ou `env-reproduction`, quando pedirem para "reproduzir o bug no ambiente/no cliente/em qa", quando o prompt pedir para "validar em <ambiente>", "validar em dev/qa/sandboxefeso/sandboxrt/producao", "tirar prints da validacao", "gravar as evidencias da validacao", ou para criar o item de validacao de um card.
---

# validar-ambiente

Valida **uma demanda num ambiente** e deixa a prova: uma sequencia curta de prints, na ordem, mostrando que funciona
(ou o que falhou), gravada como **evidencia da sub-tarefa** do item de validacao. Instalada e atualizada pelo PRMake
(fonte: `skills/validar-ambiente`). Token: `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
ENV=~/.claude/skills/validar-ambiente/scripts/env.sh   # envs · resolve · plants · admin · user · has-password · browser · evidence
AB=~/.claude/skills/analisar-bug/scripts               # sql-query.sh (so leitura), prmake-plan.sh
```
Rode sempre com o caminho literal (`bash ~/.claude/skills/validar-ambiente/scripts/env.sh ...`), um comando por vez —
e assim que a permissao do Claude Code casa (`prmake-skills.sh permissions`).

**O navegador e este script, pelo Bash** — nao precisa de ferramenta de navegador nem de MCP (o executor so tem Bash e
o MCP do PRMake, e e o suficiente). Nunca conclua "a sessao nao tem navegador": rode `bash $ENV setup` (cria o venv e
instala o Playwright uma vez; usa o Chrome da maquina) e so um **exit 2** do `setup`/`browser start` significa que falta
o Chrome/Playwright. Skill ausente (`env.sh` nao existe) → `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet
validar-ambiente` e siga. **`user`/`plant` vazios sao normais**: o `env.sh` escolhe quem entra — o usuario que **ja
tem senha gravada nesta maquina** para o ambiente (ex.: `admin@ewcm.com.br`, que entra em qualquer planta) vence o admin
da planta — nunca pare nem pergunte por eles.

## Autonomia: voce acha o caminho, a pessoa so entra com o que so ela tem
O executor roda sozinho; cada `wait` faz a pessoa voltar a tela e clicar *Ja resolvi*. **`wait` so para**: senha que
falta / login recusado / maquina sem navegador (o `env.sh` ja avisa), ambiente fora do ar, mudanca ainda nao publicada no
ambiente (validacao) e dado de negocio que so uma pessoa pode criar num ambiente somente leitura. **Nunca** `wait` para
perguntar URL, caminho ou menu de uma tela, qual usuario/planta/idioma usar, qual registro abrir, nem para "aceitar uma
evidencia alternativa" — isso e trabalho seu (secao "Achar a tela") e decisao registrada no resultado. Vale mesmo que a
descricao da sub-tarefa diga "fique em espera se faltar acesso": acesso = senha/permissao, nao localizar a tela.
- **Nao da para mostrar na tela** (opcao que o codigo configura mas a tela nao exibe — ex.: `customRangeLabel` num
  `daterangepicker` com `singleDatePicker` e sem `ranges` —, modulo migrado para o revamp no ambiente, feature desligada):
  tire o print do que a tela mostra no lugar, cite o codigo que explica (`repo:arquivo:linha`) e conclua com `complete`,
  `reproduced: "parcial"` (ou `"nao"`) e uma linha **"Para decidir depois:"** no `result`. A sub-tarefa seguinte (proposta,
  decisao) segue com isso; quem revisa o item decide sem a fila parar.
- **Voltou do *Ja resolvi* com nota curta** (ex.: `1`): e a escolha da opcao 1 da sua ultima espera — siga por ela com o
  melhor esforco, sem repetir a pergunta. Nunca faca a mesma pergunta duas vezes.

## Achar a tela (sem perguntar)
1. **Base Solvace primeiro**: `prmake_base_search(card, "<tela> menu rota")` (itens de tela/UC trazem menu, rota e
   controller) e o codigo citado (`Controller`/`Action`, `.asp`). Anote o modulo e o nome da tela no menu.
2. **Pelo menu, como a pessoa faria** (o login cai em `/myworkspace/...`):
   - **Menu de modulos**: `snap` → botao `icone: apps` no canto superior direito → bloco do pilar (`item "Segurança e
     Meio Ambiente"`) → modulo (`item "Incidentes"`). Nome com **"- Novo"** = revamp (Angular); sem sufixo = legado.
   - **Legado .NET (solvace-core)**: `go /<modulo>/` (ex.: `/incident/`) abre a lista do modulo; o menu lateral sao
     icones (`snap` mostra `icone: ... @x,y`; clique e `snap` de novo). As telas internas carregam por **POST** dentro da
     moldura (`/incident/Report/Main`) — abrir a action direto por GET vem **em branco**: entre pelo modulo e pelo menu.
   - **Legado ASP**: as telas abrem no iframe `#framepag` de `systems/application/default.asp`. O `snap` lista os
     elementos de dentro como `frame=#framepag >> <alvo>` e `click`/`fill` acham sozinhos o que esta no iframe.
3. O `snap` ja nomeia: botao so de icone (`icone: apps @1384,12`), blocos clicaveis (`item "..."`), campo pelo rotulo
   (`textbox "Lugar"`) — use o alvo dele (`[data-pm="pmN"]` vale ate o proximo `snap`). Filtros do legado: botao
   `icone: fas fa-filter` abre a "Busca detalhada".
4. Tentou os tres caminhos e a tela nao existe no ambiente: registre o que tentou (menus, URLs, prints de onde chegou) e
   conclua como "Nao da para mostrar na tela" acima — sem `wait`.

## Regras que nao mudam
1. **Senha nunca passa pelo chat.** Ela fica so em `~/.claude/solvace-env-credentials.json`, gravada pela **pessoa, no
   terminal dela**: `bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set <ambiente> <usuario>`. Voce nao
   le esse arquivo, nao roda esse comando (ele recusa sem terminal) e nunca pede a senha. Senha colada na conversa:
   nao use, nao repita, diga que ficou exposta e que o certo e trocar e gravar pelo comando.
2. **DEMO jamais tem dado alterado** e producao de cliente e **somente leitura**: so navegar, abrir e consultar. A
   sessao desses ambientes bloqueia gravacoes na rede (aparece `BLOQUEADO (ambiente somente leitura)`); nunca tente
   contornar e **nunca edite os scripts da skill**. Excecoes, so rotas que guardam **preferencia/filtro do usuario** (nao
   dado de negocio): as da configuracao (`readOnlyAllow` geral e `allow` do ambiente — ex.: `user-report-preferences`)
   ja passam sozinhas (`LIBERADO`); outra rota assim so com **autorizacao explicita de uma pessoa para aquela rota**
   (resposta no PRMake/"Ja resolvi" com a nota, conversa do caso) → reabra a sessao com
   `bash $ENV browser start <amb> ... --allow '<trecho da rota>'` (ex.: `--allow user-report-preferences.asp`) e cite no
   relatorio a rota liberada e quem autorizou; se ela vale sempre, sugira incluir em `readOnlyAllow` (Configuracoes →
   Itens de trabalho → Validacao em ambiente). Criar/alterar dado de negocio nesses ambientes → `wait` explicando o que a
   pessoa precisa fazer ou valide num ambiente de teste (dev/qa/sandbox do cliente).
3. **Valide o que esta no ambiente.** Antes, confira que a mudanca chegou la (PR do destino do ambiente mesclado e
   publicado: dev ← `development`/`-dev`, qa ← `qa`/`-qa`, sandboxefeso/sandboxrt ← `release-version`/`hotfix-version`,
   producao ← `master`; `prmake_case_context`/`prmake_card`/`prmake_work_items`). Nao chegou → `wait` dizendo qual
   PR/deploy falta e **encerre a vez** (o executor retoma quando a pessoa agir).
4. Banco **somente leitura** (`sql-query.sh` so SELECT). Nada de comentario em codigo, commit ou PR aqui.
5. **Prints sem dado sensivel alem do necessario**: nada de tela de senha, token, dados pessoais que nao provem a
   validacao. Prefira a tela com o resultado (mensagem de sucesso, registro na lista, valor calculado).

## Onde esta cada coisa
- **Ambientes conhecidos**: `bash $ENV envs` (configuracao `ValidationEnvironments` do PRMake: chave, URL, usuario padrao,
  somente leitura, banco). `bash $ENV resolve <chave|url|cliente>` resolve qualquer um; fora da configuracao, procura
  no **banco corporativo** (`corporate` / `CORPORATE_SOLVACE_DB.TB_WCM_ENVIRONMENT`: nome, URL, `CATALOG_GLOBAL`) —
  ambiguo (ex.: "novelis" = sandbox e producao) lista as URLs: use a URL.
- **Plantas e bancos**: `bash $ENV plants <ambiente> [filtro]` (`TB_WCM_SITE` do banco global: `SITE_ACRONYM`,
  `SITE_NAME`, `INSTANCE_NAME` = banco local da planta). Host do banco: o da configuracao ou o servidor das credenciais
  desta maquina que tem o banco global (regra de hosts por cliente da `analisar-bug`).
- **Usuario**: `params.user` quando vier; senao **deixe o `env.sh` escolher** (`resolve` mostra `com senha nesta maquina`
  e `entrada padrao`): admin da planta se ele tiver senha aqui → usuario da configuracao com senha → **qualquer usuario
  com senha gravada para o ambiente** (a pessoa gravou de proposito: ex. `admin@ewcm.com.br` no sandboxefeso, que entra
  em todas as plantas) → so entao o admin da planta `admin<sigla>@solvacelabs.com` (`bash $ENV admin <ambiente> <sigla>`
  confere se existe/ativo). Entrou com usuario de varias plantas e a sub-tarefa tem `plant`: **troque a planta no
  seletor da tela** (topo, icone de local com o nome da planta) — nao peca o admin da planta. Bug: a planta do cliente
  que reportou; US: a que a pessoa indicou ou a que ja vier selecionada (nao pergunte).

## Fluxo da sub-tarefa `env-validation`
Params: `environment` (chave ou URL), `plant?`, `user?`, `scenario?` (o que validar), `expected?`.
1. `prmake_work_task(card, taskNumber, start)`.
2. **Roteiro**: `scenario`/`expected` da sub-tarefa; vazios → o que o card pede (`$CARD_DIR/dados/card-resumo.txt`,
   criterios de aceite da US, o "Como validar" da analise/plano). Escreva 3–8 passos curtos (onde ir, o que fazer, o
   que deve aparecer). Regra 3: a mudanca esta no ambiente?
3. **Ambiente e usuario**: `bash $ENV resolve <environment>`; usuario = `params.user` ou a `entrada padrao` que o
   `env.sh` escolhe (quem tem senha nesta maquina) — sem `--user`, o `browser start` ja usa essa.
4. **Abrir**: `bash $ENV browser start <amb> [--user <params.user>] [--plant <sigla>] --card <card> --task <taskNumber>`.
   Sempre com `--card`/`--task`: e o que liga a sessao ao item na Bancada — quem executa ve a tela ao vivo pelo botao
   "Ver tela ao vivo" (0095, so na maquina do executor, nada gravado). Nao abra com `--headed` para isso.
   - exit 4 (**sem senha**), 3 (**login recusado**) ou 2 (sem Chrome/Playwright): com `--task`, o proprio `env.sh` ja
     deixa a sub-tarefa **aguardando a pessoa na ferramenta** (Bancada/Execucao) com o que falta, a maquina e o comando
     exato (`prmake-skills.sh env-credentials set <amb> <email>`) + *Ja resolvi* — a saida diz `AVISADO NA FERRAMENTA`.
     Entao **encerre a vez** sem chamar `wait` de novo. Se a saida disser que nao conseguiu marcar, registre voce:
     `prmake_work_task(wait)` com a mesma orientacao. Sem `--task` (validacao fora de item): diga o comando no chat.
   - Abriu: anota a sessao (`<amb>-<email>` em minusculas, separado por `-`) e a pasta dos prints.
5. **Executar o roteiro**, passo a passo, olhando a tela:
   - `bash $ENV browser snap <sessao>` → titulos, elementos clicaveis com o alvo sugerido e o texto; o arquivo da
     tela (`_snap.jpg`) voce abre com `Read` quando precisar ver.
   - `go <sessao> <url|/caminho>` · `click <sessao> "<alvo>"` · `fill <sessao> "<alvo>" "<valor>"` ·
     `select <sessao> "<alvo>" "<opcao>"` · `press <sessao> Enter` · `wait <sessao> "<alvo>|ms"` · `text <sessao>`.
     Alvo = o do `snap` (`#id`, `[formcontrolname=...]`, `text=Salvar`, `role=button[name="Salvar"]`) ou o texto visivel.
   - **Print e prova, nao diario de navegacao.** Um `shot` por **resultado esperado** do roteiro — a tela onde o criterio
     aparece (data no formato certo, mensagem de sucesso, registro na lista) —, mais no maximo um da tela de partida se
     ajudar a entender: `bash $ENV browser shot <sessao> "<nome-curto>" --caption "<o que prova>"` (`--full` = pagina
     inteira). Login, menus, abas, foco, tentativas e telas intermediarias: so `snap` (voce ve, nao vira evidencia).
     Tipico: **2–5 prints**; teto `maxShots` da configuracao (8). Tela igual a um print ja tirado: o `shot` avisa
     `IGUAL` e nao grava outro. Abra (`Read`) os que provam o resultado antes de concluir.
   - **Rodada nova** (depois do *Ja resolvi*): nao refaca o que ja foi provado — os prints da rodada anterior estao na
     mesma pasta; tire so os dos passos que faltavam.
   - Algo nao funciona: tire o print do erro, confira se nao e o roteiro (outro caminho na tela) e so entao conclua
     como **falha de validacao**.
6. **Fechar**: `bash $ENV browser stop <sessao>`.
7. **Evidencias**: escreva `validacao-<amb>.md` na pasta dos prints (ambiente, URL, usuario, planta, data, roteiro e,
   por criterio, o print que o prova — nome do arquivo + o que mostra; veredito **aprovado / reprovado** com o motivo). O
   `evidence` sobe **so os prints citados no relatorio** (sem repetir imagem igual; acima do teto ele recusa — escolha):
   `bash $ENV evidence <card> <item> <taskNumber> <pasta-dos-prints> --report <pasta>/validacao-<amb>.md`.
8. **Concluir**:
   - Aprovado → `prmake_work_task(complete, result: "<veredito em 2–5 linhas: o que foi validado, onde, como quem, prints
     #1–#n>", output: {environment, url, user, plant?, shots: "<n>", verdict: "ok"}, undo: {action: "none", params:
     {reason: "so leitura/validacao"}})`.
   - Reprovado → `prmake_work_task(fail, result: "<o que falhou, em qual passo, print #n>")` — os prints ja estao na
     sub-tarefa; nao siga para os outros ambientes do item sem a pessoa decidir.

## Reproduzir o bug (sub-tarefa `env-reproduction`, 0093)
Params: `environment` (`cliente` = o relatado no card — padrao; ou chave/URL de outro ambiente), `plant?`, `user?`,
`steps?` (vazio = os passos do card), `expected?`. Mesmas regras e mesmo navegador da validacao.
1. `prmake_work_task(card, taskNumber, start)`.
2. **Ambiente do cliente**: `bash $ENV card-env <card>` — le os campos `Environment`/`Site` do card (busca o card se faltar)
   e as URLs `*.solvacelabs.com` dos passos/discussion; mostra ambiente, planta (sigla) e o admin da planta. `environment`
   = `cliente` → `browser start cliente --card <card> --task <n>` ja usa isso (planta do card → `admin<sigla>`).
   Card sem ambiente → use `qa` (ou o ambiente que a analise/sub-tarefa citar) e diga isso no relatorio — sem `wait`.
3. **Passos**: `params.steps`, senao os passos para reproduzir do card (`$CARD_DIR/dados/description.txt`; Bug = Repro
   Steps) + prints/evidencias do card quando ajudarem (`prmake_work_item_evidence`). Anote a versao/tela/usuario do relato.
4. **Abrir** como no fluxo da validacao (sem senha / login recusado / sem navegador → o `env.sh` ja avisa na ferramenta;
   encerre a vez). Reproduza **com o perfil mais proximo do relatado** que der (admin da planta do card).
5. **Seguir os passos** com `snap`/`click`/`fill` (tela que voce nao sabe onde fica: "Achar a tela"; nao da para
   mostrar: "Nao da para mostrar na tela" — os dois sem `wait`); `shot` so do **ponto de partida do fluxo relatado e do erro**
   (mensagem, tela quebrada, valor errado, `Invalid date`...) — e de um passo intermediario so se ele mudar a tela de forma
   que importe para entender. Navegacao: `snap`. Tipico: 2–4 prints. Erro de console/rede ajuda: anote a URL e o texto do erro no relatorio.
6. **Somente leitura (producao/DEMO)**: reproduza ate o passo que grava; se o bug so aparece ao salvar/alterar, **nao
   contorne o bloqueio** — tire o print do ponto em que parou e repita a parte que grava no **sandbox do cliente**
   (`bash $ENV resolve sandbox<cliente>`/URL do sandbox; ex.: `sandboxmondelez`) ou em `qa`, dizendo isso no relatorio.
7. **Fechar e evidencias**: `browser stop`; relatorio `reproducao-<amb>.md` (ambiente, URL, usuario, planta, versao se
   visivel, passos seguidos, por print o que mostra, **resultado: reproduziu / nao reproduziu / parcial** e o que
   difere do relato) → `bash $ENV evidence <card> <item> <n> <pasta> --report <pasta>/reproducao-<amb>.md`.
8. **Concluir sempre com `complete`** (nao reproduzir tambem e resultado): `result` = 2–5 linhas (reproduziu? em qual
   passo, a mensagem, prints #n; nao reproduziu: o que foi tentado e a diferenca provavel — dado, permissao, planta,
   versao), `output: {environment, url, user, plant?, shots, reproduced: "sim|nao|parcial"}`, `undo: {action: "none",
   params: {reason: "so leitura/reproducao"}}`. `fail` so quando nao deu para tentar (ambiente fora do ar etc.).
Com plano de analise aberto, cite o resultado na analise (secao Reproducao) — o item nao muda o plano.

## Criar o item de reproducao
- MCP: `prmake_env_reproduction(card, environments?, plant?, user?, steps?, expected?, execute?)` — `environments`
  vazio/`cliente` = o ambiente relatado (padrao); outro e opcional (ex.: `"cliente, qa"`). So Bug.
- Ferramenta: `prmake_tool_run(card, "reproduzir-bug-no-ambiente", {"environments": "cliente"})`; na tela: Ferramentas →
  **Reproduzir o bug no ambiente**, ou `/reproduzir-bug-no-ambiente` na conversa do Especialista.

## Criar o item de validacao (padrao do PRMake)
Toda entrega que **altera** algo e precisa ser validada ganha um **item de validacao** (tipo `validation`), com **uma
sub-tarefa por ambiente** — ex.: dev, qa, sandboxefeso (homologacao de release) ou sandboxrt (release/hotfix) e a
producao do cliente do bug (Bug) ou de um cliente especifico (US). A pessoa tambem pode pedir um ambiente especifico.
- MCP: `prmake_env_validation(card, environments: "dev, qa, mondelez", plant?, user?, scenario?, expected?, execute?)`
  — mesma checagem de duplicidade dos itens (`prmake_work_items` antes; ja existe → use o existente). Sem
  `environments` = os sugeridos para o tipo do card (`defaults` da configuracao).
- Ferramenta: `prmake_tool_run(card, "validar-em-ambiente", {"environments": "...", "plant": "COR", "scenario": "..."})`.
- Na tela: Ferramentas → **Validar em ambiente**, ou `/validar-em-ambiente` na conversa do Especialista.
O item nasce pendente; roda quando a pessoa (ou o especialista, a pedido) clica em **Executar**. Cada sub-tarefa roda
este fluxo; prints e relatorio ficam na sub-tarefa (galeria "Evidencias").

## Exemplo — card 77434, "validar em QA e na producao da Mondelez (planta COR)"
1. `prmake_env_validation(card: "77434", environments: "qa, mondelez", plant: "COR", scenario: "Abrir o 5W2H do plano 12 e
   salvar com anexo; o anexo aparece na lista")` → item #120 com #121 (QA) e #122 (Producao Mondelez).
2. #121: `resolve qa` → `browser start qa --card 77434 --task 121` (admin@ewcm.com.br) → roteiro com 6 prints →
   `evidence 77434 120 121 ~/.prmake/cards/77434/validacao/qa-121 --report .../validacao-qa.md` → `complete`.
3. #122: `admin mondelez COR` → `admincor@solvacelabs.com` (nenhum outro usuario com senha para a mondelez), sem senha
   → o `env.sh` deixa a espera com o comando
   `env-credentials set mondelez admincor@solvacelabs.com`; a pessoa grava e clica *Ja resolvi* → `browser start
   mondelez --plant COR ...` (somente leitura: so consulta o plano 12 e o anexo) → prints → `complete`.
