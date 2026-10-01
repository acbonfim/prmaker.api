# 0039 — Analisar pelo PRMake sem abrir o Claude Code + MCP remoto do plano

## Objetivo

Hoje, para começar uma análise, alguém precisa abrir o Claude Code na pasta certa e digitar `/analisar-bug <card>`.
O vigia da 0033 (`prmake-card.sh agent`) só **retoma** sessões que já existem nesta máquina. Ele não começa nada,
faz polling a cada 60 s, não sabe se o processo do Claude terminou bem ou mal, não tem fila, trava nem tentativa,
e o usuário não vê na tela se a máquina dele está ligada.

Esta feature tem duas partes:

1. **Fila de execução + executor local robusto**: qualquer pessoa pede "Analisar com Claude" pela tela do card,
   e o executor da máquina dela pega o pedido, roda a skill sem terminal aberto, acompanha o processo e mostra na
   tela tudo o que está acontecendo (na fila, rodando, falhou, máquina offline). Ninguém digita `/analisar-bug`.
2. **Servidor MCP remoto do PRMake**: as operações do plano de execução (e o que a skill hoje faz por
   `prmake-plan.sh`) viram ferramentas MCP servidas pela própria API. Isso economiza tokens, acaba com os
   problemas de bash/jq no Windows (0035) e deixa o plano acessível a qualquer cliente MCP (Claude Code, Agent
   SDK, um executor central no futuro).

Regras que continuam valendo: **o Claude só abre PRs, nunca faz merge**; **toda gravação passa pelo PRMake**;
**configuração vem do PRMake**; a skill nunca para só porque falta alguém (0024).

---

## Parte 1 — Fila de execução e executor local

### 1.1 Pedido de execução (fila no PRMake)

Nova entidade no módulo `Solvace.ExecutionPlans` (schema `execution`): **pedido de execução** (`ExecutionRequest`).

- **Tipos**:
  - `analyze`: começar a análise de um card.
  - `resume`: continuar uma sessão, por pedido de "Continuar" na tela ou porque chegaram respostas pela tela.
  - `correction`: seguir para a correção depois das respostas.
  - Os gatilhos de retomada da 0033 (`resume-request` e respostas `AnsweredVia = prmake`) passam a **gerar
    pedidos** nesta fila.
- **Campos**:
  - card, tipo, plano (quando já existe), sessão do Claude (para `resume`);
  - quem pediu, quando, origem (`button`, `answers`, `rule`, `api`);
  - executor alvo: "meu executor", um executor específico ou qualquer executor meu online;
  - status; tentativas e máximo; último erro; instantes de cada transição.
- **Status**:
  - `queued` → `claimed` → `running` → `done`, `failed`, `cancelled` ou `expired`;
  - `running` → `queued` também acontece quando o executor cai: a trava vence e o pedido volta para a fila.
- **Idempotência**: no máximo **um pedido ativo por card** (`queued`/`claimed`/`running`). Um novo pedido para o
  mesmo card devolve o ativo em vez de duplicar. Dois cliques em "Analisar" não abrem duas sessões.
- **Trava (lease)**:
  - O executor pega o pedido de forma atômica (`FOR UPDATE SKIP LOCKED` ou versão otimista) e recebe
    `leaseUntil` (ex.: 2 min).
  - Ele renova a trava com heartbeat a cada 30 s enquanto o processo do Claude vive.
  - Trava vencida → o pedido volta para `queued` (tentativa + 1). Depois do máximo (padrão 3), vai para `failed`
    com o motivo, visível no card.
- **Backoff entre tentativas**: 1 min, 5 min, 15 min. Uma tentativa nova de `resume` usa a **mesma sessão**
  (`--resume`). A de `analyze` retoma o plano aberto (o `contexto` já faz isso).
- **Expiração**: um pedido `queued` sem executor por mais de N horas (configurável, padrão 24 h) vira `expired`
  e avisa no card. Ele não fica na fila para sempre.

### 1.2 Executores (máquinas registradas)

Nova entidade **executor** (`ExecutionWorker`), uma por máquina por usuário.

