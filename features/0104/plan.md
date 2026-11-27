# Plano 0104 — Rascunhos de histórias pelos cards do épico

Branches `feature/0104` em `prform.api-0104` (back, skills) e `prform-app-0104` (front), a partir da origin/master.

## Decisões

- **Flag no épico** `AllowCardStoryDrafts` (coluna `bool` default `true` — épicos existentes já nascem permitindo). Vai no
  `SaveEpicRequest` como `bool?` (criar: null = true; editar: null = não mexe) e nas respostas (`allowCardStoryDrafts`).
- **Quem é "card do épico"**: o card do caso precisa estar no épico como `created` ou `linked`. `mentioned` (só citou
  `#<épico>`) não dá escrita.
- **Especialista do caso de um card** (token do caso com card numérico), pelo MCP:
  - `prmake_epic_stories` (ler): basta o card estar no épico (qualquer papel) — serve para checar duplicidade.
  - `prmake_epic_story_save`: card `created`/`linked` + flag ligada. Criar → sempre `draft` (status `ready`/`discarded`
    recusado com a explicação). Editar → só rascunho (`draft`) criado pelo próprio card, e continua `draft`.
  - A história guarda o card de origem (`SourceCard`, coluna nova) — a Mesa mostra "pelo card 79047" e o contexto do
    épico também.
  - `prmake_epic_story_convert`: recusado para qualquer especialista cujo pedido venha de alguém **sem** `admin`/
    `management` (papéis de quem pediu, `DeskProfileService.RequesterAsync`). Com o papel, o especialista do card pode
    converter rascunho do épico do card (a pessoa tem acesso à Gestão). REST já exigia o papel (`EnsureDeskWriter`).
- **Contexto**: `prmake_epics`/`prmake_epic` mostram a flag; `ContextMarkdown` do épico cita a flag e a origem das
  histórias. Skill `especialista-do-caso` aprende o caminho (épicos do card → rascunho → conversão só pela Gestão).
- **Front**: diálogo do épico ganha o interruptor "Permitir rascunhos de histórias pelos cards" (ligado por padrão,
  editável sempre); aba Histórias mostra a etiqueta "pelo card N" nas histórias de origem de card.

## Fases

| Fase | O quê | Dep. |
|---|---|---|
| B1 | Domínio + migração `CardStoryDrafts0104` (`Epics.AllowCardStoryDrafts`, `EpicStories.SourceCard`), request/response | — |
| B2 | Regras no MCP (`EpicStoryMcpTools`): leitura/escrita pelo card, só rascunho, conversão por papel; contexto | B1 |
| S1 | Skill `especialista-do-caso` (+ descrição das ferramentas) | B2 |
| F1 | Diálogo do épico (interruptor) + etiqueta de origem na aba Histórias | B1 |
| Q1 | Testes (xUnit + specs) e build | B2, F1 |
