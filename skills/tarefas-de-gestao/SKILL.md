---
name: tarefas-de-gestao
description: Instrucoes por tipo de sub-tarefa de GESTAO dos itens de trabalho do PRMake (feature 0099, Mesa de gestao) — consultar cards no DevOps (devops-query), criar/editar/mover cards (devops-create, devops-update, devops-move) com checagem de duplicidade, tags existentes e confirmacao de lote, relatorio de bugs em markdown com tabelas e sumario executivo (report), itens duplicados com grupos, confianca e recomendacao (duplicates), entendimento de bug/US com as regras da Base Solvace (understanding), RCA no Modelo do RCA do prompt de Bug (rca-doc) e PDF de um markdown do item (pdf). Numeros vem do script (gestao.sh: consulta pelo PRMake, contagens, grupos, parados), nunca estimados. Toda gravacao pelo PRMake; nunca fecha, remove nem mescla card sem a pessoa decidir. Usada pela executar-item-de-trabalho quando o kind da sub-tarefa e de gestao (item de epico E<n> ou de card) e pelo especialista-de-gestao.
---

# tarefas-de-gestao

Guia dos **kinds de gestao** das sub-tarefas de itens de trabalho (0099). Quem chama e a `executar-item-de-trabalho`
(o ciclo `start` → fazer → `complete`/`fail`/`wait` e o `next` continuam la) — aqui esta o **"fazer"** de cada kind, com o
`result`, o `output` e o `undo` esperados. O item pode ser de um **epico** (chave `E<numero>`, ex.: `E1234` — Mesa de
gestao) ou de um card do DevOps (Bancada); a `<chave>` abaixo e o `card` do item.

Instalada e atualizada pelo PRMake (fonte: `skills/tarefas-de-gestao`). Token: env `PRMAKE_TOKEN` ou
`~/.claude/prmake-token.txt`.

```bash
GES=~/.claude/skills/tarefas-de-gestao/scripts/gestao.sh   # fetch · pull · save · pdf · report · duplicates · stale · rca-check
AB=~/.claude/skills/analisar-bug/scripts                    # bug-fetch.sh (card inteiro, so leitura)
DIR=~/.prmake/cards/<chave>/gestao                          # consultas (.json), relatorios (.md) — copia local
```

## Regras que nao mudam
1. **Toda gravacao pelo PRMake**: cards (`prmake_devops_card_create/update/move`), arquivos do item
   (`prmake_work_item_file` ou `$GES save`), PDF (`prmake_pdf` ou `$GES pdf`), andamento (`prmake_work_task`). Leitura
   tambem pelo PRMake (`prmake_devops_query`/`prmake_devops_card_get`/`prmake_devops_meta` ou `$GES fetch`, que chama a API
   do PRMake). Nada de PAT nem API direta do DevOps. Cada gravacao no DevOps usa a integracao pessoal de quem roda.
2. **Nunca feche, remova nem mescle card sem a pessoa decidir**: estado/coluna de fechamento (`Closed`, `Done`,
   `Removed`, coluna `outgoing` do board), apagar, ligar como duplicado. Sub-tarefa que pede isso sem a decisao escrita
   (`params.authorization` ou `**Resolvido por <pessoa>:**` no `result`) → `wait` com a lista do que seria feito.
3. **Lote > 10 cards gravando** (criar, editar, mover) → confirmacao: sem `params.confirmed = "sim"` nem
   `**Resolvido por <pessoa>:**` no `result`, grave a lista como arquivo do item (`lote-<slug>.md`: card, o que muda, de →
   para) e `wait` com "Confira `lote-<slug>.md` e clique em *Ja resolvi* para confirmar". Com a confirmacao, grave
   **so** os da lista.
