---
name: analisar-bug
description: Faz a analise (triagem tecnica) de um bug a partir do card no PRMake/Azure DevOps e do codigo do repositorio, publica a analise na Timeline, propoe solucoes perguntando ao usuario (responde no PRMake ou no Claude) e monta e executa o plano de correcao (codigo, PRs por repositorio seguindo o fluxo de branches Solvace, chamados) — so abre PRs, nunca faz merge. Tudo vai para o plano de execucao no PRMake em tempo real (o usuario acompanha, pausa, continua ou cancela pela tela do card; se a sessao cair, retoma de onde parou). Usa a Base Solvace (engenharia reversa + regras de negocio do Knowledge Center) antes de vasculhar codigo. Descobre o card pela branch atual (hotfix/<card> ou bugfix/<card>) ou por um numero informado. Considera os comentarios, imagens e arquivos que o usuario anexou no plano pelo PRMake. Use quando o usuario pedir para "analisar bug", "fazer analise inicial", "triagem de bug", "investigar o card", "retomar o card" ou similar — e tambem quando ele pedir para ver/analisar um anexo, imagem ou comentario do plano de um card no PRMake ("veja a imagem 2 do card 74519", "olha o anexo print.png", "leia o comentario 3").
model: opus
---

# analisar-bug

Leva um bug **da analise a correcao**, em duas fases, cada uma com o seu plano de execucao no PRMake:
1. **Analise** — repro steps + comentarios do usuario, Base Solvace, codigo, causa raiz provavel, analise na
   Timeline e **solucoes propostas como perguntas ao usuario**.
2. **Correcao** — com as respostas, **plano de correcao** proprio do caso (codigo, PRs por repositorio, chamados,
   configuracao, orientacao ao cliente) e execucao da sua parte. **Voce so abre PRs — nunca faz merge.**

Instalada e atualizada pelo PRMake (fonte: `skills/analisar-bug` no repositorio do PRMake — nao edite a copia
instalada). Depende da skill `base-solvace` (instalada junto). Token: env `PRMAKE_TOKEN` ou
`~/.claude/prmake-token.txt` (HTTP 401/403 = token expirou).

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh    # plano de execucao (lista completa: ref.sh plano comandos)
REF=~/.claude/skills/analisar-bug/scripts/ref.sh              # referencias por SECAO: ref.sh [arquivo] [secao]
KB=~/.claude/skills/base-solvace/scripts/kb.sh               # Base Solvace: index [termos] | show <projeto> [secao] | find <termo>
KC=~/.claude/skills/base-solvace/scripts/kc.sh               # Knowledge Center: search <termos> | article <n>
```

## Economia de tokens (vale para todo o fluxo)
O custo e **respostas × contexto**: tudo o que entra (saida de comando, arquivo lido) e relido em **cada resposta
seguinte** — 10 KB lidos cedo numa analise de 80 respostas = ~200 mil tokens. Por isso:
- **Contexto inicial num comando**: `bash $PLAN contexto <card>` ja traz os campos do card (`dados/card-resumo.txt`),
  repro steps, plano, comentarios/anexos, os projetos da Base Solvace e os artigos do KC ligados ao card. Nao abra
  `card.json`, nao rode `kb.sh index` sem termos nem repita essas buscas.
- **Referencias so por secao, so na hora**: `bash $REF <arquivo> <secao>` (tabela no fim). **Nunca** `cat`/Read de
  um arquivo inteiro de `references/`, nunca varios de uma vez, nunca "para ja ter". Esta SKILL.md ja esta no
  contexto — nao a releia (so se o passo 0 disser que atualizou).
- **Engenharia reversa primeiro (0052, obrigatorio)**: o `contexto` ja traz o bloco `=== ENGENHARIA REVERSA` com os
  itens do modulo do card (regras `RN`, casos de uso `UC`, telas, endpoints, tabelas) que casam com o titulo/repro — e o
  texto dos primeiros. Investigue **a partir deles**: `prmake_base_search(query, module, kinds, card)` para outros
  assuntos (barato: so referencias) → `prmake_base_get(refs, card)` para o texto do item → `prmake_base_impact(tabela|item)`
  para quem mais usa. **Sempre com `card`** (registra a consulta). O codigo entra so para **confirmar o `Onde:`** que o
  item cita (Read com offset/limit naquele arquivo:linha) ou quando a base nao tem o assunto — ai e lacuna: diga qual e
  registre (`arch.sh suggest <modulo> re-funcional lacuna.md --kind gap --card <card>`). Modulo com engenharia
  **COMPLETA**: a etapa `investigar-codigo` so conclui citando no resumo os itens usados (`revamp-kaizen#RN-012`) ou
  `lacuna: ...` — o PRMake recusa o `advance` sem isso. Cite os IDs tambem na analise e no RCA. O consumo do plano
  (0055) mede de onde voce leu: engenharia reversa × base antiga × codigo **confirmando** um item (o arquivo que o `Onde:`
  cita) × codigo **explorando** (sem item que o cite — vira candidato a lacuna na tela).
