# Plano — Feature 0099: Mesa de gestão (épicos + tarefas administrativas + DevOps no PRMake)

Worktrees: back `prform.api-0099` (branch `feature/0099`), front `../solvace.prform.web/prform-app-0099`
(branch `feature/0099`), ambos a partir da master. Agentes paralelos trabalham em worktrees próprios
(`prform.api-0099-devops` / `prform-app-0099-board`, branches `feature/0099-devops` / `feature/0099-board`) e o Q1
mescla em `feature/0099`. **Este contrato é a fonte da verdade entre os agentes.**

## Decisões

### Nome, rota, acesso
- **Mesa de gestão** (aba "Gestão", ícone `space_dashboard`), rota `auth/desk?epic=<n>&card=<id>&item=<n>&tab=board|items|cards|files|reported`.
  Par natural da **Bancada** (onde o card é trabalhado): a Mesa é onde se *gere* — relatórios, duplicados,
  entendimento, RCAs, criação e organização de cards no DevOps.
- Entrada no menu lateral logo abaixo de "Bancada". Aberta a todo usuário logado (as gravações no DevOps usam a
  integração pessoal de quem age — o próprio DevOps limita o que cada um pode fazer). Tab interna com `card: false`.

### Épico (registro do PRMake, não confundir com o Epic do DevOps)
- Tema de trabalho com título, descrição (markdown), status (`active|paused|done|archived`), cor, tags, time/board
  preferido do DevOps e, opcionalmente, o id de um card do DevOps que o representa (Epic/Feature).
- **Número da sequência global** `execution.work_item_number_seq` (a mesma de itens e sub-tarefas): `#n` identifica
  sem ambiguidade épico, item ou sub-tarefa.
- **Chave de dono** `E{número}` (ex.: `E1234`) — usada como `CardNumber` em tudo que hoje é "por card" (itens de
  trabalho, conversa do especialista, arquivos, relatados, pedidos da fila, grupo de tempo real `execplan:E1234`).
  Assim os componentes da Bancada funcionam sem mudança; os pontos que assumem card do DevOps passam a reconhecer a
  chave (`EpicKey.TryParse`). Helper único no domínio: `EpicKey.Of(long) / TryParse(string, out long) / IsEpic(string)`.
- **Cards do épico** (`EpicCard`): cards do DevOps criados pela Mesa dentro do épico (`created`), adicionados à mão
  (`linked`) ou mencionados por `#<épico>` numa conversa/Timeline/nota de card (`mentioned`). Um card pode estar em
  vários épicos. O board do épico mostra só esses cards (padrão) ou o board inteiro do time (alternância).
- Trabalhar num card sem épico: a busca da Mesa abre o **painel do card** (campos, tags, coluna, comentário) direto;
  "Adicionar ao épico" e "Abrir na Bancada" ficam no painel.

### Comunicação Mesa ↔ Bancada
- `#n` / `##n` funcionam nos dois sentidos (mesma sequência): mencionar um item da Bancada num épico (ou o contrário)
  vira **relatado** (0094) pela regra que já existe (dono diferente). `GET WorkItems/{n}` resolve também número de
  épico → `{kind:"epic", epic:{...}}` (o popover do `#n` mostra o épico).
- Mencionar `#<épico>` numa conversa/Timeline/nota de um card adiciona o card ao épico (`mentioned`).
- Na Bancada, o cabeçalho mostra os chips dos épicos do card (`GET Epics/by-card/{card}`) com link para a Mesa.

### Tarefas administrativas (itens de trabalho do épico)
- Mesmos itens/sub-tarefas da 0085 (executar em paralelo, cancelar/desfazer, arquivos, entregas).
- Tipos de item novos (catálogo `WorkItemTypes`, seed idempotente): `report` (Relatório), `duplicates` (Duplicados),
  `understanding` (Entendimento), `rca` (RCA), `devops` (Gestão de cards), `planning` (Planejamento — criar histórias
  e organizar cards).
- Kinds de sub-tarefa novos (`WorkItemTaskKinds`): `devops-query` (consulta WIQL/filtro), `devops-create`,
  `devops-update` (campos/tags), `devops-move` (coluna/lane), `report` (gera relatório markdown), `duplicates`,
  `understanding`, `rca-doc`, `pdf` (converte um markdown do item em PDF nos arquivos do item).
