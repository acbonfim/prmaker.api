# 0041 — MCP primeiro: o ciclo inteiro do plano pelo MCP do PRMake, limite da conta no executor e medição

## Contexto
A 0039 publicou o MCP remoto (`/mcp`, 13 ferramentas), mas numa análise real (card 75022) o Claude carregou as
ferramentas MCP e seguiu pelo Bash: os comandos mais usados da skill — `advance` (conclui uma etapa e inicia a
próxima) e `block` (trava a etapa esperando o usuário) — não existem no MCP, e a tabela "dia a dia" da SKILL.md só
mostra o script. Na mesma investigação:
- a ferramenta MCP `prmake_devops` chama o serviço direto e **não registra na Timeline** (o endpoint REST registra);
- o executor tratou "You've hit your session limit · resets 7:50pm" como falha comum e gastou as 3 tentativas em
  minutos;
- não há como saber se o MCP economiza tokens: o custo é registrado, mas não o canal usado.

## Objetivo
1. **Toda a parte do plano pelo MCP**: o Claude não precisa do Bash para conduzir o plano (etapas, bloqueios,
   perguntas, respostas do chat, links, plano de correção, configuração, card do DevOps).
2. **Mesmo efeito pelos dois caminhos**: MCP e REST usam a mesma lógica (inclusive a Timeline das ações do DevOps).
3. **SKILL.md MCP primeiro**, com o script como reserva (sem MCP, Windows sem `claude mcp`, fila offline).
4. **Limite da conta do Claude** reconhecido pelo executor: o pedido espera o horário de reset (sem gastar tentativa)
   e a máquina para de pegar pedidos até lá; a tela mostra "Limite da conta do Claude até HH:MM".
5. **Medição**: cada sessão registra quantas chamadas foram pelo MCP e quantas pelo script; relatório de consumo por
   análise (com MCP × sem MCP) em "Meus executores".
6. **Skills atualizadas para todo mundo** pela atualização automática que já existe (hook de início de sessão), sem
   ação manual; o MCP é registrado na mesma atualização para quem ainda não tem.

## Escopo
### Ferramentas MCP novas
| Ferramenta | Equivale a |
|---|---|
| `prmake_advance(card, from, to?, message?, kind?)` | `prmake-plan.sh advance` |
| `prmake_block(card, key, text)` / `prmake_unblock(card, key)` | `block` / `unblock` |
| `prmake_checkpoint(card, key, text)` | `checkpoint` |
| `prmake_answer(card, question, text)` | `answer` (resposta dada no chat, número ou id) |
| `prmake_link(card, key, url, title?, kind?, blocks?)` | `link` |
| `prmake_correction(card, title, steps)` | `correction` (herda a sessão do Claude do plano de análise) |
| `prmake_config()` | `settings` (Skills Configurations, prompts, campos do DevOps) |
| `prmake_devops_config()` | `devops <card> config` + `classifications` |
| `prmake_card(card)` | campos principais do card do Azure DevOps, comentários e alertas, compactos |
| `prmake_devops(..., action=classify)` | `devops <card> classify` |

`prmake_plan` passa a trazer o que o `resume-info` mostra (checkpoint, atividade e links de cada etapa).

### Continuam no script (e por quê)
`contexto` (grava arquivos locais e sincroniza a Base Solvace), `sync`/`upload` (arquivos do disco), `branches`/
`worktree`/`open-pr`/`pr-text`/`save-pr-text` (git local e textos longos já em arquivo), `status` ao concluir o plano
(envia o custo da sessão), consultas a banco/Cognito (credenciais e VPN locais), gerar-prmake/gerar-handover
(publicação de textos longos em arquivo) e a fila offline.

### Executor (1.0.2)
- Reconhece limite de uso da conta ("session limit", "usage limit", "rate limit" com "resets <hora>") no resultado
  ou no stderr; calcula o horário de reset (com o fuso informado) e avisa o PRMake (`retryAt`).
- PRMake: o pedido volta para a fila com `NotBefore = retryAt` **sem contar tentativa**; a máquina fica
  `throttledUntil` e não pega pedidos até lá; motivo de espera `throttled`.

### Medição
- `send_usage` (skill) conta no transcript as chamadas `mcp__prmake__*` e as do `prmake-plan.sh` e envia junto com
  o custo; a sessão do plano guarda os dois números.
- `GET ExecutionPlan/usage-report?days=30`: planos do usuário (admin: de todos) com custo registrado, agrupados em
  "com MCP" (≥ metade das chamadas do plano pelo MCP) e "sem MCP": quantidade, média de turnos, de tokens de entrada
  (inclui cache) e de saída por plano, por fase.
- "Meus executores" mostra esse comparativo.

## Critérios de aceite
1. Uma análise inteira pode ser conduzida pelo MCP (etapas, bloqueio, perguntas, resposta do chat, link, plano de
   correção, configuração, card) — só `contexto`, arquivos, git e o `status` final pelo script.
2. Ação do DevOps pelo MCP registra na Timeline igual ao endpoint.
3. Plano de correção criado pelo MCP pode ser retomado pelo executor (sessão herdada) e `prmake-plan.sh use <card>
   correction` encontra o plano mesmo sem estado local.
4. "Session limit · resets 7:50pm" → pedido na fila até 19:50 com a mesma tentativa, máquina sem pegar pedidos até lá,
   tela com o motivo.
5. Relatório mostra os dois grupos com médias.
6. Skills e MCP chegam a todos pela atualização automática.
