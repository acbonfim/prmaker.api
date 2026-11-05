# Status — Feature 0086

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Catálogo, `GET/PATCH settings`, histórico, validação | em andamento | agente B1 | | |
| FA | Página por seção, editores simples, revisão/histórico, avançado | em andamento | agente FA | | |
| FB | Editores estruturados (table/map/json/prompt) | concluída | agente FB | front a8bea8d | 29 specs (Karma, tsconfig temporário — `ng test` geral não compila por specs antigos); `SettingColumn.allowCustom` acrescentado; JSON vazio = padrão do código |
| Q1 | Build + e2e local + ajustes | pendente | Claude | | |

## Log
- 2026-11-05 — inventário dos 13 plugins (~130 chaves), spec e plano; decisões: por assunto, catálogo no código,
  escopo completo.
- 2026-11-05 — onda 1 disparada em paralelo: B1 (back), FA (página + editores simples), FB (editores estruturados).
