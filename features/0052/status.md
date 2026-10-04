# Status — Feature 0052

Branch `feature/0052` em `prform.api-0052` (API, executor, skills) e `prform-app-0052` (front, `node_modules` →
`prform-app-0019`). Executor sobe para **1.0.9** (autoatualiza).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | b19cb75 |
| B2 | ✅ concluída | Claude | c6bddc2 |
| B3 | ✅ concluída | Claude | ce1bab9 |
| E1 | ✅ concluída | Claude | ce1bab9 |
| S1 | ✅ concluída | Claude | d81e673 |
| S2 | ✅ concluída | Claude | 760adb1, c97595e |
| F1 | ✅ concluída | Claude | front f2fcc95 |
| F2 | ✅ concluída | Claude | front f2fcc95 |
| T1 | ✅ concluída | Claude | — (harness fora do git em `.t0052/`) |

## Decisões
- Documento publicado = seção `re-<tipo>` do projeto: espelho, busca, Pergunte e ficha da Base Solvace passam a ver a
  engenharia reversa sem duplicar. Seção `re-*` editada direto na Base reindexa os itens.
- ID do item é por módulo (`RN-012`), referência global `<módulo>#<ID>` — sem sigla nova por módulo.
- Andamento ao vivo (pedido do usuário no meio da feature): etapas, áreas, "agora" e registro na própria revisão
  (`POST revisions/{id}/progress`, evento `reverseRevision` nos grupos `reverse` e `reverse:<módulo>`); o `re.sh`
  reporta sozinho no inventário, checagem, rascunho e envio.
- A skill nova vem junto com a `analisar-bug` (dependência no `skill.json`) — o `update` não instala skill nova sozinho.
- A trava da investigação só vale para módulo **completo** (documentos exigidos publicados) e se desliga com
  `ReverseEngineeringGateStep` vazio.

## Log
- 2026-10-04 — spec, plano e status; worktrees criados a partir de `origin/master` (back 3b43c89, front 750287e).
- 2026-10-04 — B1: entidades `ReverseModule`, `ReverseRevision`, `ReverseAsset`, `ReverseIndexEntry`,
  `ReverseCardContext` (migração `ReverseEngineering`, schema `knowledge`); tipos de documento com modelos; parser de
  itens (ignora blocos de código; ```mermaid``` inline não abre bloco) e lint (seções obrigatórias, IDs, evidência,
  removidos, referências, segredo, cobertura).
- 2026-10-04 — B2: `ReverseEngineeringController` (módulos, sessão com pacote, revisões, aprovar/pedir ajustes/
  descartar/publicar com diff por item, anexos, índice/itens/impacto, `for-card`, `consulted`); seção `re-*` na Base
  reindexa; espelho com `reverse/INDEX.md` e `reverse/<módulo>.tsv`; índice da Base marca `RE n/6`.
- 2026-10-04 — B3/E1: MCP `prmake_base_search|get|module|impact`; trava no `prmake_advance` e no PATCH da skill
  (`message` no corpo); credencial do executor lê o índice; chaves no "Skills Configurations"; executor 1.0.9 conta
  `prmake_base_*`/`re.sh` como base.
- 2026-10-04 — S1/S2: skill `engenharia-reversa` (`re.sh`, `re_tool.py` com inventário .NET/ASP/Angular/SQL e
  cobertura), `analisar-bug` com o bloco de engenharia reversa no `contexto` e a regra "engenharia reversa primeiro",
  `kb.sh re find|get`.
- 2026-10-04 — andamento ao vivo (back, skill e tela).
- 2026-10-04 — F1/F2: tela Engenharia reversa (módulos, Como gerar, andamento ao vivo, revisão, publicado com sumário,
  UI/UX, fontes/apelidos, revisões e sessões, índice e impacto); link na Base Solvace.
- 2026-10-04 — T1 (`.t0052/`: Postgres 55452, API 5083, front 4200, tokens gestor/dev): `re.sh` no revamp-BOS real
  (inventário 437 itens back+front; bug do separador de fonte sem subpasta corrigido), check/save/submit, dev não
  publica (403) e gestor publica; índice, impacto, espelho (`reverse/*.tsv`), MCP (4 ferramentas), `for-card` com os
  itens e o texto, trava recusando `advance` sem citação (MCP e script) e liberando com `revamp-bos#RN-001`,
  `kb.sh re find|get`; tela no Chrome headless: andamento atualizado pelo tempo real, revisão com diff, publicar pela
  tela, sumário com âncora `?i=RN-002`, Como gerar, índice, revisões e mobile. 76 testes do módulo Knowledge.
