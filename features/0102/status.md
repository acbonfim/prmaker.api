# Status 0102

- Branch: `feature/0102` (prform.api-0102 e prform-app-0102), a partir de origin/master.
- Merge: autorizado pelo usuário ("ao finalizar, pode abrir pr e fazer merge automaticamente", 2026-11-25).

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — entidade, kind, migração | ✅ | principal | 97041b0 — `execution.BaseQuestions`, kind `base-ask`, pista `base` |
| B2 — fila (claim, prompt, modelo/effort, arquivos) | ✅ | principal | 97041b0 — gate 1.9.0, 2 vagas por máquina, prioridade junto com o especialista |
| B3 — token, MCP answer, endpoints | ✅ | principal | 97041b0 + correção do e2e (seções nos refs, modelo no token) |
| B4 — flag + opções em Minhas integrações | ✅ | principal | 97041b0 — `BaseSolvaceRunner` semeada (campo pessoal), `options` do catálogo |
| E1 — executor 1.9.0 | ✅ | principal | 97041b0 — pista base, `--effort`, MCP estrito, pasta neutra, `PRMAKE_FILES_DIR` |
| F1 — campo com opções | ✅ | principal | front 25164e4 — mat-select com rótulos (vale também para Provider/Effort do catálogo) |
| F2 — Pergunte pelo executor | ✅ | principal | front 25164e4 — andamento ao vivo, cancelar, sem executor → IA do servidor desta vez, a fundo pelo executor |
| Q1 — testes | ✅ | | prform 652 · executionplans 416 · knowledge 244 · executor 61 · specs kb-ask-* 10 · ng build ok |
| Q2 — e2e | ✅ | principal | `.t0102`: API local + Claude Code real (sonnet, effort low) — 15 s, só as 5 ferramentas da Base, resposta gravada; telas conferidas no Chrome |

## Log

- 2026-11-25 — e2e local (`.t0102`, banco `cime0102` copiado do 0098): flag em Minhas integrações → `ask/status` runner executor;
  claim com `sonnet`/`low` e token restrito (tools/list = 5 ferramentas; `prmake_plan` e REST → 403; depois do finish → 401);
  Claude Code real respondeu em 15 s (11 s de modelo, US$ 0,13 na assinatura) com 2 buscas + 1 leitura; 409 `NO_EXECUTOR`
  para quem não tem máquina. Ajustes que o e2e mostrou: o Claude cita seções (`projeto/secao § título`) — viram resultados e a
  "Leia a seção"; o modelo vai no token e aparece no selo ("Claude Code · sonnet").
- 2026-11-25 — spec/plano; backend, executor e front.

## Pendente

- Executor 1.9.0 se autoatualiza nas máquinas depois do deploy (até lá a pergunta espera com "precisa da versão 1.9.0").
- Primeira pergunta real em produção pelo executor de alguém.