- Modelos (`WorkItemTemplates`, seed): `relatorio-bugs` (consulta → relatório → PDF), `itens-duplicados`
  (consulta → duplicados → PDF), `entendimento-card` (entendimento → resumo), `rca-pdf` (RCA → PDF),
  `criar-historias` (planejamento → criar cards → mover).
- Execução: o executor roda `executar-item-de-trabalho`, que delega os kinds de gestão à skill nova
  `tarefas-de-gestao` (instruções por kind). Toda gravação pelo PRMake (MCP/REST), nunca PAT local.

### PDF
- Gerado **no servidor**: Markdig (markdown → AST) + PDFsharp-MigraDoc (MIT) — títulos, parágrafos, listas, tabelas,
  código, citações, links, imagens `data:`/arquivos do item; cabeçalho com título/épico/data, rodapé com página.
  Fontes: `fonts-dejavu-core` no Dockerfile (Linux) + resolver que procura DejaVu/Arial nas pastas do sistema
  (macOS/Linux/Windows). **QuestPDF fica fora** (licença comunitária só para receita < US$ 1 mi).
- `POST WorkItems/{n}/files/pdf` grava como arquivo do item (`kind=pdf`); `POST Pdf/render` devolve o PDF para
  download de qualquer markdown (botão "Baixar PDF" do visualizador de arquivos).

### Board do DevOps ("exatamente igual o DevOps")
- Colunas, limites WIP, colunas divididas (Doing/Done), lanes (swimlanes), mapeamento coluna→estado por tipo, campo
  de coluna/lane/done (`WEF_*_Kanban.Column|Lane|Column.Done`) — **tudo lido do DevOps** (`work/boards`), cache de 60 s
  com `configHash`. A tela consulta de novo ao abrir, a cada 60 s visível e ao receber `devopsCardsUpdated`; hash
  novo ⇒ redesenha (colunas/estados/lanes novos aparecem sozinhos).
- Arrastar: entre colunas (grava coluna + estado mapeado do tipo + done), entre lanes, dentro da coluna (ordem —
  `work/workitemsorder`), Doing↔Done na coluna dividida. Coluna que não aceita o tipo (sem mapeamento) fica
  bloqueada durante o arraste. Otimista com rollback e aviso em erro. Cartão como no DevOps: barra de cor do tipo,
  id, título, responsável (avatar), estado, tags, campos extras do board quando houver.
- Filtros (como o DevOps): texto, tipo, responsável, tags, estado; alternância "Cards do épico | Board do time".

### Edição de campos
- Painel do card: campos do tipo (`wit/workitemtypes/{t}/fields?$expand=allowedValues`) — picklist com autocomplete
  quando há `allowedValues`; Área/Iteração por árvore (classification nodes) com autocomplete; Responsável pelos
  membros dos times (autocomplete); texto/HTML/número/data conforme o tipo do campo; somente leitura respeitado.
- Tags: chips com autocomplete/busca nas tags do projeto (`wit/tags`) + "Criar tag «x»" quando não existe.
- Criar card: tipo (dos tipos do projeto, com cor/ícone), título, campos obrigatórios do tipo + comuns
  (descrição, área, iteração, responsável, prioridade), tags, pai (opcional), épico atual (adiciona como `created`).

### Especialista na Mesa
- Mesmo `app-case-compact` com `card = E{n}` e `cardType = 'epic'`. Contexto do caso para chave de épico =
  épico + cards (estado/coluna) + itens + board (time/board/colunas/lanes) + regras de gestão + ferramentas.
- Prompt da fila escolhe a skill `especialista-de-gestao` quando a chave é de épico (cards seguem com
  `especialista-do-caso`). Ela faz tudo que a tela faz (criar/editar/mover cards, tags, consultas, relatórios,
  duplicados, entendimento, RCA, PDF, gerir o épico e itens) e mais: triagem em lote, quebra de épico em histórias,
  resumo semanal, sugestão de tags/área, checagem de duplicidade antes de criar card.
- Guardião do caso: com chave de épico as ferramentas de DevOps valem para qualquer card (com a integração de quem
  pediu — `ActingTools`); com chave de card, só no próprio card (criar só com `parent` = o card).