- **Base antiga (modulo sem engenharia reversa)**: a PRIMEIRA consulta sobre o codigo e a base:
  `kb.sh show <projeto> modulos` do mundo certo — legado (`legado-<modulo>`: telas → `.asp`/controller → service/SP)
  ou revamp (`revamp-<modulo>`); o `contexto` mostra os dois. So entao os arquivos que ela aponta. Outro modulo:
  `kb.sh index <termos>`; nao sabe o mundo: `kb.sh show edv-solvace modulos` (glossario de siglas → projeto). Base sem o
  caso → busca **so na pasta do modulo** (`revamp-repos.sh where <repo>`; nunca `grep -r` na pasta de todos os
  repositorios) e, ao achar, registre a lacuna
  (`arch.sh suggest <projeto> modulos lacuna.md --kind gap --card <card>`); varredura ampla inevitavel → subagente
  `Explore` (volta so o resumo). Diga no `advance` de `investigar-codigo` qual secao da base usou.
- **Saidas curtas**: `head`/`grep -m`/`sed -n` com limite; SQL/logs longos → `$CARD_DIR/dados/` e leia so o trecho.
- **Menos turnos**: agrupe comandos independentes numa chamada (`>/dev/null` no que so confirma), troque de etapa
  com `advance`, `log` curto; nada de "vou fazer X" sem fazer.
- **Analise → correcao sem carregar a analise (0049)**: a analise termina deixando o **resumo para a correcao** no
  checkpoint de `propor-solucoes` (`$REF correcao 6`). No executor a correcao comeca numa **sessao nova** so com ele
  (`contexto-correcao`) — nao rele a conversa da analise a cada resposta.

## Comandos do plano no dia a dia — MCP primeiro
Com as ferramentas `mcp__prmake__*` na sessao (MCP do PRMake), conduza o plano por elas: menos tokens, sem bash/jq.
Elas chegam "adiadas": carregue as que vai usar **uma vez, no inicio**, num unico
`ToolSearch("select:mcp__prmake__prmake_base_search,mcp__prmake__prmake_base_get,mcp__prmake__prmake_base_impact,mcp__prmake__prmake_advance,mcp__prmake__prmake_step,mcp__prmake__prmake_log,mcp__prmake__prmake_block,mcp__prmake__prmake_ask,mcp__prmake__prmake_control,mcp__prmake__prmake_plan,mcp__prmake__prmake_file,mcp__prmake__prmake_checkpoint")`
(as outras so quando precisar). Sem as ferramentas (MCP nao registrado) use o script — mesmo efeito no PRMake.

