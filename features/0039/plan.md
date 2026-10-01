# Plano — 0039

## Decisões (as "em aberto" da spec, decididas na execução)
1. **Executor em .NET 10** (`tools/Cime.ExecutionAgent`), single-file self-contained por RID (osx-arm64, osx-x64,
   win-x64, linux-x64), compilado na imagem da API e servido por `GET /api/v1/ExecutionWorker/agent/{rid}`. No macOS
   o instalador faz `codesign -s -` (assinatura ad-hoc) para garantir que rode.
2. **Gatilho automático pela leitura do PRMake (WIQL)**, avaliado de forma preguiçosa no long-poll do executor do
   dono (no máximo a cada 5 min) — sem service hook no DevOps nem serviço em segundo plano no Cloud Run (que só tem CPU
   durante o request). Só existe regra se o usuário ligar.
3. **Orçamento diário por usuário, definido pelo próprio usuário** (vazio = sem limite). Estourou: o pedido fica na
   fila com o motivo; o dono libera com "rodar mesmo assim" (pedido com `force`). O executor também passa o saldo
   para o `claude --max-budget-usd`.
4. **Worktrees por card guardadas 7 dias** depois que o plano termina (o executor limpa).

Desvios da spec (com motivo):
- **Pareamento sem código**: o executor se registra com a api-key que a instalação das skills já guarda
  (`~/.claude/prmake-token.txt`) e recebe a credencial própria do executor. Quem tem a api-key já pode tudo, então o
  código não acrescenta segurança e abriria endpoints anônimos.
- **Tipo `correction` não existe**: `resume` cobre (a skill retoma a correção pelo `contexto`/`resume-info`).
- **Long-poll faz o polling do banco a cada 2 s dentro do request** (acordado na hora por sinal em memória quando o
  pedido nasce na mesma instância).

## Desenho
### Backend (módulo `Solvace.ExecutionPlans`, schema `execution`)
- `ExecutionRequest` (pedido): card, tipo (`analyze`/`resume`), origem (`button`/`answers`/`resume`/`rule`/`api`),
  status (`queued`/`claimed`/`running`/`done`/`failed`/`cancelled`/`expired`), dono, quem pediu, executor alvo e
  executor que pegou, tentativas/máximo, `NotBefore` (backoff 1/5/15 min), `LeaseUntil`, pid, sessão, observação,
  `Force`, último erro, stderr final, custo/tokens, motivo de espera (`WaitReason`). Índice único parcial: um pedido
  ativo por card.
- `ExecutionWorker` (executor): dono, nome, host, SO, versões (executor/Claude/skills), concorrência, status
  (`active`/`paused`/`revoked`), último sinal, capacidades e `doctor` (jsonb), id da credencial.
- `ExecutionUserSettings`: orçamento diário, regra automática (ligada, tipos, estados, áreas, "atribuído a",
  máximo por dia), última avaliação.
- Credencial do executor: JWT x-api-key assinado com `Auth:Secret` (mesmas claims do dono + `executorId` + `jti`).
  Middleware no host: token com `executorId` só alcança `ExecutionQueue`, `ExecutionWorker`, `ExecutionPlan` e
  `Skills`, e é recusado (401) se o executor foi revogado ou a credencial trocada.
- Gatilhos de `resume` dentro do plano: após qualquer alteração que não veio do executor (resposta, etapa do usuário,
  chamado, PR mesclado, "Continuar", comentário), se o plano está ativo, o dono tem executor, ninguém está rodando
  (sem pedido ativo e sem sinal da skill depois do último pedido) e há trabalho para o Claude.
- `resume-candidates` (vigia antigo) ignora planos de quem já tem executor.
- MCP remoto em `/mcp` (`ModelContextProtocol.AspNetCore`, Streamable HTTP stateless), autenticado pelo mesmo
  `X-API-Key`, ferramentas sobre `IExecutionPlanApplication`/`IExecutionQueueApplication` e serviços do host.

### Executor (`tools/Cime.ExecutionAgent`, comando `prmake-agent`)
`register` · `run` · `install`/`uninstall` (LaunchAgent, Agendador de Tarefas, systemd --user) · `status` ·
`doctor` · `update`. Long-poll → workspace → `claude -p` (stream-json, `--session-id`/`--resume`,
`--permission-mode dontAsk`, settings do executor com allow/deny + hook `PreToolUse`) → heartbeat 30 s (cancelar,
pausa > 10 min, timeout) → fim com custo. Concorrência local, backoff de rede, log rotativo, notificação do SO.

### Skills
- `PRMAKE_EXECUTOR=1` no ambiente do processo headless: `watch`/`wait`/`wait-answers` saem na hora com
  `MODO EXECUTOR` (exit 12) — a skill encerra a vez e o PRMake retoma sozinho.
- Novo `worktree` no `prmake-plan.sh` e `branches` imprime os comandos com worktree no modo executor.
- `prmake-card.sh`: `agent` vira aviso; abrir no terminal checa pedido rodando ("esperar" ou "assumir").
- Instalador: baixa/atualiza o executor (opcional), registra o MCP (`claude mcp add --transport http`).
- SKILL.md/referências: preferir `mcp__prmake__*` quando houver; modo executor.

### Frontend
- Botão "Analisar com Claude" (card sem plano) / "Continuar com Claude" (plano aberto), diálogo com máquina e
  observação.
- Faixa do pedido no plano (na fila / máquina offline / rodando / falhou + tentar de novo / orçamento).
- Tela "Meus executores": máquinas, status, versões, doctor, pausar/retomar, revogar, histórico, orçamento e regra
  automática, comandos de instalação.

## Fases
| Fase | O quê | Depende |
|---|---|---|
| B1 | Entidades, contexto, migração (`AddExecutionQueue`) | — |
| B2 | Aplicação da fila + executores + configurações (claim/lease/backoff/expiração/orçamento/regra) | B1 |
| B3 | Controllers `ExecutionQueue`/`ExecutionWorker`, credencial do executor + middleware, gatilhos de `resume` no plano, `resume-candidates` | B2 |
| B4 | WIQL no módulo Azure + avaliação das regras | B2 |
| B5 | MCP remoto `/mcp` | B3 |
| A1 | Executor .NET (`tools/Cime.ExecutionAgent`) + publicação na imagem | B3 |
| S1 | Skills: modo executor, worktree, prmake-card, instalador (executor + MCP), SKILL.md | A1, B5 |
| F1 | Front: botão + diálogo + faixa do pedido no plano | B3 |
| F2 | Front: "Meus executores" (+ orçamento e regra) | B3 |
| Q1 | Teste local ponta a ponta (Postgres isolado, `claude` falso, executor real, MCP com cliente real) | tudo |
| D1 | PRs back e front, merge dos dois juntos, acompanhar deploy | Q1 |
