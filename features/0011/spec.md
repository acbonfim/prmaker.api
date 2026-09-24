Tipo: Nova funcionalidade
Prioridade: a definir

Objetivo: Acompanhar **em tempo real, dentro do card no PRMake**, uma sessão do Claude Code de um dev trabalhando naquele card: o que foi pedido, o que o Claude respondeu, quais arquivos leu e editou, quais comandos rodou e quando está parado esperando permissão. Só leitura: o PRMake mostra a sessão, mas não a controla.

Situação atual (confirmada no código em 2026-09-24):

1. O Claude Code tem o **Remote Control** (`/rc`), que abre a sessão local no claude.ai/code e no app do celular. Ele não tem API nem embed para sistemas de terceiros: não dá para colocar o `/rc` dentro do PRMake.
2. O Claude Code tem **hooks**: comandos executados em pontos da sessão, que recebem no stdin um JSON com `session_id`, `transcript_path`, `cwd`, `hook_event_name` e, conforme o evento, `prompt`, `tool_name`, `tool_input`, `tool_response`, `message`. A conversa inteira também fica gravada em um arquivo JSONL (`transcript_path`), inclusive o texto das respostas do Claude, que os hooks de ferramenta não trazem.
3. O PRMake já recebe dados do Claude Code pelas skills `gerar-prmake` e `prmake-timeline` (`POST /api/v1/Timeline`, header `x-api-key` com a api-key pessoal, card derivado da branch `hotfix/<card>` / `bugfix/<card>`).
4. O backend já tem tempo real: `Cime.BuildingBlocks.RealTime` (SignalR, `RealTimeHub` com `AddToGroup`/`RemoveFromGroup`, `IRealTimeNotifier`) e o grupo por card `pullrequest:{card}` com o evento `pullRequestCardUpdated` (`PullRequestRealTimeEvents`). O front já se conecta pelo `WsService`.

Features:

1. **Script de hook** (`prmake-live`), no mesmo padrão das skills de usuário (`~/.claude/skills/prmake-live/scripts/prmake-live.sh`), reusando o token do `gerar-prmake` (`PRMAKE_TOKEN` → `~/.claude/prmake-token.txt` → `.claude/prmake-token.txt`). Ele é registrado nos hooks do Claude Code e envia ao CIME um evento por acontecimento da sessão:

   | Hook | Evento no PRMake |
   |---|---|
   | `SessionStart` | sessão iniciada (repo, branch, pasta, origem: nova/retomada/após compactação) |
   | `UserPromptSubmit` | prompt do dev |
   | `PreToolUse` | ferramenta iniciada (ex.: `Bash` com o comando, `Edit` com o arquivo, chamada MCP) |
   | `PostToolUse` | resultado da ferramenta (resumo, truncado e mascarado) |
   | `Notification` | Claude aguardando permissão ou input do dev |
   | `Stop` / `SubagentStop` | fim da resposta, com o **texto do assistente** lido do `transcript_path` desde o último envio |
   | `SessionEnd` | sessão encerrada (motivo) |

2. **Identificação do card** pela branch atual (`hotfix/<card>`, `bugfix/<card>`, `feature/<card>`), com prioridade para a env `PRMAKE_CARD` ou o arquivo `.claude/prmake-card` do projeto. Sem card identificado, a sessão é registrada e aparece só em "Minhas sessões" (item 6), podendo ser vinculada a um card depois.
3. **API no CIME** (módulo PullRequests):
   - `POST /api/v1/ClaudeSessions/events`: recebe um evento ou um lote, autenticado pela api-key pessoal (o usuário vem da chave, como na Timeline).
   - `GET /api/v1/ClaudeSessions/card/{cardNumber}`: sessões do card (dev, status, início, último evento, repo/branch).
   - `GET /api/v1/ClaudeSessions/{id}/events?afterSeq=`: eventos da sessão, paginados, para abrir uma sessão em andamento ou já encerrada.
   - `GET /api/v1/ClaudeSessions/mine`: sessões do usuário logado.
   - `PUT /api/v1/ClaudeSessions/{id}/card`: vincular ou trocar o card de uma sessão (só o dono).