| O que | MCP | Script (reserva) |
|---|---|---|
| **Base Solvace (antes do codigo)**: itens da engenharia reversa, secoes, KC · impacto entre modulos | `prmake_base_search(query, module?, kinds?, card)` · `prmake_base_get(refs, card)` · `prmake_base_impact(term)` · `prmake_base_module(module)` | `kb.sh re find` · `kb.sh re get <m>#<ID> --card` · `kb.sh show` |
| concluir etapa e iniciar a proxima | `prmake_advance(card, from, to, message, kind)` | `advance <card> <key> <proxima\|-> "resumo" [finding]` |
| mudar etapa / registrar andamento | `prmake_step(card, key, status, reason)` · `prmake_log(card, message, kind, stepKey)` | `step` · `log` |
| criar/refinar etapas · onde parei | `prmake_steps(card, steps)` · `prmake_checkpoint(card, key, text)` | `steps` · `checkpoint` |
| entre etapas (pausado? cancelado?) | `prmake_control(card)` (`action`: continue/wait/stop) | `control` (0/10/11) |
| travada esperando o usuario | `prmake_block(card, key, text)` · `prmake_unblock` | `block` · `unblock` |
| perguntar · resposta dada no chat | `prmake_ask(card, questions)` · `prmake_answer(card, n, text)` · `prmake_answers` | `ask` · `answer` · `answers` |
| comentarios e anexos do usuario | `prmake_notes(card)` · `prmake_attachment(card, "imagem 2")` (ja mostra a imagem) | `notes` · `attachment` + Read |
| estado do plano / retomar | `prmake_plan(card)` (etapas com checkpoint, atividade, links) | `resume-info` |
| link/chamado na etapa · plano de correcao | `prmake_link(...)` · `prmake_correction(card, title, steps)` | `link` · `correction` |
| arquivo nos arquivos do plano (script `.sql`, analise `.md`, texto do chamado) | `prmake_file(card, name, content, kind?, key?)` | `upload` · `sync` |
| configuracao · card do DevOps · mover o card | `prmake_config` · `prmake_devops_config` · `prmake_card(card)` · `prmake_devops(card, action)` | `settings` · `devops <card> config` · `devops` |
| Timeline | `prmake_timeline(card, markdown)` | skill `prmake-timeline` |

**Sempre pelo script** (arquivos locais, git, custo): `contexto` (inicio), `sync`/`upload`, `branches`, `worktree`,
`pr-text`/`save-pr-text`/`open-pr`, `watch`/`wait` (fora do executor) e `status <card> completed|failed` ao concluir o
plano (envia o custo da sessao). Depois de `prmake_correction`, se for usar o script: `bash $PLAN use <card> correction`.

## Regras que valem sempre
- **Plano de execucao obrigatorio**: tudo o que voce produz vai para o plano em pedacos; `control` entre etapas;
  nunca encerre a vez com algo pendente de fora sem o vigia (`watch` **em segundo plano**), exceto no modo executor.
  O PRMake escreve sozinho os marcos na Timeline — nao duplique.
- **Modo executor (`PRMAKE_EXECUTOR=1`: sessao aberta pelo PRMake, sem terminal)**: ninguem le esta sessao. Nada de
  `watch`/`wait`/`wait-answers` (saem com exit 12) nem pergunta no chat: o que depende de alguem vai para o plano
  (`ask`, `block`, etapa `waiting`) e voce **encerra a vez** — o PRMake retoma esta mesma sessao quando a pessoa agir
  (a correcao, numa sessao nova: o prompt pede `contexto-correcao`).
  Correcao sempre no worktree do card (o `branches` ja imprime os comandos com `$WT`). Detalhes: `$REF plano executor`.
- **Repositorios da maquina (0048)**: as pastas vem do mapa da maquina (`~/.prmake/repos.json`, nome do repositorio
  pelo remote → pasta) — nunca suponha `~/repos/solvace/...`. Ache com `revamp-repos.sh where <repo>` (ou `list`); o
  `branches` ja imprime `pasta=`. Fora do mapa ou com mais de um clone (`where` exit 2/3, `branches` exit 4): pergunte
  ao usuario a pasta (`ask`; no terminal, no chat) e fixe com `bash ~/.claude/skills/.prmake/prmake-skills.sh repos set
  <repo> <pasta>` — nao conclua a analise "sem o codigo" sem ele dizer que pode. Detalhes: `$REF consultas 3a`.
- **MCP do PRMake primeiro** (tabela acima): plano, etapas, bloqueios, perguntas e respostas, comentarios, anexos,
  links, correcao, configuracao, card e DevOps pelas ferramentas `mcp__prmake__*`; o script fica para arquivos locais,
  git, o `contexto` e o `status` final — e para quando o MCP nao estiver na sessao.