## Contrato

### Domínio (schema `execution`, `ExecutionPlanContext`) — B1
`Epic`: Id (Guid), Number (long, `nextval('execution.work_item_number_seq')`, único), Title (≤ 300), Description
(text), Status, Color (≤ 20, ex. `#7a5cff`), TagsJson (jsonb `string[]`), DevOpsTeam? (≤ 200), DevOpsBoard? (≤ 200),
DevOpsCardId? (string ≤ 100), CreatedBy (Guid?), CreatedByName, CreatedAt, UpdatedAt, ClosedAt?, Version (xmin).
`EpicCard`: Id (Guid), EpicId, CardNumber (≤ 100), Role (`created|linked|mentioned`), Title? (≤ 300, foto),
WorkItemType? (≤ 100, foto), AddedBy (Guid?), AddedByName, AddedAt. Único (EpicId, CardNumber); `linked`/`created`
nunca rebaixa para `mentioned`.
Migração `Epics0099` (EP infra). `ResolveNumberAsync` ganha épico. `WorkItem.Type` validado pelo catálogo (já é).

### REST `api/v1/Epics` — B1 (auth x-api-key; token do caso só lê e só o próprio épico)
| Método | Rota | Corpo / retorno |
|---|---|---|
| GET | `?status=&q=&mine=` | `EpicSummary[]` ordem UpdatedAt desc |
| GET | `{number:long}` | `EpicResponse` (com `cards`) |
| POST | `` | `SaveEpicRequest {title, description?, color?, tags?, devOpsTeam?, devOpsBoard?, devOpsCardId?}` → 201 `EpicResponse` |
| PUT | `{number}` | `SaveEpicRequest` → `EpicResponse` |
| POST | `{number}/status` | `{status}` |
| POST | `{number}/cards` | `{card, role?}` (role padrão `linked`) → `EpicCardResponse` |
| DELETE | `{number}/cards/{card}` | 204 |
| GET | `by-card/{card}` | `EpicSummary[]` |

`EpicSummary`: `{id, number, key, title, status, color, tags, devOpsTeam?, devOpsBoard?, devOpsCardId?, createdByName,
createdAt, updatedAt, counts:{cards, items, openItems}}`. `EpicResponse` = summary + `description`, `cards:
EpicCardResponse[]`. `EpicCardResponse`: `{card, role, title?, workItemType?, addedByName, addedAt}`. `key = "E"+number`.
`GET WorkItems/{n}` com número de épico → 200 `{kind:"epic", epic: EpicSummary}` (item continua `kind` ausente ou
`"item"`).

### Tempo real
- Grupo `execplan:E{n}` (igual ao card): `workItemsUpdated`, `caseChatUpdated` sem mudança.
- Grupo `epics` (todos na Mesa): evento **`epicsUpdated`** `{number, action}` (created|updated|status|cards).
- Grupo `devops`: evento **`devopsCardsUpdated`** `{ids:string[], action}` (created|updated|moved|reordered) depois de
  qualquer gravação de card pelo PRMake. Sinais: a tela refaz o GET.