4. **Antes de criar card, procure duplicado**; **antes de tag nova, procure a existente** (secao `devops-create`).
5. **Picklists sao a lei**: valor de campo com `allowedValues`, area, iteracao, responsavel, tipo, coluna, lane — sempre
   um que `prmake_devops_meta` devolveu. Nunca invente estado, coluna, area nem nome de pessoa; nao achou → `wait` com as
   opcoes validas mais proximas.
6. **Numeros vem do `$GES`** (contagens deterministicas sobre a consulta salva), nunca estimados. Diga a fonte (consulta,
   data, quantos cards; se veio menos que o total por causa do `top`, diga).
7. **Item de epico (`E…`) nao tem branch, PR, worktree, plano nem estado de card**: `prmake_card`/`prmake_rules` do card
   nao se aplicam — o epico e `prmake_epic(<numero>)` (time/board preferidos, cards do epico). Timeline, horas e acoes do
   card (`prmake_timeline`, `prmake_devops`) sao **de um card do DevOps**: passe o numero do card, nunca `E…`.
8. **Custo**: nada de endpoints de IA do servidor; consultas com os campos que a tarefa usa e `top` coerente (padrao 500).
   Leia texto grande de card (repro, discussion) so dos cards que a tarefa pede, nao do lote inteiro.
9. Documentos para pessoas: **pt-BR com acentos**, sem emojis, tabelas markdown, cards citados pelo numero (78123), itens
   de trabalho por `#n`. Sem senha, token nem dado pessoal alem do necessario. RCA segue o idioma do modelo (ingles).

## Passagem de dados entre sub-tarefas
- A consulta (`devops-query`) grava `consulta-<slug>.json` no item (kind `data`) e devolve `output.file`. As seguintes
  (`report`, `duplicates`, `devops-update`…) usam `params.source` (nome do arquivo ou `#<sub-tarefa>`); sem `source`, o
  `output.file` da ultima `devops-query` concluida do item (`prmake_work_item`).
- Copia local em `$DIR`. Nao esta la (outra sessao/maquina) → `bash $GES pull <chave> <item> <arquivo>` (baixa do item).
  **Nao** leia o JSON inteiro no chat: o `$GES` le e resume.
- Documento final (`.md`) sempre como **arquivo do item** (`$GES save <chave> <item> <arquivo.md> --task <n>` ou
  `prmake_work_item_file(card, number, name, content, kind: "analysis", taskNumber)`) e citado no `result`; documento
  entregue → `prmake_work_deliverable(card, taskNumber, kind: "doc", title, text: "<arquivo>")`.

## Ciclo de cada sub-tarefa (o mesmo da executar-item-de-trabalho)
`prmake_work_item(card, number)` (cancelado/pausado → pare) → `prmake_work_task(card, taskNumber, "start")` → o kind
abaixo → `prmake_work_task(card, taskNumber, "complete", result, output, undo)` | `"fail"` (motivo + o que tentar) |
`"wait"` (o que a pessoa decide/faz). `result` comeca por uma **manchete curta em negrito** e traz o resultado rico
(tabelas, numeros, arquivos). `output`/`undo` com valores **texto**; o catalogo do `undo` e o da executar-item-de-trabalho
(`none {reason}` quando nada se desfaz pelo PRMake — diga no `reason` como voltar a mao).

`params` vem do modelo/da pessoa; os nomes abaixo sao os usuais — o que faltar, tire da **descricao** da sub-tarefa e do
epico (`prmake_epic`: `devOpsTeam`, `devOpsBoard`, tags). Ambiguo e decide o resultado → `wait` com a pergunta objetiva.

## Guia por kind