- **Pendencia do usuario sempre evidente no PRMake**: etapa sua travada por algo que so o usuario resolve (o Claude
  Code barrou o comando — permissao/auto mode —, VPN desligada, credencial ausente, acesso negado) → **na hora**
  `prmake_block` (ou `bash $PLAN block <card> <key> "..."`) com o que ele precisa fazer (o que liberar, o comando exato e a alternativa), diga o
  mesmo no chat e rode o vigia. **Nunca deixe a etapa `running` parada.** O vigia avisa `PENDENCIA RESOLVIDA` quando
  ele clica *Ja resolvi* (ou ele responde no chat) → `step <key> running` e tente de novo. Autorizar no PRMake **nao**
  libera comando no Claude Code: quem libera e a regra de permissao (`prmake-skills.sh permissions`).
- **Card que depende de dados nao fecha sem o banco**: o `contexto` mostra se ha credencial de banco nesta maquina;
  teste o acesso cedo (`sql-query.sh --ping`, passo 3). Sem acesso, diga **claramente** no chat que esta sem acesso ao
  banco do cliente e o que falta (permissao do Claude Code, VPN ou credencial); credencial o usuario cadastra **no
  terminal dele** com `prmake-skills.sh db-credentials` — **nunca peca nem aceite senha no chat ou no PRMake**; `block`
  na `consultar-ambiente` e espere — nao conclua a analise, nao proponha solucoes como fato e nao
  empurre a verificacao para o plano de correcao. So siga sem o banco se o usuario responder explicitamente que e
  para seguir assim (`ask`), e diga isso na analise. Detalhes: `$REF consultas 3c`.
- **Orientacao ao cliente = resumo PT/EN do fechamento**: o resumo nao tecnico publicado na discussion ja e a
  orientacao (com o passo a passo). Nao crie etapa `orientar-cliente` nem `validar-cliente` depois do fechamento — o
  plano conclui no `fechar-card`. Detalhes: `$REF correcao 7`.
- **Arquivos do card sempre no plano (0046/0049), cada um na sua fase (0050)**: todo script, analise e **texto do
  chamado** que o usuario precisa ver vai para os arquivos do plano — **no plano da fase certa**:
  - **Analise** (`phase: "analysis"`): so as **consultas somente leitura** que levaram ao problema
    (`00_consulta-<assunto>.sql`, com um comentario no topo dizendo o que cada uma mostrou), a analise `.md` e os dados.
  - **Correcao** (`phase: "correction"`): o **script que altera dados** (`01_<nome>.sql` com rollback), a consulta de
    validacao e o **texto do chamado** (`chamado-<nome>.md`, kind `ticket`), com `key` = a etapa do chamado. Rodar o
    script e execucao: **nunca** grave script de alteracao nem chamado na analise — a API recusa (mensagem de regra).
  **No executor: direto com `prmake_file(card, "01_nome.sql", conteudo, "script", key, phase: "correction")`** — uma
  chamada, sem gravar local antes nem `sync`. No terminal: grave em `$CARD_DIR` (a pasta que o `contexto` imprime,
  padrao `~/.prmake/cards/<card>` — **nunca** `~/.claude/...`, que o Claude Code protege) e rode `sync`, ou use
  `prmake_file`. Gravacao local negada → `prmake_file` na hora e siga — **nunca** trave a etapa, `block` nem peca
  permissao ao usuario por causa de arquivo.
  **Diga o que anexou, pelo nome** (0050): na descricao da etapa, na mensagem final da vez e na Timeline —
  "Anexei ao plano de correcao: `01_x.sql` (script + rollback), `chamado-x.md` (texto do chamado)"; na analise, o
  mesmo para as consultas. O usuario nao deve ter que procurar no rodape para saber que o arquivo existe.
- **Comentarios e anexos do usuario sao entrada da analise** (mesmo peso dos repro steps). Referencia a anexo
  ("imagem 2", "#12", "print.png") → `prmake_attachment(card, "<ref>")` (sem MCP: `bash $PLAN attachment <card> "<ref>"` e
  abra com Read); comentario → `prmake_notes(card)` (sem MCP: `bash $PLAN notes <card> <n>`). Anexos do PRMake ficam so em `$CARD_DIR/anexos-prmake/` (nunca copie para
  `imagens/`/`anexos/` — o `sync` duplicaria). Nunca diga que nao consegue ver um anexo sem tentar.
