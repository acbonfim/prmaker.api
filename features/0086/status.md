# Status — Feature 0086

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Catálogo, `GET/PATCH settings`, histórico, validação | concluída | agente B1 | 84e57ca, 3fc060f, ed5c598, a98da54, af8e5ac | 130 chaves em 11 seções; migração `PluginSettings0086` (só a tabela de histórico; xmin sem coluna); padrão = reserva do código (JSON grande sem reserva → null); literal JS recusado no PATCH (a tela converte ao editar) |
| FA | Página por seção, editores simples, revisão/histórico, avançado | concluída | agente FA | front 170a300, 2b6e1bb, 96c1b6b, f2ffa14, 6afc7db, 8799c4b | 63 specs da pasta ok (Karma, tsconfig temporário); `?exemplo=1` só em dev (fixture); seção `avancado` montada no front; 409/400 viram resultado no serviço |
| FB | Editores estruturados (table/map/json/prompt) | concluída | agente FB | front a8bea8d | 29 specs (Karma, tsconfig temporário — `ng test` geral não compila por specs antigos); `SettingColumn.allowCustom` acrescentado; JSON vazio = padrão do código |
| Q1 | Build + e2e local + ajustes | concluída | Claude | 9d031b8, merge da master (c07a179 / front bd5624b) | rótulo do catálogo vale na tela (FieldSettings fica p/ Minhas integrações); 283 testes back, 63 specs front, build prod ok; e2e `.t0086` |

## Log
- 2026-11-05 — inventário dos 13 plugins (~130 chaves), spec e plano; decisões: por assunto, catálogo no código,
  escopo completo.
- 2026-11-05 — onda 1 disparada em paralelo: B1 (back), FA (página + editores simples), FB (editores estruturados).
- 2026-11-05 — Q1 (harness `.t0086`: Postgres 18 isolado 55486, auth falsa 8098, API 5086, front dev servido em
  localhost:4201 — a 4200/5083 estavam com o harness da 0085; Chrome headless `e2e/q1.mjs`). Testado: 12 seções na
  lateral, busca ("sufixo" → BranchSuffixByTarget), bool salvo com revisão antes→depois, `ActiveBranchs` em literal JS
  aberto como tabela (4 linhas) e gravado como JSON ao incluir linha, conflito real (409 → diálogo → sobrescrever),
  histórico com as trocas, área avançada (plugins faltando + chaves fora do catálogo), celular sem rolagem horizontal,
  nenhum erro novo no console (o NG0303 do TopMenu já existia). Decisão no Q1: na tela vale o rótulo do catálogo — os
  rótulos semeados em FieldSettings eram técnicos e continuam em Minhas integrações. Master (0085 r3) mesclada sem
  conflito; sem chave nova. Merge autorizado pelo usuário para esta feature.
- Pendências fora do escopo: semear em banco novo os plugins criados à mão (AI, DevOps, GitHub, Teams, provedores, 9 e
  11); editores estruturados de WorkItemTaskKinds/WorkItemTemplates (hoje JSON com ajuda); unificar o diff do FA e do FB.
- 2026-11-05 — mesclados back #159 e front #90 (deploys ok). O #158 (0085 r4a) entrou na master antes e criou `WorkItemTypes` (o teste de cobertura do catálogo passou a falhar na master) → catalogada como tabela em Itens de trabalho (PR de correção).