- **Campos**:
  - id, usuário dono, host, SO;
  - versão do executor, do Claude Code e das skills;
  - concorrência máxima (padrão 1, até 3);
  - status (`online`, `offline`, `paused`), último heartbeat;
  - capacidades: repositórios mapeados (repo → pasta local) e o que a máquina alcança (aliases de banco, VPN).
    As capacidades vêm do próprio executor ao subir e de um teste periódico.
- **Pareamento** ("Conectar esta máquina"):
  - O instalador mostra um código.
  - O usuário confirma o código na tela do PRMake, e o executor recebe uma **credencial própria** (x-api-key de
    serviço no Cime.Auth).
  - Essa credencial só alcança os endpoints de execução, plano, Timeline e skills, e pode ser revogada na tela.
  - Ela acaba com o "token expirou" do `~/.claude/prmake-token.txt` no meio da madrugada.
- **Tela "Meus executores"** (perfil do usuário):
  - lista de máquinas, online/offline, versões, o que está rodando agora e o histórico;
  - botões pausar/retomar executor e revogar;
  - aviso de versão desatualizada.
- **Segurança**: um executor só recebe pedidos do **próprio dono**. Executor compartilhado de time fica fora do
  escopo; ver "Fora do escopo".

### 1.3 Entrega dos pedidos: long-poll em vez de polling de 60 s

- `GET /api/v1/ExecutionQueue/next?wait=25` (long-poll): responde assim que existe pedido para aquele executor
  ou em até 25 s com `204`. O executor repete. Na prática, o pedido começa em segundos sem precisar de SignalR
  no executor. Funciona no Cloud Run (requisição curta, sem conexão presa) e atrás de proxy corporativo.
- Heartbeat: `POST /api/v1/ExecutionQueue/{id}/heartbeat` (pid, fase, últimas linhas do stderr). A resposta diz
  se o pedido foi **cancelado** ou o plano **pausado/cancelado** pela tela, e o executor age na hora (ver 1.5).
- O executor também manda heartbeat próprio (sem pedido) a cada 60 s. É isso que alimenta online/offline.
- `resume-candidates` continua respondendo para vigias antigos durante a transição (compatibilidade). Os
  executores novos usam só a fila.

### 1.4 O executor (substitui `prmake-card.sh agent`)

Recomendação: **um executável .NET 10 single-file** (`tools/Cime.ExecutionAgent`), publicado pelo PRMake como as
skills (`/Skills/tool`) e atualizado sozinho. Motivos:

- é a mesma stack do backend;
- é testável;
- não depende de jq/python3/Git Bash, o que resolve a 0035 de vez;
- supervisiona processos de verdade (pid, exit code, timeout, kill da árvore);
- roda igual em macOS, Windows e Linux.

Instalação como serviço do usuário: LaunchAgent (macOS), Agendador de Tarefas no logon (Windows) e
`systemd --user` (Linux).

Responsabilidades:

1. **Pegar pedidos** da fila (1.3), respeitando a concorrência máxima e o executor pausado.
2. **Preparar o workspace**:
   - Resolver a pasta pelo mapa repo → pasta (o card diz o produto; a configuração do PRMake diz os repos).
   - **Isolar por card com `git worktree`** quando a correção mexe num repositório. Dois cards em paralelo
     nunca dividem o mesmo checkout nem trocam a branch um do outro.
   - Worktrees de cards concluídos/cancelados são limpos depois de X dias.
3. **Rodar o Claude Code em modo headless, sob controle do executor**:
   - `analyze`: `claude -p "/analisar-bug <card>" --session-id <uuid gerado pelo executor> --output-format stream-json`.
     O id da sessão é conhecido **antes** de começar, já registrado no plano e no pedido.
   - `resume`/`correction`: `claude -p --resume <sessão> "<prompt de retomada da 0033>" --output-format stream-json`.
   - Lê o stream: sabe quando terminou, com qual resultado e quanto custou (o evento final traz tokens/custo e
     alimenta o `usage` mesmo se a skill não conseguir enviar).
   - Timeout por pedido (padrão 2 h, configurável) → kill + `failed` com motivo.