- **Regra de negocio: consulte o Knowledge Center antes de perguntar** ao usuario ou concluir o comportamento
  "esperado" (`bash $KC search ...` / `article <n>`); cite o **ART-n** na analise, no RCA e no handover. Sem artigo
  sobre a regra: diga isso (lacuna) — nao invente.
- **Quem grava e o PRMake — sempre** (root cause, resumo, classificacao, estimativa, mover o card, PRs, Timeline):
  voce gera os textos; nada direto no Azure DevOps/GitHub. Falta endpoint → pare e avise.
- **Configuracao vem do PRMake** (`prmake_config` · `prmake_devops_config` — acoes e classificacoes; sem MCP: `bash $PLAN settings <card>`, `devops <card> config|classifications`; `branches` sempre pelo script): nunca escreva de
  memoria estados, areas, branches, titulos. Faltou regra → pergunte e sugira configurar no PRMake.
- **Ready for QA so com autorizacao**: com a correcao em QA, mova para Dev Test in QA (`devops <card> dev-test-in-qa`,
  sem perguntar); `devops <card> ready-for-qa` so quando o usuario concluir a etapa `validar-qa` no plano ou
  autorizar explicitamente depois dela (a resposta do passo 6 nao vale). Detalhes: `$REF catalogo "ready for qa"`.
- **Foco no card**: solucoes/scripts so para o caso relatado; outros afetados → so um aviso curto.
- **Analise e somente leitura**; codigo muda so depois da resposta do usuario, **sem comentarios novos no codigo**
  (o porque vai no commit/PR/plano). **Nunca merge nem aprovacao de PR.** Nunca escrita em banco/Cognito.
- PII: o que vai para o plano/Timeline e visivel no PRMake — so o necessario.
- `PLANO INDISPONIVEL` no `start`/`contexto`: siga normalmente (os comandos do plano viram no-op).

## Fluxo
**0. Atualizar a skill** — `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet analisar-bug 2>/dev/null || true`;
se imprimir "Skills do PRMake atualizadas", releia esta SKILL.md.

**1. Card** — branch `hotfix/<n>`/`bugfix/<n>` ou o numero informado; sem nenhum dos dois, pergunte.

**2. Contexto (1 comando)** — `bash $PLAN contexto <card>`. Ele cria/retoma o plano (se ja existe plano aberto,
**continue da primeira etapa pronta com o checkpoint**; analise concluida sem correcao → siga do passo 6, sem
refazer), mostra os campos do card e os repro steps (inteiros em `$CARD_DIR/dados/`), lista comentarios/anexos novos
(abra cada anexo novo com Read), sincroniza o KC e a Base Solvace e mostra os projetos/artigos ligados ao card.
Conclua `identificar-card`/`coletar-dados` (um `advance`) e **refine as etapas** (`steps`) para este caso concreto.
**Correcao numa sessao nova** (o prompt do executor pede): `bash $PLAN contexto-correcao <card>` no lugar do
`contexto` — resumo da analise, respostas, comentarios e arquivos; nao refaca a investigacao, siga do passo 7.

**3. Investigar** — comece pelos itens da engenharia reversa que o `contexto` mostrou (`prmake_base_get` com o card;
outros assuntos: `prmake_base_search`); sem engenharia reversa do modulo, pela secao da Base Solvace (`bash $KB show
<projeto> <secao>`; nenhum casou → `bash $KB index <modulo/tela>`); depois so o codigo apontado; regra de negocio → KC. Reconstrua
o fluxo, levante hipoteses priorizadas com `caminho:linha`, marque o que e hipotese. Diga em qual mundo/repo esta o
codigo (legado `edv-solvace` ou `revamp-<modulo>`). Dados, Cognito, localizar codigo fora da base:
`$REF consultas 3a|3b|3c` (`consultar-ambiente`; cancele com motivo so se o caso **nao** depender de dados). Card que envolve dados
(usuario, cadastro, status, configuracao por tenant): **antes de investigar a fundo**,
`bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host <h> -d <db> --ping` — falhou → `block` com o que fazer
(ligar a VPN, liberar a permissao, credencial). Reconheca o padrao do caso no catalogo: `$REF catalogo catalogo`. Se o
codigo divergir da Base Solvace, registre um `log warning` e proponha a correcao da secao:
`bash ~/.claude/skills/base-solvace/scripts/arch.sh suggest <projeto> <secao> divergencia.md --kind divergence --card <card>`.