4. **Tempo real**: cada evento gravado é enviado por SignalR no grupo `claude-session:{id}` (feed aberto) e, quando a sessão inicia, muda de status ou termina, também no grupo do card (`pullrequest:{card}`, novo `Action` `claude-session`), para a lista de sessões do card se atualizar sozinha.
5. **Tela no card**: nova seção/aba "Claude ao vivo":
   - lista de sessões com dev, selo de status (● **Ao vivo**, ⏳ **Aguardando permissão**, ⏸ **Ociosa**, ✔ **Encerrada**), início, duração e repo/branch;
   - feed da sessão: prompts do dev e respostas do Claude (markdown, usando o `timeline-markdown` pipe) em formato de conversa; ferramentas como linhas compactas e expansíveis, com ícone por tipo (leitura, edição com diff, comando com saída, busca, MCP);
   - rolagem automática para o fim, pausada quando o usuário rola para cima, com o botão "↓ novos eventos";
   - filtro por tipo (conversa, edições, comandos, tudo).
6. **Minhas sessões** (menu do usuário): sessões do próprio dev, inclusive as sem card, com a opção de vincular a um card.
7. Na lista de cards/PRs, um indicador discreto no card com sessão ao vivo: "● Claude ao vivo · Fulano".
8. Opcional (avaliar na análise): no `SessionEnd`, gravar na **Timeline** do card um resumo curto da sessão (duração, nº de prompts, arquivos editados), sem o conteúdo.

Regras:

1. **Nunca atrapalhar o Claude Code**: o script sempre termina com exit `0`, envia em background com timeout curto (ex.: 3 s) e não imprime nada no stdout (a saída de alguns hooks entra no contexto do Claude). Se o CIME estiver fora do ar ou o token for inválido, os eventos são descartados, ou guardados em fila local limitada para reenvio no próximo evento. Não pode travar nem deixar a sessão lenta.
2. **Opt-in**: nada é enviado sem o dev instalar os hooks e ter o plugin habilitado (item 9). O dev pode desligar a qualquer momento com `PRMAKE_LIVE=off`.
3. **Mascaramento e truncamento no script, antes de enviar** (o banco de dev é o de produção, e `tool_response` pode trazer conteúdo de arquivos):
   - mascarar connection strings, `password=`/`pwd=`, `Bearer …`, `x-api-key`, chaves AWS/GCP, tokens do GitHub (`ghp_`, `github_pat_`), JWT e padrões configuráveis pelo admin;
   - não enviar o conteúdo lido de arquivos sensíveis (`.env*`, `appsettings*.json`, `*.pem`, `*.key`, `prmake-token.txt`, `secrets*`), só o nome do arquivo;
   - truncar `tool_input`/`tool_response` a um limite configurável (padrão 4 KB por evento), marcando "… (truncado)";
   - modo **só metadados** (configuração pessoal): envia o tipo do evento, a ferramenta e o arquivo/comando, sem saídas nem texto das respostas.
4. O backend repete o mascaramento como segunda barreira e rejeita eventos acima do limite de tamanho (`413`).
5. **Idempotência e ordem**: cada evento leva um id (UUID gerado pelo script) e um `seq` por sessão. Reenvio não duplica, e o feed ordena por `seq`.
6. **Visibilidade**: o dono vê sempre as próprias sessões. Os demais usuários com acesso ao card veem as sessões vinculadas a ele (regra final de papéis a validar, ver abaixo). A sessão é identificada pelo `session_id` do Claude Code, e só o dono da api-key que a criou pode enviar eventos para ela.
7. **Status da sessão** calculado pelo backend: `Ao vivo` (evento há menos de N min), `Aguardando permissão` (último evento é `Notification` de permissão), `Ociosa` (sem eventos há mais de N min), `Encerrada` (`SessionEnd`). Sessão sem `SessionEnd` fica `Encerrada` depois de X horas sem eventos.
8. **Retenção**: os eventos são apagados depois de N dias (padrão 30, configurável pelo admin), e a sessão fica só com os metadados. Limpeza por job/rotina no startup, sem bloquear o startup.
9. Configuração pelo plugin novo **"Claude Live Configurations"**:
   - admin: `Enabled`, `MaxEventBytes`, `RetentionDays`, `IdleMinutes`, `RedactPatterns` (regex extras), `MaxEventsPerMinute` (limite por sessão, excedente com `429`);
   - pessoal: `Enabled` (opt-in), `MetadataOnly`, `SendAssistantText`.