4. **Perfil de permissões do modo headless** (sem prompt de permissão, mas com cerca):
   - Arquivo de settings publicado junto com as skills: libera os scripts das skills, `git` de leitura/branch/
     commit/push de branch própria, `Read`/`Edit` dentro do workspace do card e a criação de PR pelo PRMake.
   - **Nega**: merge/aprovação de PR (`gh pr merge`, `gh pr review --approve`), `git push --force`, push em
     `master`/`main`/`develop`/branches de release, escrita em banco e Cognito, `rm -rf` fora do workspace.
   - Um hook `PreToolUse` reforça os bloqueios de merge e de banco (dupla checagem, já que o modo é sem prompt).
5. **Concorrência com o terminal**: enquanto o executor tem um pedido `running` no card, `prmake-card.sh <card>`
   (abrir no terminal) avisa e oferece "esperar terminar" ou "assumir". Assumir cancela o pedido e mata o
   processo headless antes do `--resume` interativo. Nunca dois processos na mesma sessão.
6. **Registrar o ciclo de vida no pedido e no plano**: começou (host, pid, sessão), terminou (exit code,
   resultado), falhou (últimas linhas do stderr, já sem token/segredo).
7. **Notificar localmente** (notificação do SO) só quando algo precisa do usuário: pergunta aberta, etapa do
   usuário, falha. Começar e terminar sem pendência não notifica.
8. **Diagnóstico**:
   - `cime-agent doctor`: Claude Code instalado e logado, skills atualizadas, credencial válida, repos mapeados,
     acesso aos bancos (aliases do `sql-hosts`) e ao KC.
   - O resultado vai para a tela "Meus executores". É o mesmo cuidado da 0037 com falta de permissão: a
     pendência aparece **antes** de a análise precisar.

### 1.5 Controle pela tela durante a execução

- **Pausar** (já existe): a skill continua respeitando `control`. Além disso, o executor vê a pausa no heartbeat,
  e se a skill não parar em X min (padrão 10), ele encerra o processo. A sessão fica para retomar depois.
- **Cancelar plano/pedido**: o executor mata o processo na hora (sem esperar a skill) e marca `cancelled`.
- **Continuar** e **respostas pela tela**: geram pedido `resume` (1.1). Se a máquina estiver offline, o pedido
  fica na fila e a tela diz isso.

### 1.6 Gatilhos

1. **Botão "Analisar com Claude"** no card (e na lista de cards). Abre um diálogo curto com:
   - executor: "minha máquina" (padrão) ou outra máquina minha;
   - observação opcional, que vira um comentário do plano (0031) e entra como contexto da análise.

   Se o card já tem plano aberto, o botão vira "Continuar com Claude" (pedido `resume`).
2. **Continuar / respostas** (1.5).
3. **Regras automáticas** (desligadas por padrão, por usuário, configuradas no PRMake e não na skill), por
   exemplo: "card de bug atribuído a mim, no estado X, na área Y → pedido `analyze`".
   - Origem: service hook do Azure DevOps para um endpoint do PRMake, ou leitura periódica pelo PRMake.
   - Limite diário de pedidos automáticos por usuário.
4. **API** (`POST /api/v1/ExecutionQueue`) para outras integrações.

### 1.7 O que o usuário vê no card (estado do pedido sempre evidente)

Faixa no topo do plano, sempre com o próximo passo explícito:

- **Na fila**: "aguardando a máquina *Alex-MacBook*". Se ela estiver offline: "*Alex-MacBook* está offline desde
  10:32. O pedido começa quando ela voltar." Ação: [escolher outra máquina].