**4–5. Analise e Timeline** — monte `$CARD_DIR/analises/analise-inicial.md` pelo modelo de `$REF analise 4`
(publicacao: `$REF analise 5`; inclua "Regras de negocio (Knowledge Center)" com os ART-n usados), `sync` e publique
com `prmake-timeline`. O plano de analise segue para `propor-solucoes`.

**6–8b. Solucoes, plano de correcao e execucao** — uma secao por passo: `$REF correcao 6`, depois `7`, `8`, `8b`
(perguntas com opcoes cujo `label` e a propria opcao, espera nas duas pontas, `correction`, fluxo de branches pelo
`branches`, PRs com `pr-text`/`save-pr-text`/`open-pr`, fechamento pelo `devops`, nova rodada no mesmo plano).

**9. Aprender e reportar** — se o caso ensinou algo que nao esta na Base Solvace (regra, armadilha, fluxo, tabela,
query util) ou mostrou um item da engenharia reversa errado/incompleto, proponha em poucas linhas:
`bash ~/.claude/skills/base-solvace/scripts/arch.sh suggest <projeto> <secao> aprendizado.md --kind learning|divergence
--card <card> --item RN-012` — com engenharia reversa, a secao e o documento do item (`re-funcional`, `re-arquitetura`...)
e `--item` o ID: entra na proxima sessao `melhorar` do modulo (nunca grava direto). Divergencia entre o Knowledge Center
e o codigo: `--kind kc` (lista para o time de produto). **Armadilha** (o que deu errado e nao muda o "como o sistema e":
sintoma, causa, diagnostico): `bash ~/.claude/skills/engenharia-reversa/scripts/re.sh armadilha <projeto> --titulo "..."
--texto-file t.md --itens RN-012 --cards <card>` (fica "a conferir"). O `contexto` e o `prmake_base_get` ja mostram as
armadilhas ligadas aos itens — leia antes de investigar. Reporte: card,
causa raiz, solucao, PRs (links), o que ficou com o usuario e o que falta; tudo esta nos planos do card no PRMake. O custo da sessao vai sozinho ao mudar o status do plano (`bash $PLAN usage` mostra).

## Retomar um card
Sessao do Claude Code fica registrada no plano. Para voltar exatamente a esta conversa depois (outro card no
meio, sessao fechada): `bash ~/.claude/skills/analisar-bug/scripts/prmake-card.sh <card>` (o botao "Retomar no
Claude" do PRMake copia esse comando). Ao ser retomado, veja o que mudou na tela enquanto estava parado — `prmake_plan` + `prmake_notes` + `prmake_answers` (MCP; sem MCP: `resume-info`, `notes`,
`answers`) — e siga de onde parou. Para o PRMake rodar e retomar sozinho (botao "Analisar com Claude", respostas
pela tela) sem terminal: executor do PRMake — `bash ~/.claude/skills/.prmake/prmake-skills.sh agent install`.

## Referencias — `bash $REF <arquivo> <secao>` (so a secao, so na fase)
| Quando | Comando |
|---|---|
| passo 3: localizar codigo, Cognito, banco | `$REF consultas 3a` · `3b` · `3c` |
| passo 3/6/7: padrao do caso (A–H) | `$REF catalogo catalogo` |
| passos 4–5 | `$REF analise 4` · `$REF analise 5` |
| passos 6–8b | `$REF correcao 6` · `7` · `8` · `8b` |
| fechamento | `$REF catalogo fechamento` · `"ready for qa"` · `mover` · `"quem grava"` |
| detalhe do plano | `$REF plano comandos` · `retomar` · `control` · `block` · `watch` · `anexos` |
| nao sabe a secao | `$REF` (lista arquivos e secoes com ~tokens) |
