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
| S1 — skills leem/conferem padrões | ✅ | agente skill | f61d964 — seção Padrões (0101) nas skills; `prmake-plan.sh standards/standards-check`; upload/sync de .sql conferem |
| S2 — hook confere .sql/migrações | ✅ | agente skill | ebad2c0 — Post de Edit/Write em .sql/migrações → achados ao Claude; Pre de Write .sql com block → deny; testado contra a API real |
| F1 — aba Padrões | ✅ | agente front | bc534e1 + 6bc644f — catálogo, detalhe, edição, lote, testar conferência |
| F2 — importar wiki/ER com rascunho | ✅ | agente front | 90d09fd — rascunho com diff por item; backend devolve added/changed{before,after}/removed(keys) |
| F3 — aderência | ✅ | agente front | 3216006 — por padrão/repositório (`repo:` no detalhe)/card, atalho da Auditoria |
| Q1 — testes | ✅ | | governance 77 · prform 512 · SkillTests 119 · specs da governança 68 · ng build ok |
| Q2 — e2e + A/B da Bancada | ✅ | principal | importação real da wiki; trava MCP e hook contra a API real; aba Padrões com dados reais; A/B Bancada master × 0101 (enforce, R5 block): status idênticos |

## Log

- 2026-11-24 — e2e local (`.t0101`, banco descartável com o plugin do DevOps): importação REAL da wiki pelo endpoint (10 páginas, 104 itens, 28 propostos, reimportar = 0 mudanças); conferência; trava pelo MCP em enforce (R5 block → arquivo não gravado; FK3 monitor → gravado com aviso; consulta do legado limpa); trilha `standard` com card e IDs.

- 2026-11-24 — spec/plano; cópia da wiki (página 236 + 9 subpáginas, v2.0) em `features/0101/wiki/` com ok do usuário.

## Pendente

- B6: carga em produção (importar a wiki 236 e a ER publicada) — precisa de ok do usuário; tudo entra em Monitorar.
- Itens (NEW) da wiki ficam propostos até o DBA validar (a reimportação muda o estado quando o (NEW) sair da wiki).
- Consulta SQL de validação da wiki (46 itens) fica no catálogo para rodar sob demanda; ainda não há botão que rode no banco de um ambiente.
