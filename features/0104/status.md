# Status 0104

- Branch: `feature/0104` (prform.api-0104 e prform-app-0104), a partir de origin/master.

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — domínio + migração | ✅ | principal | `Epic.AllowCardStoryDrafts` (sem default no modelo; migração `CardStoryDrafts0104` com `defaultValue: true` p/ os existentes), `EpicStory.SourceCard`, request (`bool?`) / responses |
| B2 — regras MCP + contexto | ✅ | principal | `CardStoryAccess` (ler: card no épico; escrever: created/linked + flag, só draft, só os próprios); conversão exige admin/management de quem pediu (`DeskProfileService.RequesterAsync`); `prmake_epics(card)`, `prmake_epic_save(allowCardStoryDrafts)`, contexto do épico |
| S1 — skill | ✅ | principal | especialista-do-caso (linha da tabela de ações) e especialista-de-gestao (flag no `prmake_epic_save`) |
| F1 — front | ✅ | principal | interruptor no diálogo do épico (ligado por padrão), "pelo card N" na aba Histórias e no diálogo da história |
| Q1 — testes | ✅ | principal | executionplans 462 (3 novos) · prform 672 (novos em `CardStoryAccess0104Tests`) · specs de épicos 36 · ng build ok. Sem e2e local |

## Log

- 2026-11-27 — feature aberta a partir da conversa do card 79047.
- 2026-11-27 — B1/B2/S1/F1/Q1 concluídas.
