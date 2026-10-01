# Status — Feature 0039

| Fase | Descrição | Status |
|---|---|---|
| B1 | Entidades, contexto, migração `AddExecutionQueue` | ✅ concluída |
| B2 | Aplicação da fila, executores, configurações | ✅ concluída |
| B3 | Controllers, credencial do executor, gatilhos de resume | ✅ concluída |
| B4 | WIQL + regras automáticas | ✅ concluída (sem DevOps no teste local: erro gravado em `autoLastError`) |
| B5 | MCP remoto `/mcp` | ✅ concluída |
| A1 | Executor .NET + publicação na imagem | ✅ concluída |
| S1 | Skills (modo executor, worktree, MCP, agent install) | ✅ concluída |
| F1 | Front: botão + faixa do pedido | ✅ concluída |
| F2 | Front: Meus executores | ✅ concluída |
| Q1 | Teste ponta a ponta | ✅ concluído |
| D1 | PRs, merge e deploy | 🚀 PRs abertos |

## Q1 — teste local (Postgres 18 isolado, auth falso, executor real com `claude` falso, Chrome headless)
- Pedido pela tela sem executor: `queued`, motivo "Nenhuma máquina com o executor"; segundo clique devolve o mesmo pedido.
- Executor registrado com a api-key do usuário; a credencial dele dá 403 na Timeline, 200 em Skills/ExecutionWorker.
- Pedido → claim em segundos (long-poll) → `claude -p --session-id` com `PRMAKE_EXECUTOR=1` → plano e pergunta criados →
  fim `done` com custo (US$ 0,42, 7 turnos) lido do stream-json.
- Resposta pela tela → pedido `resume` (origem respostas) para a mesma máquina → `claude --resume <mesma sessão>`.
- Falha (exit 1): volta para a fila, tentativa 2/3 com espera de 1 min; token no stderr aparece como `[oculto]`.
- Cancelar pela tela com o Claude rodando: processo encerrado no heartbeat seguinte (exit 137), pedido `cancelled`.
- Executor morto (kill -9) durante a execução: trava vence em 2 min → fila, tentativa 3/3 com espera de 5 min; motivo
  "A máquina … está offline".
- Comentário do usuário no plano → pedido `resume` (origem comentário).
- Orçamento abaixo do gasto: pedido fica na fila com "Orçamento do dia atingido"; "Rodar mesmo assim" libera.
  **Bug achado e corrigido**: início do dia com offset -03:00 (Npgsql só grava timestamptz em UTC).
- Revogar: o executor para (403/401) e o que estava com ele volta para a fila; registrar de novo reativa.
- Pausar: motivo "O executor de … está pausado"; retomar → o pedido roda.
- `resume-candidates` (vigia antigo) vazio para quem tem executor.
- Login inválido (`/login`): falha sem nova tentativa, faixa vermelha com o motivo e "Tentar de novo".
- MCP: `initialize`, 13 ferramentas, `prmake_plan`, `prmake_steps`/`prmake_ask` (listas), `prmake_log`,
  `prmake_control`, `prmake_attachment` devolve a imagem como bloco de imagem, `prmake_devops ready-for-qa` barrado
  sem `validar-qa`, sem x-api-key → 401. **Ajuste**: erros de regra chegavam como "An error occurred" — agora vão com
  a mensagem (ex.: "Etapa não encontrada: 'corrigir'").
- Guard (hook PreToolUse): bloqueia `gh pr merge`, `gh api …/merge`, push forçado, push em develop/master
  (`HEAD:master` inclusive), `DELETE` via sql-query, escrita no Cognito e `rm -rf ~`; libera push da branch de correção,
  SELECT, `admin-get-user`, checkout.
- Tela: faixa "Na fila do Claude · O executor de … está pausado" com Meus executores/Cancelar; faixa de falha; botão
  "Analisar com Claude" no estado vazio e no cabeçalho; diálogo Meus executores (máquina, versões, doctor, orçamento,
  regra com o último erro). Ajuste: ícone e texto da faixa na mesma linha no painel estreito.
- Executor publicado para osx-arm64/osx-x64/win-x64/linux-x64 (12–14 MB, trimmed, sem avisos de trimming).

## Log
- 2026-10-01: spec, plano e branches `feature/0039` (back e front) criados.
- 2026-10-01: B1–B5, A1, S1, F1–F2 implementadas; Q1 executado (2 correções).