- **Rodando** em *Alex-MacBook* desde 10:40, tentativa 1/3, mais o custo até agora.
- **Falhou**: o motivo em linguagem simples (ex.: "o Claude Code não está logado nesta máquina", "sem acesso ao
  banco do cliente X") e o detalhe técnico recolhido. Ações: [tentar de novo] e [abrir no terminal].
- **Aguardando você**: integra com a 0037. Pergunta aberta ou etapa do usuário fica em destaque, e o pedido
  seguinte só nasce quando o usuário age.

Mudanças de estado relevantes (começou, falhou, aguardando você) vão para a Timeline como marcos curtos, sem
duplicar o que o plano já escreve.

### 1.8 Limites e custo

- Concorrência por executor (1.2) e **orçamento diário por usuário** (tokens ou US$, usando o `usage` que o plano
  já registra). Estourou: novos pedidos ficam `queued` com o motivo "orçamento do dia atingido" até o admin
  liberar ou o dia virar.
- Custo por pedido visível no histórico do executor e no card.

### 1.9 Compatibilidade

- `/analisar-bug <card>` no terminal continua funcionando exatamente como hoje. O executor é opcional.
- `prmake-card.sh <card>` continua abrindo/retomando no terminal.
- `prmake-card.sh agent` vira um aviso: "substituído pelo executor, rode `install`".
- `resume-candidates` e `resume-request` continuam respondendo durante a transição.
- API antiga sem a fila (antes do deploy): o executor não sobe e diz isso. A skill segue igual.

---

## Parte 2 — Servidor MCP remoto do PRMake

### 2.1 O servidor

- Endpoint `/mcp` no host da API (`solvace.prform.api`), transporte **Streamable HTTP em modo stateless**, que
  roda no Cloud Run sem sessão presa a instância. Usar o SDK oficial `ModelContextProtocol.AspNetCore`.
- **Autenticação**: o mesmo `X-API-Key` (token do usuário ou credencial do executor da 1.2). Toda chamada pelo
  MCP conta como **executor** (mesma semântica do header `X-Execution-Client: skill`): pode o que a skill pode,
  e não pode o que a API já barra para o executor (ex.: editar/remover comentário do usuário).
- As ferramentas chamam a **mesma camada de aplicação** dos controllers (`IExecutionPlanApplication` etc.). Não
  existe regra duplicada nem caminho de escrita paralelo.
- OAuth (para conectar no claude.ai/Claude Desktop como conector) fica fora do escopo. O desenho não pode impedir
  isso depois.

### 2.2 Ferramentas (espelham o `prmake-plan.sh`)

Prefixo `prmake_`, respostas **enxutas**: só o que a skill usa, sem eco do payload. O ganho de tokens é medido
(2.5).

| Grupo | Ferramentas |
|---|---|
| Contexto | `context(card)`: o mesmo pacote do `prmake-plan.sh contexto` (plano criado/retomado, card, repro steps, comentários/anexos novos, trechos de KB/KC) |
| Plano | `plan_steps`, `step_update`, `step_wait`, `log`, `checkpoint`, `control` (pausado/cancelado/ok), `status`, `resume_info` |
| Perguntas | `ask(card, pergunta, opções)`, `answers(card)` |
| Comentários e anexos | `notes(card, n?)`, `attachment(card, ref)`: **devolve a imagem como conteúdo MCP** (bloco de imagem), sem baixar arquivo e sem `Read` (o "veja a imagem 2" vira uma chamada) |
| Configuração | `settings(card)`, `branches(card)`, `devops_config(card)`, `skills_config()` |
| Timeline | `timeline_add(card, texto)` |
| PR e card | `pr_text`, `save_pr_text`, `open_pr`, `devops_action(card, ação)`: as mesmas ações permitidas hoje (`dev-test-in-qa`, `ready-for-qa` com as regras da skill), sempre pelo PRMake |
| Conhecimento | `kc_search`, `kc_article`; KB por seção (`kb_show(projeto, seção)`) para quem não tem o espelho local |
| Fila | `queue_status(card)`: o estado do pedido (1.7) |

Recursos MCP (leitura): `prmake://card/{n}/plan` (plano resumido) e `prmake://card/{n}/notes`.

### 2.3 O que continua no script/executor (não faz sentido no MCP)

- **Arquivos locais** (`sync` de análises, scripts, dados): o servidor não lê o disco do usuário, e mandar o
  conteúdo em base64 pelo argumento da ferramenta gasta muito token. Continua no script, ou passa para o
  executor, que faz upload direto.
- **Espera longa** (`watch`/`wait` em segundo plano): ferramenta MCP não deve bloquear minutos. Com o executor,
  a espera deixa de ser da skill: a skill encerra a vez e o próximo pedido `resume` nasce sozinho (1.1). O
  `watch` fica só para quem roda sem executor.
- **Fila offline** (`.prmake-outbox.jsonl`): continua no script, como fallback.

### 2.4 Skill e instalação

- O instalador (`prmake-skills.sh install`) registra o servidor no Claude Code (escopo do usuário, transporte
  HTTP, header com a credencial) e `doctor` confere se ele responde.
- A `analisar-bug` (e `gerar-prmake`, `gerar-handover`, `prmake-timeline`) **prefere as ferramentas
  `mcp__prmake__*`** quando estão disponíveis e cai no script quando não estão. Os dois caminhos geram
  exatamente o mesmo resultado no PRMake.
- A migração é por grupo de ferramenta (contexto e plano primeiro), sem big bang.

### 2.5 Medição

- Comparar o `usage` por análise antes e depois (mesmo tipo de card): tokens de entrada e número de turnos.
- Comparar falhas de comando no Windows antes e depois.
- Registrar no `status.md` da feature.

---

## Critérios de aceite

1. Clico em "Analisar com Claude" num card, com o terminal fechado e a máquina ligada. Em até ~30 s o plano
   aparece "rodando em *minha máquina*", e a análise segue até a primeira pergunta sem eu tocar no computador.
2. Respondo pela tela. A sessão continua sozinha, na mesma conversa (`--resume`), e segue até a próxima pergunta
   ou PR. **Nunca faz merge.**
3. Máquina desligada: o card diz "na fila, *Alex-MacBook* offline desde HH:MM". Ao ligar, o pedido começa sozinho.
4. Mato o processo do Claude no meio. A trava vence, o pedido volta para a fila e tenta de novo (até 3 vezes).
   Depois disso, `failed` com o motivo legível no card e o botão "tentar de novo".
5. Cancelo pela tela. O processo morre em segundos e o pedido fica `cancelled`.
6. Dois cliques/dois usuários em "Analisar" no mesmo card geram um pedido só.
7. Dois cards em paralelo no mesmo repositório não interferem um no outro (worktrees separados).
8. No modo headless, uma tentativa de `gh pr merge` ou de push em `master`/`develop` é bloqueada pelo perfil de
   permissões e pelo hook, e vira `log error` no plano.
9. Funciona em macOS e Windows com o mesmo executor, sem jq/python3.
10. `/analisar-bug <card>` no terminal, sem executor e sem MCP, continua idêntico ao de hoje.
11. Com o MCP configurado, a análise usa `mcp__prmake__*` para contexto/plano/perguntas/anexos, e o resultado no
    PRMake é o mesmo do script. "Veja a imagem 2 do card X" mostra a imagem com uma chamada.
12. O MCP rejeita chamada sem credencial e respeita as mesmas restrições do executor na API.

## Fora do escopo (próximas features)

- **Executor central na nuvem** (VM com Claude Code headless ou Agent SDK, cobrança por chave de API). O desenho
  da fila e do MCP já serve para ele: seria só mais um executor. O bloqueio principal é o acesso aos bancos dos
  clientes.
- Executores compartilhados de time (pedido de uma pessoa rodando na máquina de outra).
- OAuth no MCP e conector no claude.ai/Claude Desktop.
- Mudanças nas regras da análise em si (são da 0037).

## Decisões em aberto

1. **Executor em .NET single-file (recomendado) × manter em bash.** O .NET resolve Windows e supervisão de
   processo. O bash reaproveita o que existe, mas herda as fragilidades.
2. **Gatilho automático: service hook do Azure DevOps × leitura periódica pelo PRMake.** O hook é imediato, mas
   precisa ser configurado na organização do DevOps.
3. **Orçamento diário: por usuário ou global, e quem libera ao estourar** (admin? gestor?).
4. **Retenção dos worktrees** de cards concluídos (dias).
