# Status — Feature 0046

Branch `feature/0046` em `prform.api-0046` (skills, executor, MCP). Sem front.

| Fase | Status |
|---|---|
| S1–S3 | ✅ concluída |
| B1 | ✅ concluída |
| E1 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-02 — diagnóstico no transcript da sessão do card 75091: `Write`/`Bash` em `~/.claude/cards/75091` negados
  ("don't ask mode"). Teste com `claude -p --permission-mode dontAsk`: `~/.claude/cards` negado com e sem
  `Write(~/.claude/cards/**)`; `~/.prmake/cards` gravado (com `--add-dir`, e só com o settings do instalador:
  `additionalDirectories` + `Edit(~/.prmake/cards/**)`).
- 2026-10-02 — migração testada (card só na pasta antiga → movido; nas duas → copia o que falta sem sobrescrever; novo
  → pasta nova). MCP na API local (Postgres 55446): `prmake_file` grava `01_mover_usuario.sql` (script) e
  `chamado.md` (analysis) na etapa `chamado-x`, com tipo de conteúdo de texto.
