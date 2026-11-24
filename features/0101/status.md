# Status 0101

- Branch: `feature/0101` (prform.api-0101 e prform-app-0101), a partir de origin/master (0098 mesclada).
- Merge: não autorizado.

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — entidade, migração, aplicação | ✅ | principal | a23e3f6 — `GovernanceStandards` + rascunhos, na fotografia em memória da governança |
| B2 — parser da wiki | ✅ | principal | a23e3f6 — 104 regras (28 NEW → propostas, PK3 CHANGED); exemplos certo/errado; SQL de validação por página |
| B3 — conferidor | ✅ | principal | a23e3f6 + N12 — 22 conferências embutidas (SQL Server + EF); `dbo` fora; exemplos de DDL da própria wiki passam limpos |
| B4 — importação wiki (DevOps) e ER | ✅ | principal | 8ef6edb — `IAzureService.GetWikiPagesAsync`; ER: ADR, TEC e `Onde:` do índice técnico (END/ROT/TAB/DBO) |
| B5 — pontos de aplicação (check, travas, for-card, MCP) | ✅ | principal | 8ef6edb — `standards/check`, `for-card`, MCP `prmake_standards(_check)`, `prmake_rules`; trava de .sql/migração no `prmake_file`/`prmake_work_item_file` |
| B6 — carga inicial em produção | ⏳ | | precisa de ok do usuário |
| S1 — skills leem/conferem padrões | ⏳ | agente skill | |
| S2 — hook confere .sql/migrações | ⏳ | agente skill | |
| F1 — aba Padrões | ⏳ | agente front | |
| F2 — importar wiki/ER com rascunho | ⏳ | agente front | |
| F3 — aderência | ⏳ | agente front | |
| Q1 — testes | ⏳ | | |
| Q2 — e2e + A/B da Bancada | ⏳ | | |

## Log

- 2026-11-24 — e2e local (`.t0101`, banco descartável com o plugin do DevOps): importação REAL da wiki pelo endpoint (10 páginas, 104 itens, 28 propostos, reimportar = 0 mudanças); conferência; trava pelo MCP em enforce (R5 block → arquivo não gravado; FK3 monitor → gravado com aviso; consulta do legado limpa); trilha `standard` com card e IDs.

- 2026-11-24 — spec/plano; cópia da wiki (página 236 + 9 subpáginas, v2.0) em `features/0101/wiki/` com ok do usuário.
