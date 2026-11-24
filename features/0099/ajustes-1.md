# 0099 — Ajustes 1 (2026-11-24)

Pedido do usuário, depois de usar a Mesa em produção:

1. Muitos autocompletes estão feios, sem ser autocomplete e sem filtrar/digitar texto — componente nativo do navegador.
2. Na criação de um épico, ele não sugere as tags existentes.
3. Na criação de um card, o Assigned To não lista todos os usuários (procurei "Alex" e não achei) e o componente parece
   nativo, sem a UI/UX do PRMake.
4. Na criação do épico, permitir filtrar uma lane no board do time.
5. Filtros do board do épico salvos e considerados pelo especialista no board do time.

## Diagnóstico
- `<select>` nativos: board (time/board), painel do card (estado/coluna/lane), diálogo do épico (time/board).
- Os `mat-autocomplete` existentes (campos picklist, pessoas, tags) usam `class="dv-panel"` com o CSS no componente: o
  painel é overlay (fora do componente) e fica sem estilo.
- Pessoas: só membros dos times (produção: 24 times, 63 pessoas); quem não é membro de time não aparece.

## Contrato

### Back (branch `fix/0099-mesa-ajustes`)
- `GET DevOps/meta/people?q=&top=` — junta fontes, sem duplicar (chave `uniqueName`, sem caixa), ordem por nome:
  1. membros de todos os times (como hoje, cache 10 min);
  2. usuários da organização pelo Graph (`https://vssps.dev.azure.com/{org}/_apis/graph/users?subjectTypes=aad,msa`,
     paginado por `x-ms-continuationtoken`, cache 1 h) — melhor esforço (PAT sem escopo → ignora, loga 1x);
  3. com `q` (≥ 2 letras): busca de identidades (`https://vssps.dev.azure.com/{org}/_apis/identities?searchFilter=General&filterValue={q}&queryMembership=None`) — melhor esforço;
  4. pessoas que aparecem em cards recentes (WIQL `[System.ChangedDate] >= @today-180`, até 2000 ids; `System.AssignedTo`,
     `System.CreatedBy`, `System.ChangedBy` pelo `workitemsbatch`; cache 30 min) — funciona com o escopo de work items.
  Filtro `q` sem acento/caixa em displayName e uniqueName; `top` padrão 50 com `q`, sem `q` devolve todos.
- `GET Epics/tags?q=` → `string[]` — tags usadas nos épicos (todas as situações), contagem desc, depois nome.
- Épico ganha `boardFilters` (jsonb, migração `EpicBoardFilters0099`): `{ lanes: string[], types: string[], states: string[],
  assignedTo: string[] (uniqueName), tags: string[], text: string }` — `SaveEpicRequest.boardFilters` (opcional; null = não
  mexe) e `PUT Epics/{n}/board-filters` (corpo = o objeto; publica `epicsUpdated` action `filters`). Respostas trazem
  `boardFilters` (sempre objeto, listas vazias por padrão).
- `POST DevOps/board/items` ganha `useEpicFilters?: bool` (padrão false na REST): com `epic`, aplica os filtros salvos do
  épico no servidor (lanes/tipos/estados/responsável/tags/texto em título ou id). Resposta ganha `filtersApplied` (o objeto
  aplicado, ou null).
- MCP novo `prmake_devops_board(epic?, team?, board?, scope? = "board", useEpicFilters? = true, top?)` → colunas/lanes do
  board + cards (id, tipo, título, estado, coluna, done, lane, responsável, tags) com os filtros do épico aplicados; sem
  team/board usa os do épico. Leitura (não vai para ActingTools).
- Contexto do especialista (`CaseChat/E…/context`) mostra os filtros salvos e a regra: "no board do time, considere os
  filtros salvos do épico — `prmake_devops_board(epic)` já aplica; para ver além deles, passe useEpicFilters=false e diga
  isso na resposta". Skill `especialista-de-gestao` com a mesma regra.

### Front (branch `fix/0099-mesa-ajustes`)
- Componente novo `app-pm-combo` (`components/pm-combo/`): campo com digitação + filtro (sem acento/caixa) sobre
  `mat-autocomplete`, opções com ícone/cor/avatar/dica, `multiple` opcional (chips), `allowEmpty`/rótulo do vazio,
  `loading`, `search` assíncrono opcional (`(query)` output com debounce para busca no servidor), teclado, limpar (×),
  `panelClass` com estilo GLOBAL no `styles.scss` (tokens do PRMake, escuro). Lógica pura em `pm-combo.logic.ts` + spec.
- Trocar TODOS os `<select>` nativos da Mesa: board (time/board), painel (estado/coluna/lane), diálogo do épico
  (time/board + lanes). Picklists, área/iteração e pessoas do `devops-field-input` passam a usar `app-pm-combo` (ou ao
  menos o painel com estilo global). Tags (`devops-tags-input`) com painel estilizado.
- Pessoas: busca no servidor com `q` (debounce 250 ms) — não baixa só a lista dos times.
- Diálogo do épico: tags com chips + autocomplete das tags existentes (`GET Epics/tags` + tags do DevOps) + criar nova;
  seção "Board do time" com lanes (multi, das lanes do board escolhido) → `boardFilters.lanes`.
- Board: filtro de Lane na barra; filtros (texto, tipo, responsável, tags, estado, lane) carregam de `epic.boardFilters` e
  são salvos no épico (debounce 600 ms, `PUT Epics/{n}/board-filters`) — valem para "Cards do épico" e "Board do time";
  aviso discreto "filtros salvos no épico (o especialista usa)" + "Limpar filtros".