10. Mudança de schema só no MySQL (`DefaultContext`), com migração. Tabelas de exemplo: `ClaudeSessions` (SessionId, UserId, CardNumber, Repo, Branch, Cwd, Source, Status, StartedAt, LastEventAt, EndedAt, EndReason) e `ClaudeSessionEvents` (Id, ClaudeSessionId, EventUid, Seq, Type, ToolName, ToolUseId, Summary, Payload JSON, OccurredAt), com índice único em (ClaudeSessionId, EventUid).
11. Fora do escopo: enviar prompts, aprovar permissões ou controlar a sessão pelo PRMake (isso é o `/rc`).
12. Segurança: nenhum token ou payload bruto em log. A api-key vai só no header, nunca na URL.

Validar antes de implementar:

1. **Campos dos hooks** na versão atual do Claude Code usada pelo time: confirmar o JSON de cada evento (principalmente `tool_response` no `PostToolUse`, `message` no `Notification` e `reason` no `SessionEnd`) e o formato das linhas do transcript JSONL, que não é uma API oficial e pode mudar entre versões.
2. **Quem pode ver** as sessões de outros devs no card: todos com acesso ao card, ou só `gestor`/`admin`/`support`? Há questão de privacidade/LGPD a alinhar com o time antes de ligar por padrão?
3. **Volume**: estimar eventos por sessão (uma sessão longa pode passar de mil tool calls) para definir o limite, a paginação e a retenção sem pesar no MySQL do MonsterASP.
4. O script precisa rodar no macOS e no Windows (Git Bash/WSL)? Isso define bash + `curl` + `jq` ou um script em Node.

---

## Passo a passo de configuração (como vai ficar depois da feature)

### A. Admin (uma vez)

1. No CIME, em **Plugins → Claude Live Configurations**, marque **Enabled** e ajuste os limites se precisar (padrões: 4 KB por evento, 30 dias de retenção, 5 min para ociosa).

### B. Cada dev

1. Tenha a api-key pessoal configurada (a mesma do `gerar-prmake`: **Minha API Key** no menu do usuário, salva em `~/.claude/prmake-token.txt`).
2. Instale a skill/script `prmake-live` em `~/.claude/skills/prmake-live/` (distribuição definida no plano, ex.: junto com as outras skills do PRMake).
3. Em **Minhas integrações → Claude Live Configurations**, marque **Enabled** (e, se preferir, **MetadataOnly**).
4. Adicione os hooks em `~/.claude/settings.json` (vale para todos os repositórios):

```json
{
  "hooks": {
    "SessionStart":     [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "PreToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "PostToolUse":      [{ "matcher": "*", "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "Notification":     [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "SubagentStop":     [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }],
    "SessionEnd":       [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/skills/prmake-live/scripts/prmake-live.sh" }] }]
  }
}
```

5. Reinicie o Claude Code. Em uma sessão, `/hooks` mostra os hooks registrados.

### C. Conferir

1. Numa branch `bugfix/<card>`, abra o Claude Code e peça algo simples (ex.: "leia o README").
2. No PRMake, abra o card: a sessão aparece em **Claude ao vivo** com ● **Ao vivo**, e o prompt, a leitura do arquivo e a resposta entram no feed em poucos segundos.
3. Peça um comando que exija permissão: o selo muda para ⏳ **Aguardando permissão**.
4. Saia do Claude Code (`/exit`): a sessão passa para ✔ **Encerrada**.
5. Para pausar o envio numa sessão: `PRMAKE_LIVE=off claude`.