### `devops-query` — consulta (somente leitura)
Params usuais: `wiql`, ou filtros (`types`, `states`, `areaPath`, `iterationPath`, `tags`, `assignedTo`, `text`,
`createdFrom`, `createdTo`, `changedFrom`), `fields`, `top`, `name` (slug do arquivo), `text` (`sim` = traz repro/descricao,
para duplicados/entendimento).
1. Monte a consulta. Filtro pronto serve? use-o. WIQL: sempre `[System.TeamProject] = @project`; datas com `@today - 90`;
   area com `UNDER`; ordem explicita. Area/iteracao/estado/tipo **validos** de `prmake_devops_meta(what: areas|iterations|types|board)` — nunca
   deduzidos. Ex.:
   ```sql
   SELECT [System.Id] FROM WorkItems
   WHERE [System.TeamProject] = @project AND [System.WorkItemType] = 'Bug'
     AND [System.CreatedDate] >= @today - 90 AND [System.AreaPath] UNDER 'Solvace\Revamp'
   ORDER BY [System.CreatedDate] DESC
   ```
2. Rode e salve: `bash $GES fetch <chave> <item> consulta-<slug> --wiql "<wiql>" [--filter '<json>'] [--fields +Campo.X]
   [--text] --top <n> --task <n>` → grava `consulta-<slug>.json` no item e imprime total, estados e as 15 primeiras linhas.
   Campos de cliente/ambiente/planta (variam por processo): ache a referencia em `prmake_devops_meta(what: fields, type: Bug)` e
   acrescente com `--fields +<ref>`. Exit 3 (sem permissao na API) → `prmake_devops_query(...)` com `top` menor e grave o
   resultado com `prmake_work_item_file(..., name: "consulta-<slug>.json", kind: "data")`.
3. `result`: manchete (`**142 bugs abertos nos ultimos 90 dias.**`), a consulta em ```sql```, a contagem por estado e a
   tabela das primeiras ~20 linhas (card, tipo, estado, titulo); diga o total e se o `top` cortou.
- `output {file, count, query}` · `undo none {reason:"so leitura"}`

### `devops-create` — criar card(s)
Params usuais: `type`, `title`, `fields` (`{ref: valor}`), `tags`, `parentId`, `column`, `lane`; lote: `cards` (lista) ou
`source` (arquivo do item com a lista — ex.: a saida do planejamento). Epico do item → `epic: <numero>` (o card entra no
epico como `created`).
1. **Duplicado primeiro** (para cada card): `prmake_devops_query` com o tipo e 2–4 termos-chave do titulo (`text`),
   ultimos 180 dias, campos id/titulo/estado. Titulo igual ou muito parecido (mesmos termos, mesma area) → **nao crie**:
   `wait` com os candidatos ("78123 'Erro ao salvar...' (Active) parece o mesmo — crio mesmo assim?"), a menos que a
   pessoa ja tenha decidido (`params.allowDuplicate = "sim"` ou `**Resolvido por...**`). Lote: rode o `duplicates` do
   `$GES` sobre a lista + candidatos se ficar grande.
2. **Campos**: `prmake_devops_meta(what: fields, type: <tipo>)` — preencha os `required`, respeite `readOnly`, valores de
   picklist so dos `allowedValues`; area/iteracao de `prmake_devops_meta(what: areas|iterations)`; responsavel de
   `prmake_devops_meta(what: people, q: <nome>)` (um so resultado; varios → `wait`). Descricao/repro em HTML simples.
3. **Tags**: `prmake_devops_meta(what: tags, q: <termo>)` para cada tag — use a grafia existente ("Kaizen", nao "kaizen");
   tag nova so se a sub-tarefa/pessoa pediu (`params.newTags` ou a descricao diz) — liste no `result` as tags criadas.
4. Coluna/lane (opcional): validas no `prmake_devops_meta(what: board, team, board)` e aceitas pelo tipo (`stateMappings`).
5. Lote > 10 → regra 3. Depois `prmake_devops_card_create(type, title, fields, tags, parentId, epic, column, lane)` um a
   um; erro do DevOps (regra de transicao, campo obrigatorio) → corrija pelo meta e tente uma vez; senao anote e siga.
6. `result`: manchete (`**3 historias criadas no epico E1234.**`) + tabela `| Card | Tipo | Titulo | Coluna | Tags |`
   com os ids novos; os que falharam com o motivo; duplicados evitados.
