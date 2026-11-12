---
name: executar-item-de-trabalho
description: Executa um item de trabalho de um card no PRMake (feature 0085) — as sub-tarefas na ordem, uma por vez (criar branch, cherry-pick, codigo, abrir PR, mover o card no DevOps, script SQL, validacao, comentario, tarefa manual, abrir chamado, avisar na Timeline/Teams, acao no DevOps — root cause, resumo, classificacao, Remaining, estados —, gerar o texto do PR sem abrir PR, consulta somente leitura, revisar PR, resumo do pedido — o racional do card: o que pede ou reclama, o que fazer e o estado atual, validar no ambiente com prints), registrando inicio, resultado, saidas (PR, branch, commit) e como desfazer cada uma. Usa as regras do PRMake para o card (nome de branch por destino, padroes de commit/titulo) — nunca inventa nomes —, so abre PRs (nunca mescla) e grava tudo pelo PRMake. Roda pelo executor do PRMake (pedido "work-item") ou manualmente com /executar-item-de-trabalho <card> <numero>. Use quando o prompt pedir para executar o item de trabalho #n de um card.
---

# executar-item-de-trabalho

Executa **um** item de trabalho (`#<numero>`) de um card: as sub-tarefas **na ordem**, uma por vez, cada uma com
`start` → fazer → `complete` (resultado + saidas + desfazer) ou `fail`/`wait`. Itens rodam em paralelo entre si (cada
um na sua pista); dentro do item, a ordem manda.

Instalada e atualizada pelo PRMake (fonte: `skills/executar-item-de-trabalho`). Depende da `analisar-bug` (scripts de
branches, PR, DevOps, testes). Token: env `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh   # rules · work-items · branches · worktree · open-pr --item · devops · test
REPOS=~/.claude/skills/analisar-bug/scripts/revamp-repos.sh # where <repo> · master <repo>
CARD_DIR=~/.prmake/cards/<card>                             # cache do card (dados/regras.md, dados/card-resumo.txt)
```

## Regras que nao mudam
1. **Toda gravacao pelo PRMake** (MCP ou `prmake-plan.sh`): PR, card no DevOps, Timeline, status das sub-tarefas.
   Nada de PAT nem API direta do GitHub/DevOps. `git` local (worktree, commit, push da branch de trabalho) e permitido.
2. **Nunca mescle nem aprove PR.** A sub-tarefa de PR conclui quando o PR esta **aberto**; o merge e humano.
3. **Nunca invente branch, titulo nem mensagem de commit**: venham dos `params` da sub-tarefa, conferidos com as regras
   (`prmake_rules` / `bash $PLAN rules <card>`) e o `bash $PLAN branches <card> <repo>` (linhas `por-destino:` e
   `branches-por-destino:` = `BranchSuffixByTarget`, ex.: release-version → `hotfix/77434-rv`, nunca `-release`).
   Param vazio ou divergente da regra → use a regra e diga no `result`; regra ausente → `wait` com a pergunta.
4. **Item ligado a plano (`planId`) nao roda aqui**: ele e executado pelo plano (correcao no Bug, implementacao na US;
   pista do plano; o servidor
   sincroniza as sub-tarefas). Se cair aqui, nao toque em nada: diga no fim "item #n e do plano — use Continuar no
   plano" e encerre.
5. **Cancelado para tudo**: antes de **cada** sub-tarefa, `prmake_work_item(card, number)`; status `cancelled`,
   `reverting` ou `reverted` → pare na hora (nao comece a proxima, nao marque nada).
6. **Nunca force-push** nem reescrever historico de branch ja publicada. **Banco e somente leitura** (sub-tarefa `sql-script` so escreve o script); sem comentarios novos no codigo.
7. Modo executor (`PRMAKE_EXECUTOR=1`): ninguem le o chat. O que depende de alguem vira `wait` na sub-tarefa (com o
   que a pessoa precisa fazer/decidir) e voce **encerra a vez** — o PRMake retoma quando ela agir.