### REST `api/v1/DevOps` — B2 (auth x-api-key; integração pessoal "AzureDevOps Configurations")
| Método | Rota | Retorno |
|---|---|---|
| GET | `meta/types` | `[{name, color, icon, description, isDisabled}]` |
| GET | `meta/fields?type=` | `[{referenceName, name, type (string|integer|double|dateTime|html|plainText|boolean|treePath|identity|picklistString|picklistInteger), required, readOnly, allowedValues[], defaultValue?, helpText?}]` |
| GET | `meta/tags?q=` | `string[]` (todas do projeto, filtro contém, ignora caixa) |
| GET | `meta/areas` / `meta/iterations` | `[{path, name, depth}]` (achatado) |
| GET | `meta/people?q=` | `[{displayName, uniqueName, id, imageUrl?}]` (membros dos times, cache 10 min) |
| GET | `teams` | `[{id, name}]` |
| GET | `boards?team=` | `[{id, name}]` |
| GET | `board?team=&board=` | `BoardConfig {team, board, configHash, columns:[{id, name, itemLimit, columnType (incoming|inProgress|outgoing), isSplit, description?, stateMappings:{type: state}}], lanes:[{id, name, isDefault}], fields:{column, lane, done}, types:[{name, color}], orderField}` |
| POST | `board/items` | `{team, board, epic?: long, scope: "epic"|"board", text?, types?, assignedTo?, tags?, states?, top? (300)}` → `{configHash, items: BoardItem[]}` |
| POST | `query` | `{wiql?, filter?:{types?, states?, areaPath?, iterationPath?, tags?, assignedTo?, text?, createdFrom?, createdTo?, changedFrom?}, fields?: string[], top? (500)}` → `{count, items:[{id, fields:{ref: value}}]}` |
| GET | `workitems/{id}` | `CardDetail {id, rev, type, title, state, fields:{ref: value}, tags[], url, relations:[{rel, id?, url}]}` |
| POST | `workitems` | `{type, title, fields?:{ref: value}, tags?, parentId?, epic?: long, team?, board?, column?, lane?}` → `CardDetail` (+ `EpicCard created`) |
| PATCH | `workitems/{id}` | `{fields?:{ref: value|null}, addTags?, removeTags?, comment?}` → `CardDetail` |
| POST | `workitems/{id}/move` | `{team, board, column, lane?, done?, previousId?, nextId?}` → `CardDetail` (coluna + estado mapeado + done + lane; ordem pelo `workitemsorder` quando vier vizinho) |
| POST | `workitems/{id}/comment` | `{text}` (markdown → html) |

`BoardItem`: `{id, rev, type, title, state, assignedTo?:{displayName, uniqueName, imageUrl?}, tags[], column, done,
lane, order, priority?, parentId?, changedDate}`. Erros do DevOps → 400/403/409 `{code, message}` com a mensagem do
DevOps (ex. regra de transição).

### PDF — B1
- `POST WorkItems/{number}/files/pdf` `{name, title?, markdown, taskNumber?}` → `WorkItemFileResponse` (kind `pdf`).
- `POST Pdf/render` `{title?, markdown, name?}` → `application/pdf`.
- Serviço `IMarkdownPdfRenderer` em `Cime.BuildingBlocks` (ou módulo ExecutionPlans infra) — sem I/O de rede.

### MCP — B1 (épico/PDF) e B2 (DevOps)
| Ferramenta | Uso | Dono |
|---|---|---|
| `prmake_epics(q?, status?)` / `prmake_epic(number)` | listar / detalhar épico | B1 |
| `prmake_epic_save(number?, title, description?, color?, tags?, devOpsTeam?, devOpsBoard?)` | criar/editar | B1 |
| `prmake_epic_cards(number, add?, remove?)` | cards do épico | B1 |
| `prmake_pdf(card, item, name, markdown, title?, task?)` | markdown → PDF nos arquivos do item | B1 |
| `prmake_devops_meta(what: types|fields|tags|areas|iterations|people|teams|boards|board, type?, q?, team?, board?)` | metadados/picklists | B2 |
| `prmake_devops_query(wiql? | filtros, fields?, top?)` | consulta | B2 |
| `prmake_devops_card_get(id)` | card com campos | B2 |
| `prmake_devops_card_create(type, title, fields?, tags?, parentId?, epic?, column?, lane?)` | criar | B2 (acting) |
| `prmake_devops_card_update(id, fields?, addTags?, removeTags?, comment?)` | editar | B2 (acting) |
| `prmake_devops_card_move(id, column, lane?, done?, team?, board?, previousId?, nextId?)` | mover | B2 (acting) |

Com token do caso: chave de épico ⇒ qualquer card; chave de card ⇒ só o próprio (create só com `parentId` = card).

### Especialista / fila — B1
- `CaseChat/{E123}/context` → markdown de gestão (épico, cards, itens, board, regras, ferramentas).
- `SpecialistPrompt`: chave de épico ⇒ `~/.claude/skills/especialista-de-gestao/SKILL.md`.
- `Skills/rules?card=E123` ⇒ regras de gestão (gravação só pelo PRMake, nunca mesclar, confirmar antes de lote > 10
  cards, checar duplicado antes de criar, PDF pelo servidor).