- `output {ids:"78901,78902", created:"true", epic}` · `undo none {reason:"card criado no DevOps nao se apaga pelo PRMake — mover para Removed a mao, com decisao da pessoa: <ids>"}`

### `devops-update` — editar campos/tags/comentario
Params usuais: `ids` (ou `source`), `fields`, `addTags`, `removeTags`, `comment`.
1. Para cada card: `prmake_devops_card_get(id)` e anote os valores atuais dos campos que vao mudar (o "de").
2. Valide como no create (picklists, tags existentes, readOnly). Mudar `System.State` para fechamento → regra 2; estado
   comum → prefira `devops-move` (coluna + estado mapeado).
3. Lote > 10 → regra 3. `prmake_devops_card_update(id, fields, addTags, removeTags, comment)`.
4. `result`: tabela `| Card | Campo | De | Para |` (+ tags e comentario). O "de" e o caminho de volta.
- `output {ids, fields:"<refs>"}` · `undo none {reason:"voltar a mao com os valores 'De' do resultado"}`

### `devops-move` — mover de coluna/lane
Params usuais: `ids`, `column`, `lane`, `done` (`sim` = metade Done da coluna dividida), `team`, `board` (padrao: os do
epico), `previousId`/`nextId` (ordem).
1. `prmake_devops_meta(what: board, team, board)`: a coluna existe e aceita o tipo de cada card (`stateMappings[tipo]`);
   coluna `outgoing`/de fechamento → regra 2. Lane valida.
2. Anote a coluna/lane atual (`prmake_devops_card_get` ou o board). Lote > 10 → regra 3.
3. `prmake_devops_card_move(id, column, lane, done, team, board, previousId, nextId)` (grava coluna + estado mapeado).
4. `result`: tabela `| Card | De (coluna/estado) | Para (coluna/estado) |`; recusas do DevOps com a mensagem.
- `output {ids, column, lane?, from:"<id>=<coluna>;..."}` · `undo none {reason:"voltar com devops-move para as colunas 'De': <id>=<coluna>;..."}`

### `report` — relatorio (bugs ou cards)
Params usuais: `source`, `title`, `focus` (o que destacar), `weeks` (8), `staleDays` (14), `closedStates`.
1. Dados: o `source` (regra da passagem). Sem consulta antes no item → faca a `devops-query` aqui mesmo (mesmo passo 2).
2. Numeros: `bash $GES report $DIR/<consulta>.json --title "<titulo>" --out $DIR/tabelas.md [--weeks N] [--stale-days N]
   [--closed-states "..."]` e leia `tabelas.md` (Numeros; por estado, severidade, prioridade, Bug Type, area, cliente,
   responsavel; por semana criados × fechados; idade; mais antigos; parados por coluna; "Fatos para as tendencias").
   Estados de fechamento do processo diferentes do padrao → `--closed-states` com os do board (`outgoing`).
3. Escreva `$DIR/sumario-<slug>.md` (so o texto; o script poe o titulo "Sumario executivo"): 1 paragrafo com o retrato
   (quantos, abertos, ritmo de entrada × saida); `### Tendências` (3–5 itens com os numeros dos "Fatos" — subindo,
   caindo, concentracao por area/cliente/pessoa, envelhecimento); `### Riscos e recomendações` (3–5 acoes objetivas:
   triagem dos sem responsavel, foco na area X, cards parados em Y). `params.focus` primeiro. So afirme o que os numeros mostram.
4. Final: `bash $GES report $DIR/<consulta>.json --title "<titulo>" --summary $DIR/sumario-<slug>.md --out
   $DIR/relatorio-<slug>-<AAAA-MM-DD>.md [mesmas opcoes]` → `bash $GES save <chave> <item> $DIR/relatorio-<slug>-<data>.md --task <n>`
   → `prmake_work_deliverable(card, taskNumber, kind: "doc", title, text: "<arquivo>")`.