## MCP primeiro
Carregue uma vez, no inicio:
`ToolSearch("select:mcp__prmake__prmake_work_item,mcp__prmake__prmake_work_items,mcp__prmake__prmake_work_task,mcp__prmake__prmake_rules,mcp__prmake__prmake_card,mcp__prmake__prmake_devops,mcp__prmake__prmake_work_item_file,mcp__prmake__prmake_work_deliverable")`
(`prmake_timeline`, `prmake_pr_open`, `prmake_file`, `prmake_devops_config`, `prmake_base_impact`, `prmake_work_item_evidence`,
`prmake_work_item_files`, `prmake_notes`, `prmake_case_context` so quando precisar).

| O que | MCP | Script (reserva) |
|---|---|---|
| detalhe do item (sub-tarefas, params, output, undo, status) | `prmake_work_item(card, number)` | `bash $PLAN work-items <card> <n>` |
| regras do card (branch por destino, padroes) | `prmake_rules(card)` | `bash $PLAN rules <card>` (cache 10 min em `dados/regras.md`) |
| iniciar / concluir / falhar / esperar / pular sub-tarefa | `prmake_work_task(card, taskNumber, action, result?, output?, undo?)` — `start\|complete\|fail\|wait\|skip` | `bash $PLAN work-task <card> <n> start\|complete\|fail\|skip "resultado" --output '{..}' --undo '{..}'` (wait so MCP) |
| card (estado, area, tipo) | `prmake_card(card)` | `dados/card-resumo.txt` |
| mover o card / lancar horas | `prmake_devops(card, action)` (`log-hours` com `hours`) | `bash $PLAN devops <card> <acao>` |
| arquivo do item (chamado completo, script, resultado) | `prmake_work_item_file(card, number, name, content, kind?, taskNumber?)` | — |
| abrir PR | `prmake_pr_open(card, repository, source, target, title?, draft?, description?, taskNumber)` (devolve `output`/`undo`) | `bash $PLAN open-pr ... --item <n> --task <sub-tarefa>` |
| codigo alterado (branch + diff completo) da sub-tarefa | — | `bash $PLAN deliver-code <card> <repo> --task <sub-tarefa> --item <n>` |
| outra entrega (chamado, aviso, link) | `prmake_work_deliverable(card, taskNumber, kind, ...)` | — |

## Fluxo
**0. Atualizar** — `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet executar-item-de-trabalho analisar-bug 2>/dev/null || true`.