- Ferramentas do especialista listadas para o composer (`/`) por tipo: incluir `epic` com os modelos de gestão.

### Skills — S1
- `skills/especialista-de-gestao/` (SKILL.md + skill.json + scripts/prmake-desk.sh se útil): guardião do épico.
- `skills/tarefas-de-gestao/` (SKILL.md): instruções por kind (`devops-query`, `devops-create`, `devops-update`,
  `devops-move`, `report`, `duplicates`, `understanding`, `rca-doc`, `pdf`), formato dos relatórios (markdown com
  tabelas, sumário executivo, gráficos em tabela), critérios de duplicidade (título normalizado, termos, repro,
  área, janela de datas; nunca fechar/mesclar card sem a pessoa decidir), RCA no Modelo do RCA do prompt de Bug.
- `executar-item-de-trabalho`: kinds de gestão → `tarefas-de-gestao`; chave `E…` = épico (sem branch/PR).
- README das skills.

### Front — F1 (página) / F2 (board e DevOps)
- F1: rota `desk`, menu, tab; `app-desk` (`pages/authenticated/desk/`): trilho de épicos (busca, filtro
  meus/todos/status, + Novo épico, cor), cabeçalho do épico (#n, título, status, contagens, editar, tags), abas
  `Board | Itens | Cards | Arquivos | Relatado`, lateral Especialista (`app-case-compact` card=`E{n}`
  cardType=`'epic'`), busca de card (abre o painel do card), estado vazio com "Criar primeiro épico", diálogos de
  épico, mobile (abas), tour `desk.tour.ts`. `EpicsService`, tempo real `epicsUpdated`. `cardType` aceita `'epic'`
  (rótulos de gestão). Bancada: chips dos épicos do card. Aba **Cards**: lista dos cards do épico (tipo, id, título,
  estado, coluna, papel, remover) + "Adicionar card" (busca por id/título).
  Na aba Board, F1 deixa `<app-devops-board [team] [board] [epic] [scope]>`; no painel do card
  `<app-devops-card-panel [cardId] [epic] (closed)>`; criar `openDevOpsCardCreate(dialog, {epic, team, board})` —
  componentes do F2 (F1 cria stubs mínimos só se precisar compilar; o Q1 troca).
- F2: `DevOpsService` (todas as rotas acima), `app-devops-board` (CDK drag-drop: colunas, Doing/Done, lanes
  recolhíveis, WIP, ordem, bloqueio de coluna sem mapeamento, otimista + rollback, filtros, polling 60 s com
  `configHash`, `devopsCardsUpdated`), `app-devops-card-panel` (campos por tipo com picklist/autocomplete, área/
  iteração, pessoas, tags com criar, comentário, mover, "Abrir na Bancada", "Adicionar ao épico"),
  `app-devops-tags-input`, `app-devops-field-input`, diálogo de criar card. Arquivos em `components/devops/`.

## Fases (ondas)

| Fase | O quê | Dono | Depende |
|---|---|---|---|
| B1 | Épicos (domínio, migração, repo, app, controller, tempo real), chave `E…` nos pontos de card (resolve, contexto, regras, prompt, menções `#épico`), seeds de gestão, PDF (renderer + endpoints), MCP de épico/PDF, Dockerfile fontes | agente B1 (`prform.api-0099`) | — |
| B2 | DevOps: serviço (tipos, campos, tags, áreas/iterações, pessoas, times, boards, itens do board, consulta, criar, editar, mover, ordem, comentário), `DevOpsController`, `DevOpsMcpTools`, `ActingTools`, tempo real `devopsCardsUpdated` | agente B2 (`prform.api-0099-devops`) | contrato |
| F1 | Página Mesa + épicos + cards do épico + chips na Bancada + tour | agente F1 (`prform-app-0099`) | contrato |
| F2 | Board DevOps + painel/criação de card + tags/campos | agente F2 (`prform-app-0099-board`) | contrato |
| S1 | Skills `especialista-de-gestao`, `tarefas-de-gestao`, `executar-item-de-trabalho` | agente S1 (`prform.api-0099`, só `skills/`) | contrato |
| Q1 | Mesclar ramos, ligar F1↔F2, build back+front, testes, e2e local (harness `.t0099`) | Claude | todas |