5. `result`: manchete + o sumario executivo + a tabela "Numeros" e "Por estado" + "Relatorio completo: `relatorio-....md`"
   (o PDF e a sub-tarefa `pdf`, se houver).
- `output {file, cards, source}` · `undo none {reason:"so leitura"}`

### `duplicates` — itens duplicados
Params usuais: `source`, `windowDays` (120; `0` = sem janela), `min` (0.55), `scope` (o que comparar).
Criterios (o `$GES` calcula; voce confirma): **titulo normalizado** (sem acento, prefixos `[RCA]`/`AB#`, numeros e
palavras genericas), **termos-chave** em comum, **passos de reproducao/descricao** (quando a consulta trouxe `--text`),
**mesma area**, **mesmo cliente/ambiente/planta** e **janela de datas** entre as criacoes. Tipos diferentes pesam menos.
1. Dados com texto: a consulta precisa de `--text` (refaca com `fetch ... --text` se o `source` nao tem).
2. `bash $GES duplicates $DIR/<consulta>.json --out $DIR/duplicados-tabelas.md --json $DIR/duplicados.json [--window-days N] [--min X]`.
3. **Confirme** os grupos de confianca alta/media: abra so o necessario (`prmake_devops_card_get` de 2–3 cards do grupo
   quando o titulo nao basta) e classifique cada grupo: **duplicado** (mesmo defeito/pedido), **relacionado** (mesma
   area/tela, causas diferentes) ou **falso positivo** (descarte). Grupo com card fechado e outro aberto = possivel
   reincidencia — diga.