**1. Carregar (uma vez)** — `prmake_work_item(card, number)` + `prmake_rules(card)` (ou `bash $PLAN rules <card>`:
usa o cache do card). Contexto minimo do card: `$CARD_DIR/dados/card-resumo.txt` se existir; senao `prmake_card(card)`.
0094: se o card tiver **relatados** (itens de outros cards ligados a este — vem no `prmake_case_context`, secao "Relatados"),
leia o resumo e inclua no contexto da analise quando for relevante (detalhe sob demanda: `prmake_work_item(card, n)`).
Nao rode o `contexto` da analise nem releia planos/analises inteiras — so o arquivo que a sub-tarefa citar.
- `planId` presente → regra 4. Status `completed`/`cancelled`/`reverted` → nada a fazer, encerre.
- Sub-tarefas: siga `order`; pule as `completed`/`skipped`; retome a `running`/`waiting` (o executor te chamou de novo
  porque a pessoa agiu — confira o que mudou antes de seguir). Sub-tarefa que voltou de uma espera pelo **"Já resolvi"**
  fica `pending` com `**Resolvido por <pessoa>:** <nota>` no fim do `result`: e o que a pessoa fez (ex.: "adicionei o
  alias X") — use isso e tente de novo; ainda sem saida → `wait` de novo dizendo o que continua faltando.

**2. Para cada sub-tarefa, na ordem** (uma por vez):
1. Checagem do item (regra 5).
2. `executor: user` → `prmake_work_task(action: wait, result: "<o que a pessoa faz, com os arquivos/links>")` e
   **pare** (encerre a vez). A pessoa conclui pela tela; o executor retoma o item.
3. `prmake_work_task(action: start)`.
4. Faca, pelo guia do `kind` abaixo.
5. `prmake_work_task(action: complete, result, output, undo)`:
   - `result`: markdown curto (2–6 linhas) — **comeca por uma manchete curta em negrito** (ex.: `**Legado: persiste.**`;
     a tela mostra essa frase na lista de etapas), depois o que foi feito, nomes reais (branch, PR, commit), avisos.
   - `output`: so fatos para a tela e para o desfazer — `{"pr": url, "number": "n", "repository", "branch", "target",
     "commit": sha, "commits": [...], "created": "true|false", "from": "<estado anterior>"}` (o que se aplicar).
   - `undo`: `{"action": "...", "params": {...}}` do catalogo abaixo — **sempre** que a sub-tarefa mudou algo fora;
     `{"action":"none","params":{"reason":"..."}}` quando nada precisa ser desfeito.
   - Nao deu: `fail` com o motivo e o que tentar (e pare o item). Depende de decisao/acao de alguem: `wait` e pare.
   - **Entregas (0091)** — o que a sub-tarefa gerou aparece no modal dela e na aba Entregas do item: `code`/`branch`/
     `cherry-pick` → **antes** do `complete`, `bash $PLAN deliver-code <card> <repo> --task <sub-tarefa> --item <n>`
     (branch, base, commits e o diff completo, direto do worktree — nao cole diff no chat); PR, acoes do DevOps e Timeline
     sao registrados sozinhos (passe `taskNumber`/`--task` quando a ferramenta aceitar); chamado com link, aviso ou
     documento → `prmake_work_deliverable` (o `output` do `complete` tambem e lido, mas a entrega deixa claro).
6. Proxima sub-tarefa. No fim, `prmake_work_item` para conferir: todas concluidas → o PRMake conclui o item sozinho.

**3. Relatorio** — 3–6 linhas no chat (item, sub-tarefas feitas, PRs/branches, o que ficou esperando quem). Os marcos
ja ficam no historico do item; nao duplique na Timeline (so a sub-tarefa `comment` escreve la).

## Guia por kind
Use os `params` da sub-tarefa (`repository`, `branch`, `source`, `target`, `from`, `commits`, `action`, `text`...).
Pasta do repositorio: `bash $REPOS where <repo>` (ou a linha `pasta=` do `branches`); fora do mapa → `wait` pedindo a
pasta (`prmake-skills.sh repos set <repo> <pasta>`). Trabalhe **sempre num worktree do card**:
`WT="$(bash $PLAN worktree <card> <pasta|repo> <branch> <base> --item <n>)"` (worktree proprio do item, em
`.prmake-wt/<card>/item-<n>/<repo>`; outra branch do mesmo item troca nele) — nunca no clone de trabalho da pessoa.
Push sempre explicito: `git -C "$WT" push -u origin <branch>` (o worktree nasce rastreando a base).

| kind | O que fazer | output | undo |
|---|---|---|---|
| `branch` | Nome pela regra (param conferido com `branches`/`rules`). `git -C <pasta> fetch origin`; ja existe no origin (`git ls-remote --heads origin <branch>`)? → so siga se foi criada **por este item** (retomada: `output` desta sub-tarefa); senao **nao reaproveite** (regra do PRMake): `wait` dizendo de onde ela veio (PR/item do card) e o que a pessoa decide. Nao existe → `WT=$(bash $PLAN worktree <card> <repo> <branch> <from> --item <n>)` e `git -C "$WT" push -u origin <branch>` | `{branch, from, created}` | criada pelo item: `delete-branch {repo, branch}`; ja existia: `none` |
| `cherry-pick` | No worktree da branch: `git cherry-pick -x <shas>` (shas dos params, ou os commits da branch de origem: `git log --format=%H --reverse origin/<base>..origin/<source>`). Conflito: resolva mantendo a intencao, `--continue`, avise no `result`; nao conseguiu → `git cherry-pick --abort` e `fail`. **Push so com o cherry-pick concluido** (`! git -C "$WT" rev-parse -q --verify CHERRY_PICK_HEAD && git -C "$WT" push -u origin <branch>`) | `{branch, commits:[novos shas]}` | `revert-commit {repo, branch, sha:"<sha1 sha2>", createdByItem:"true\|false"}` |
| `code` | Mudanca descrita na sub-tarefa, so no escopo dela, **sem comentarios no codigo**; testes so do que mudou (`bash $PLAN test <card> "$WT"`; .NET: um `dotnet build` + `dotnet test --no-build --filter`); commit no `CommitMessagePattern` (linha `commit=` do `branches`); `git -C "$WT" push -u origin <branch>` | `{branch, commit}` | `revert-commit {repo, branch, sha, createdByItem}` |
| `pr` | Descricao: reaproveite a do card se servir; senao `bash $PLAN pr-text <card> <repo> <branch>` e gere `desc.md` como a `gerar-prmake` (sem RCA para US). Abrir: `prmake_pr_open(card, repo, branch, target, title?, description: <desc.md>)` ou `bash $PLAN open-pr <card> <repo> <branch> <target> ["titulo"] <desc.md> --item <n>` (sem titulo = `PrTitlePattern`). Os dois devolvem o JSON `{output, undo}` pronto — use-o. **Nunca mescle** | `{pr, number, repository, branch, target}` | `close-pr {repo, number}` (PR que ja existia: `none`) |
| `devops-state` | `prmake_card` → anote o estado atual (`from`). Mova so pelo PRMake: estado de uma acao configurada → `prmake_devops(card, action)` / `bash $PLAN devops <card> dev-test-in-qa\|test-in-production\|ready-for-qa`; outro estado valido do tipo (a lista do editor vem de `GET Azure/card/<card>/states`) → `prmake_devops(card, "set-state", state: "<estado>")`. `ready-for-qa` so se a sub-tarefa foi pedida pela pessoa (item criado/executado por ela) — senao `wait` pedindo a autorizacao. Estado fora da lista → `fail` com os estados validos que a API devolver | `{from, to, action}` | `devops-state {from, action:"<acao que volta, se houver>"}` |
| `sql-script` | **Nunca executa escrita.** Consulte so leitura se precisar (`sql-query.sh`), escreva o script com rollback e a consulta de validacao em `$CARD_DIR/scripts/<nn>_<card>_<nome>.sql` (rollback em arquivo: `99_<card>_rollback_<nome>.sql` — o numero do card vai sempre no nome, ex.: `99_78262_rollback_ligar-sso-otronnier.sql`), grave como arquivo do item (`prmake_work_item_file(card, number, "<nn>_<card>_<nome>.sql", script, kind: "script", taskNumber)`) e cole no `result` (bloco ```sql). Quem roda e o chamado: o item com script sempre tem as sub-tarefas `ticket` logo depois (o PRMake acrescenta se faltarem) | `{file, rollback:"true"}` | `none` (nada executado) |
| `validation` | `executor: user` → `wait` com o roteiro (o que testar, onde, criterio). `claude`: build/testes do que mudou (`$PLAN test`), consulta somente leitura, conferir PR/CI (`gh pr checks <url>`) | `{checks}` | `none` |
| `comment` | Texto da sub-tarefa (curto, pt-BR; no card do DevOps em PT/EN se for para o cliente). Timeline: `prmake_timeline(card, markdown)`. Discussion do DevOps: `bash $PLAN devops <card> comment <arquivo.md> --task <n> [--image <print.png>]...` — comentario **novo**, sem precisar de registro de PR do card (a Bancada nao cria um; nunca peca para "salvar o card"); prints que o texto cita vao com `--image` (vira anexo do DevOps embutido no comentario; baixe o arquivo antes: anexo do plano `bash $PLAN attachment <card> <ref>`, arquivo da conversa do caso `bash ~/.claude/skills/especialista-do-caso/scripts/prmake-case.sh file <card> <n>`). **Nao** use `summary` para isso: ele e o resumo nao tecnico do fechamento e sobrescreve o resumo ja publicado | `{where}` | `none` (comentario nao se apaga) |
| `manual` | `executor: user` (padrao) → `wait` com o passo a passo e pare. `claude` → faca o que a descricao pede, dentro destas regras | — | conforme o que fez |
| `research` | **Somente leitura**: entenda o que a descricao pede — card/evidencias (`prmake_card`, `prmake_evidence`), Base Solvace (`prmake_base_search`/`prmake_base_get`/`prmake_base_impact`), codigo (ler, nunca alterar), dados (`sql-query.sh`, so SELECT). Conclusao (o que achou, com `caminho:linha`/itens da base) no `result`; texto longo em arquivo do item (`prmake_work_item_file(..., kind: "analysis", taskNumber)`) | `{file?}` | `none {reason:"so leitura"}` |
| `decision` | Apresente as opcoes (pros, contras, riscos) e pergunte: com plano, `prmake_ask`; sem plano, `wait` com as opcoes na mensagem — a pessoa responde no PRMake. Nao decida sozinho | `{choice?}` | `none` |
| `task` | Faca o que a descricao pede, dentro destas regras (nada de codigo, PR, script ou mover card fora do tipo proprio — se a sub-tarefa pedir isso, `fail` dizendo o tipo certo) | — | conforme o que fez |
| `ticket` | **Fluxo de chamado do PRMake** (o mesmo das etapas `chamado-*` da `analisar-bug`, `references/correcao.md`), em duas sub-tarefas. **`executor: claude`** ("Gerar o chamado completo (arquivo do chamado)"): escreva o chamado **completo** em markdown — 1a linha = titulo (`params.title`, ex.: `AB#<card> ...`); corpo: link do card, cliente/ambiente/banco, o que o script faz e por que, o script inteiro (ou "anexo `<nn>_<card>_<nome>.sql`"), rollback, consulta de validacao, prioridade (`params.priority`) e `params.description` — e grave como **arquivo do item**: `prmake_work_item_file(card, number, "chamado-<card>-<slug>.md", texto, kind: "ticket", taskNumber)` (o `.sql` ja foi no `sql-script`). Card com plano de correcao/implementacao aberto → grave tambem nos arquivos do plano: `prmake_file(card, "chamado-<card>-<slug>.md", texto, "ticket", <etapa do chamado, se houver>, phase: "correction")`. `complete` com `{file}`. **`executor: user`** ("Abrir o chamado no sistema de chamados e anexar o link"): `wait` com "Abra o chamado no <`params.system` ou `TicketSystem`> com o texto de `chamado-<card>-<slug>.md` (Arquivos do item) e anexe `<nn>_<card>_<nome>.sql`; depois use *Anexar chamado* nesta sub-tarefa — ela conclui sozinha quando o chamado for marcado como resolvido" e pare. **Nunca** `complete` esta sub-tarefa (nem com o link no chat): quem conclui e o chamado resolvido na Bancada. A conferencia depois do chamado e a sub-tarefa `sql-query` seguinte ("Conferir os dados depois do chamado"): consulta de validacao do script, resultado como arquivo do item (evidencia); divergencia → `fail` | claude: `{file}` · user: `{ticket:{url, title, status}}` (gravado pela Bancada) | `none {reason:"chamado nao se desfaz pelo PRMake"}` |
| `devops-action` | Acao do PRMake no card (`params.action`), **sempre pelo PRMake**: `rootcause` → escreva o RCA (Bug; US nao tem root cause → `skip` com o motivo) em `$CARD_DIR/analises/rootcause.md` e `bash $PLAN devops <card> rootcause <arquivo>` · `summary` → resumo nao tecnico **PT/EN** (prompt de `bash $PLAN devops <card> config` → `analises/summary-prompt.txt`; `params.value` = contexto novo, se houver) e `bash $PLAN devops <card> summary <arquivo>` (cria comentario novo, nao apaga os anteriores) · `classify` → `params.value` = preset de `bash $PLAN devops <card> classifications` + **Bug Type** (Bug: um valor de `bugTypes` do `prmake_devops_config`, pela area/funcionalidade afetada; nunca fora da lista) → `prmake_devops(card, "classify", preset, bugType)` · `zero-remaining` / `initial-estimate` / `dev-test-in-qa` / `test-in-production` → `prmake_devops(card, action)` · `ready-for-qa` → como no `devops-state` (so com autorizacao da pessoa ou `validar-qa` concluida; senao `wait`). Anote o estado antes (`prmake_card`) quando a acao move o card | `{action, from?, to?, value?}` | move o card: `devops-state {from, action?}`; texto/campo: `none {reason:"comentario/campo do DevOps nao se desfaz pelo PRMake"}` |
| `pr-text` | O texto do PR **sem abrir PR** (mesmo caminho da `gerar-prmake`): `bash $PLAN pr-text <card> <params.repository> <params.branch>` → siga o `PROXIMO:` impresso (prompt configurado; Bug com `<RCA>`, US sem RCA) gerando `pr/<repo>/desc.md` (+ `rca.md`) → `bash $PLAN save-pr-text <card> <desc.md> [rca.md]` (grava no card e nos arquivos do plano; card sem plano: o texto vai no `result`). Nada de `open-pr` aqui | `{repository, branch, file}` | `none` |
| `sql-query` | **Somente leitura**: monte o SELECT a partir de `params.how` (o que conferir) e rode `bash $AB/sql-query.sh --host <alias> -d <db> -q "SELECT ..."` (`AB=~/.claude/skills/analisar-bug/scripts`; host pela regra de hosts por cliente da `analisar-bug`; `params.database` = banco/ambiente; `cognito` → `bash $AB/cognito-query.sh user <ambiente> <email>`). Resultado em arquivo: `$CARD_DIR/scripts/<nome>-resultado.md` (consulta + tabela) e, com plano, `prmake_file(card, "<nome>-resultado.md", texto, "analysis")`; resumo (2–6 linhas, sem dado sensivel alem do necessario) no `result`. Sem acesso (VPN/credencial) → `wait` dizendo o que falta. Nunca UPDATE/DELETE/INSERT | `{database, file, rows}` | `none` |
| `pr-review` | Revisar o PR `params.pullRequest` (numero/URL) de `params.repository`: `gh pr view <pr> --json title,headRefName,baseRefName,files` + `gh pr diff <pr>` (so leitura), compare com a analise/plano do card e as regras (branch/destino/titulo), riscos, testes que faltam, `params.focus` primeiro. Conclusao no `result` (aprovaria / ajustes, com `caminho:linha`). **Nunca aprove, comente no PR nem mescle** — quem decide e a pessoa | `{pr, verdict:"ok\|ajustes"}` | `none` |
| `card-brief` | **Racional do card, em portugues** (mesmo com o card em ingles: interprete, nao traduza), so leitura. **Entenda tudo do card** antes de escrever. **1. Pedido:** `OUTDIR=$CARD_DIR/dados bash ~/.claude/skills/analisar-bug/scripts/bug-fetch.sh <card>` e leia **inteiros** `card-resumo.txt`, `description.txt` (repro steps no Bug, descricao na US), `criterios-aceite.txt` (US) e `discussion.txt` (sem o script: `prmake_card(card, comments: 50)`); comentarios do PRMake no plano, se houver: `prmake_notes(card)`. **2. Evidencias:** `prmake_work_item_evidence(card, number)` (anexos e prints da descricao/repro/discussion vao para os arquivos do item, mesmo sem plano) e abra **cada** legivel com `prmake_work_item_files(card, number, file: "#n")` (imagem volta como imagem; PDF/Office: `curl -s -H "x-api-key: $PRMAKE_TOKEN" "${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}/<contentUrl>" -o $CARD_DIR/dados/<nome>` + `python3 ~/.claude/skills/analisar-bug/scripts/evidence-text.py $CARD_DIR/dados/<nome>`). **3. Estado atual:** `prmake_card(card)` (estado no DevOps, responsavel), `prmake_case_context(card)` (planos e versoes, decisoes, perguntas abertas, PRs, divergencia) e `prmake_work_items(card)` (itens feitos/pendentes, chamados). **4. Escrever** `resumo-do-pedido.md`: **texto corrido e curto, sem secoes nem formulario fixo** (3–5 paragrafos), como quem explica o card a um colega que vai assumir — o que esta sendo **pedido** (US) ou **reclamado** (Bug) e por que; **o que precisa ser feito** para atender (o entendimento da entrega/correcao, nao a solucao tecnica nem a causa, que sao da analise); e **o estado atual** (onde o card esta, o que ja foi feito, o que a discussion mudou, o que esta aguardando quem). `params.focus` (se houver) guia o que destacar. Cite a fonte so quando ajudar (comentario de <pessoa> em dd/mm, evidencia #n); o que nao da para saber pelo card, diga em uma frase. Sem senha/token/dado pessoal alem do necessario. **5. Gravar:** `prmake_work_item_file(card, number, "resumo-do-pedido.md", texto, kind: "analysis", taskNumber)` (fica nos arquivos do card; se faltar, o PRMake grava o `result` com esse nome); `params.timeline` = `sim` → `prmake_timeline(card, texto)` tambem (`nao`/vazio: nao publique). `result` = o **racional inteiro** (e a entrega) | `{file, evidences:"<lidas>/<total>", timeline:"true\|false"}` | `none {reason:"so leitura"}` (Timeline: comentario nao se apaga) |
| `env-validation` | **Validar no ambiente com prints** — leia `cat ~/.claude/skills/validar-ambiente/SKILL.md` (faltando: `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet validar-ambiente`) e siga o fluxo da sub-tarefa `env-validation`. O navegador e o script `env.sh` pelo Bash (nao e ferramenta/MCP): rode `env.sh setup` antes de concluir que falta navegador; `params.user`/`plant` vazios = o `env.sh` escolhe (quem ja tem senha nesta maquina vence o admin da planta; planta diferente → troque no seletor da tela). Fluxo: `bash ~/.claude/skills/validar-ambiente/scripts/env.sh resolve <params.environment>` → `browser start <amb> [--user <params.user>] --card <card> --task <n>` → roteiro (`params.scenario`/`expected` ou o que o card pede) com `snap`/`click`/`fill` (navegacao) e `shot` so da tela que prova cada resultado esperado (2–5 prints) → `browser stop` → `evidence <card> <item> <n> <pasta> --report validacao-<amb>.md`. **Sem senha** (exit 4), login recusado (exit 3) ou sem navegador (exit 2) → o `env.sh` (com `--task`) ja deixa a sub-tarefa aguardando a pessoa na ferramenta com o comando (`prmake-skills.sh env-credentials set <amb> <email>`) e o *Ja resolvi*: so pare (saida `AVISADO NA FERRAMENTA`; senao, `wait` voce mesmo com a orientacao). Mudanca ainda nao publicada no ambiente → `wait` dizendo o PR/deploy que falta. DEMO/producao: so leitura. Reprovado → `fail` (nao siga para os outros ambientes) | `{environment, url, user, plant?, shots, verdict:"ok"}` | `none {reason:"so leitura/validacao"}` |
| `env-reproduction` | **Reproduzir o bug no ambiente com prints** — leia `cat ~/.claude/skills/validar-ambiente/SKILL.md` (secao "Reproduzir o bug") e siga: `params.environment` = `cliente` (padrao: o relatado no card, `env.sh card-env <card>`) ou outro; `browser start <amb|cliente> --card <card> --task <n>` → passos (`params.steps` ou os do card) com `shot` so do inicio do fluxo e do erro → `browser stop` → `evidence ... --report reproducao-<amb>.md`. Sem senha/login recusado/sem navegador: o `env.sh` ja avisa na ferramenta, so pare. Producao/DEMO: o que grava vai para o sandbox do cliente ou qa. **Sem `wait` para achar tela/URL/menu, usuario, planta ou "aceitar evidencia alternativa"** (secoes "Autonomia" e "Achar a tela" da skill): nao deu para mostrar → print do que aparece + codigo que explica + `reproduced: parcial` com "Para decidir depois:" e siga para a proxima sub-tarefa. **Conclua com `complete` mesmo sem reproduzir** (o resultado e a informacao) | `{environment, url, user, plant?, shots, reproduced:"sim\|nao\|parcial"}` | `none {reason:"so leitura/reproducao"}` |
| `notify` | Aviso curto no card com `params.message` (pt-BR; sem segredo nem dado de cliente alem do card): Timeline sempre — `prmake_timeline(card, markdown)`. Teams: o PRMake so envia pelo Teams o **pedido de aprovacao de PR** (Workflow pessoal em Minhas integracoes, botao "Pedir aprovacao" / `POST Teams/approval`); aviso que e isso e tem PR aberto → use-o; outro aviso → so Timeline e diga no `result` que o Teams nao tem envio livre | `{where:"timeline\|timeline+teams"}` | `none` (aviso nao se apaga) |

**Visao geral do item de pesquisa/analise** (sub-tarefas `research`): o item tem (ou ganha, se faltar) o arquivo
`00-visao-geral-<id>.md` (`prmake_work_item_file(..., kind: "analysis")`, `<id>` = codigo do achado/item, ex.:
`00-visao-geral-GS-002.md`; sem codigo, `item-<numero>`) desde a criacao — a pessoa le, copia, baixa e **edita** esse
arquivo na Visao geral. **Atualize-o ao concluir cada sub-tarefa `research`** (o que ja foi confirmado entra com o selo;
o que falta continua como hipotese/a confirmar) e, na **ultima**, deixe o resumo executivo final. **Antes de regravar,
leia a versao atual** (`prmake_work_item_files(card, number, file: "00-visao-geral-<id>.md")`): o que a pessoa escreveu ou
corrigiu nele fica (incorpore, nao apague). O resumo final que a Visao geral mostra no topo, pt-BR com acentos,
no **mesmo formato visual da `analise-inicial.md`** (`bash ~/.claude/skills/analisar-bug/scripts/ref.sh analise 4` e
`4b`: selos, callouts, fotografias):
- `> [!SUMMARY]` com `**Problema:**`, `**Causa (confirmada):**` (ou `(provável)`) e `**Decisão:**`;
- `### O que vai ser feito` — tabela `| # | Onde | Mudança | Por quê |` com os selos `**Alterar**`, `**Criar**`,
  `**Remover**`, `**Só leitura**`;
- `### Evidências` — cada uma com `**Confirmado:**` ou `**Hipótese:**`;
- codigo citado = `[caminho:linhas](ref:<id>)` com fotografia (`bash $PLAN coderef <card> <ref> ...` — funciona em card
  sem plano) e prints da reproducao como `![legenda](anexo:<nome>)` (o nome do arquivo do item/plano).

Catalogo do `undo` (o `desfazer-item-de-trabalho` le exatamente isto): `close-pr {repo, number}` ·
`delete-branch {repo, branch}` · `revert-commit {repo, branch, sha, createdByItem}` · `devops-state {from, action?}` ·
`none {reason}`. Valores sempre texto.

## Exemplo — "PR master → release-version" (card 77434, item #12)
1. `#13 branch` `{repository: edv-solvace, branch: hotfix/77434-rv, from: release-version}` → regra confere
   (`release-version=hotfix/77434-rv`) → worktree + push → `complete` `output {branch, from, created:"true"}`,
   `undo delete-branch`.
2. `#14 cherry-pick` dos commits de `hotfix/77434` (ja mesclados na master) → push → `undo revert-commit
   {createdByItem:"true"}`.
3. `#15 pr` → `open-pr 77434 edv-solvace hotfix/77434-rv release-version "" desc.md --item 12` → `complete` com o JSON
   impresso. Item concluido; o merge fica com o revisor.
