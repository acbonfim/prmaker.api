# Feature 0094 — Relatado (itens de trabalho relacionados)

## Pedido
Nova aba **Relatado** na Bancada do card (ao lado de *Itens de trabalho | Execução | Arquivos | Card*).
Lista itens de trabalho (e suas sub-tarefas) que foram **relacionados** a este card:

1. **mencionados** numa conversa do Especialista (`#xxx` / `##xxx`);
2. **mencionados** em outras partes do sistema (plano/análise, Timeline/comentários, descrição/sub-tarefas de itens);
3. **inseridos manualmente**, buscando por número ou título na base.

Os relatados **reaproveitam exatamente os mesmos componentes** da aba *Itens de trabalho* (lista + detalhe + sub-tarefas),
mas **somente leitura**: sem editar, sem criar, sem destravar pendência ("Já resolvi"), sem executar/cancelar/desfazer.

Além da tela, os relatados **entram no contexto das análises de IA**: o executor e o Especialista leem a lista e a
injetam no contexto quando houver.

## Decisões (do usuário, 2026-11-12)
- Menção `#xxx` → relatado **automático** (removível), não "sugestão a confirmar".
- Fontes automáticas: Especialista **+** plano/análise (`#`/`##`) **+** Timeline/comentários **+** descrição/sub-tarefas de itens.
- Contexto de IA: **resumo compacto + detalhe sob demanda** (injeta número/card/tipo/status/progresso/linha-resumo das
  sub-tarefas; o skill lê o detalhe com `prmake_work_item` só se precisar — a IA corta ~8000 tokens).
- **Default:** auto-captura só traz referência **cross-card** (item de outro card). `#n` do próprio card não vira relatado
  (já está na aba Itens). Inserção **manual** pode adicionar qualquer item.

## Modelo (schema `execution`)
Tabela nova `execution.WorkItemRelated`:
- `Id uuid PK`
- `CardNumber varchar(100)` — o card dono da aba Relatado
- `RelatedItemNumber bigint` — item relatado (número do item dono; normalmente de **outro** card)
- `RelatedTaskNumber bigint?` — se a menção foi a uma sub-tarefa (`#n` de sub-tarefa), para destacar
- `Source varchar(20)` — `specialist | plan | timeline | item-ref | manual`
- `Context varchar(500)?` — onde/como foi mencionado
- `CreatedBy uuid?`, `CreatedByName varchar(200)`, `CreatedAt timestamptz`
- Único `(CardNumber, RelatedItemNumber)`; índice `(CardNumber)`
- **manual nunca é rebaixado por auto**; auto não sobrescreve a `Source`/`Context` de um manual.

## API (`api/v1/WorkItems`)
- `GET  card/{card}/related` → `WorkItemRelatedResponse[]` (relação + `WorkItemResponse` resolvido, cross-card).
- `POST card/{card}/related` `{number}` → adiciona manual (resolve item ou sub-tarefa; 404 se não existe).
- `DELETE card/{card}/related/{number}` → remove a relação.
Reaproveita `ResolveAsync` (resolve `#n` item/sub-tarefa) e `SearchAsync` (`GET search?q=&exceptCard=`, já pronto, p/ a busca manual).

## Captura automática (idempotente, cross-card)
`CaptureMentionsAsync(card, text, source, context, actor)` extrai `#+(\d+)` do texto, resolve cada número ao item dono,
e faz upsert da relação quando o item for de **outro** card. Chamado de:
- mensagem do Especialista (`CaseMessage` ao gravar) — `source=specialist`;
- nota do plano / resultado de sub-tarefa / análise — `source=plan`;
- entrada da Timeline / comentário — `source=timeline`;
- descrição / sub-tarefa de item criado/editado — `source=item-ref`.
Falso-positivo (nº que também é PR etc.) é filtrado por só persistir o que resolve para um work item real; a relação é removível.

## Contexto de IA
`RelatedSummaryMarkdownAsync(card)` monta um bloco compacto. `prmake_case_context` e o carregamento do executor passam a
incluir "Relatados" quando houver. Skills `especialista-do-caso` e `executar-item-de-trabalho` leem a seção (detalhe via
`prmake_work_item`, que já devolve `otherCard: true`).

## Front (prmakerweb)
- `WorkItemListComponent`: input `readOnly` (ações atrás de `@if (!readOnly())`) + modo de dados `related` (consome
  `card/{card}/related`). Propaga `readOnly` para `work-item-dialog`, `work-task-dialog`, `work-item-execution`.
- Nova aba `reported` no `workbench` (union types + tablist desktop/mobile + pane), reaproveitando `app-work-item-list`
  em modo leitura, com busca p/ adicionar manual (número/título) e selo de origem por relatado.

## Fora de escopo
- Edição/execução de itens pela aba Relatado (é leitura).
- Notificações de "novo relatado".