4. Documento `duplicados-<slug>-<data>.md`: sumario (quantos cards, quantos grupos por classificacao), depois cada grupo
   com a tabela do `$GES`, **por que** (criterios que bateram) e a **recomendacao** ("manter 78006 como principal e
   relacionar 78046, 78036 como duplicados; 78056 ja fechado — conferir se e reincidencia"). Grave com `$GES save` e
   registre o `doc`.
5. **Nunca feche, ligue nem mova** card aqui. No `result`, ofereca: "para aplicar, crie um item de Gestao de cards com
   `devops-update` (comentario/tag `duplicado-de-<id>`) depois da decisao".
- `output {file, groups, cards}` · `undo none {reason:"so leitura"}`

### `understanding` — entendimento de bug/US
Params usuais: `card` (ou `cards`), `focus`.
1. Card inteiro (so leitura): `OUTDIR=~/.prmake/cards/<card>/dados bash $AB/bug-fetch.sh <card>` e leia **inteiros**
   `card-resumo.txt`, `description.txt`, `criterios-aceite.txt` (US) e `discussion.txt`. Ja tratado na Bancada?
   `prmake_case_context(<card>)` e `prmake_work_items(<card>)` (analise, decisoes, itens) — use, nao refaca.
2. Base Solvace antes de codigo: `prmake_base_search(card: <card>, ...)` com os termos do pedido → `prmake_base_get` das
   regras/casos de uso que importam → `prmake_base_impact` se a mudanca afeta outros modulos.
3. Escreva `entendimento-<card>.md` (pt-BR, para quem nao e desenvolvedor):
   - `## O que pede` — Bug: o que acontece × o esperado, quem sofre; US: o pedido e os criterios de aceite (lista).
   - `## Contexto` — cliente, ambiente/planta, modulo/tela, desde quando, o que a discussion mudou, cards relacionados.
   - `## Regras afetadas` — tabela `| Item da Base | O que diz | Como entra aqui |` (`modulo#RN-012`, `UC-003`); lacuna
     da Base → diga.
   - `## Dúvidas em aberto` — perguntas objetivas, para quem (cliente, PO, time).
   - `## Próximo passo sugerido` — analisar na Bancada, quebrar em historias, pedir evidencia...
4. Grave no item (`$GES save` ou `prmake_work_item_file`, kind `analysis`) e registre o `doc`. Varios cards → um arquivo
   por card + `entendimento-resumo.md` com uma linha por card.
- `result`: manchete + o entendimento (ou o resumo do lote). `output {file, cards}` · `undo none {reason:"so leitura"}`

### `rca-doc` — RCA em documento
Params usuais: `card` (ou `cards`), `language` (`pt` = tambem uma versao em portugues para leitura interna).
1. Fontes: card inteiro (`bug-fetch.sh`, como no `understanding`), o que o PRMake ja sabe do card
   (`prmake_case_context(<card>)`: analise, causa raiz, PRs mesclados, chamado/script; `prmake_work_items(<card>)`) e a
   Base Solvace. Causa nao confirmada em lugar nenhum → escreva como **provavel** e diga o que falta confirmar.
2. Escreva `rca-<card>.md` **no "Modelo do RCA" do prompt de Bug do PRMake** — o mesmo da `analisar-bug`/`gerar-prmake`:
   ingles, um titulo em negrito por secao (Main Cause, Contributing Factors, Impact, Applied Solution, Action/Prevention
   Plan, Modified files/…/Altered data in the database, Ticket com o numero do card). Sem codigo: "No code changes" +
   dados alterados. O modelo vem do prompt configurado — nao fixe titulos de memoria.
3. Confira: `bash $GES rca-check <arquivo> <card>` (baixa o prompt de Bug do PRMake e roda o `rca_check.py` da
   `gerar-prmake`). Exit 2 → reescreva no modelo e confira de novo.
4. Grave no item (kind `analysis`) e registre o `doc`; `language = pt` → tambem `rca-<card>-pt.md` (traducao fiel, so para
   leitura). **Nao** grave no campo de root cause nem na discussion do card: isso e do fechamento do card (ferramenta de
   fechamento na Bancada dele). Pedido explicito de publicar → `wait` sugerindo criar o item de fechamento no card.
- `result`: manchete + o RCA inteiro. `output {file, card, checked:"true"}` · `undo none {reason:"so documento"}`

### `pdf` — markdown do item em PDF
Params usuais: `file` (markdown do item; padrao: o `output.file` da sub-tarefa anterior), `name`, `title`.
1. O `.md` local em `$DIR` ou `bash $GES pull <chave> <item> <arquivo.md>`. Confira que e o documento final (sem
   rascunho, tabelas fechadas, imagens como `![legenda](anexo:<nome>)` de arquivos do item).
2. Gere no servidor: `bash $GES pdf <chave> <item> $DIR/<arquivo.md> --name <nome>.pdf --title "<titulo>" --task <n>`
   (ou `prmake_pdf(card, item, name, markdown, title, task)`). Erro do renderizador → `fail` com a mensagem.
3. Registre o `doc` (`prmake_work_deliverable(..., kind: "doc", title: "<nome>.pdf")`).
- `result`: `**PDF gerado: <nome>.pdf.**` + de qual markdown e quantas paginas/bytes. `output {file:"<nome>.pdf", from:"<arquivo.md>"}` · `undo none {reason:"arquivo do item"}`

## Extras uteis (quando a descricao pedir)
- **Parados por coluna**: `bash $GES stale $DIR/<consulta>.json --days 14` (abertos sem mudanca, agrupados pela coluna).
- **Resumo semanal do epico**: consulta dos cards do epico (`prmake_epic` → ids) com `changedFrom = @today - 7` + itens do
  epico concluidos na semana (`prmake_work_items(E…)`) → o que andou, o que entrou, o que travou, proximos passos.
- **Triagem em lote**: para cada card novo sem area/prioridade/tags, sugira valores (pelos parecidos ja classificados e
  pela Base) numa tabela `| Card | Sugestao | Por que |` — gravar so como `devops-update` com confirmacao (regra 3).
