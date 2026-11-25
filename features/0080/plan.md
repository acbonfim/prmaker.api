# Feature 0080 — Especialista do caso + tela do plano mais amigável

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch `feature/0080` a partir da `master` nos dois repos (worktrees `../prform.api-0080` e
> `../solvace.prform.web/prform-app-0080`, `node_modules` linkado de `../prform-app-0019/node_modules`).

| Repo | Caminho | Sigla |
|---|---|---|
| Backend | `prform.api` (módulo `Solvace.ExecutionPlans`, host `solvace.prform.api`) | `B*` |
| Executor | `tools/Cime.ExecutionAgent` (no back) | `E*` |
| Skills | `skills/especialista-do-caso` (nova), `skills/analisar-bug` | `S*` |
| Frontend | `../solvace.prform.web/prform-app` | `F*` |

## 1. Análise (o que existe e vamos reusar)
- Comentários do plano (`ExecutionNote`, 0031/0037): são prompts do executor principal (`TriggerResumeAsync` com
  `Source=note`). O modal `plan-chat-dialog` envolve o `plan-notes` (variante `chat`).
- Fila (0039): `ExecutionRequest` com `Kind` analyze/resume e **um pedido ativo por card** (índice parcial único
  `IX_ExecutionRequests_ActiveCard`); `busy` = pedidos ativos do executor ≥ `MaxConcurrency`. O executor
  (`Runner.cs`) pega o próximo de `GET ExecutionQueue/next` sem filtro por tipo; `JobRunner` monta `claude -p` (sem
  `--mcp-config`; MCP vem do usuário com a api-key dele; os scripts leem `~/.claude/prmake-token.txt` ou
  `PRMAKE_TOKEN`). Atividade ao vivo pelo heartbeat (0050). Texto do assistente não é capturado.
- Credencial do executor: JWT com as claims do dono + `executorId` (`ExecutorTokenIssuer`) e
  `ExecutorCredentialMiddleware` limitando caminhos. Mesmo esquema serve para o **token do especialista**.
- MCP (`PrmakeMcpTools`): quem chama é sempre executor; `ActorAsync` lê `ExternalId`.
- Tempo real: `IRealTimeNotifier` → grupo `execplan:{card}`; front `WsService`.
- Avatares: `app-user-avatar` + `AuthService.getPhotosByExternalIds`. Ditado: `Dictation` em `kb-ask-attach.ts`.

### Pontos de atenção
1. **Pista própria na fila**: pedido `kind=specialist` fica fora do índice "um ativo por card" (índice novo só para
   ele), fora da contagem `busy` e só vai para executor que pede a pista (`GET next?lanes=main,specialist`) — executor
   antigo nunca recebe (rodaria sem o token restrito).
2. **O especialista não altera o plano — no servidor**: no claim o servidor emite um **token do caso** (claims do dono
   + `caseCard` + `caseRequest`). `CaseSpecialistMiddleware`: com esse token, só `GET`, `CaseChat/*` e o MCP; no MCP
   só as ferramentas da lista (leitura + `prmake_case_*`), conferindo o corpo JSON-RPC (`tools/call`). O token só vale
   enquanto o pedido estiver ativo. O executor passa o token por `--mcp-config --strict-mcp-config` e `PRMAKE_TOKEN`, e
   nega ler `prmake-token.txt`.
3. Atividade do pedido do especialista não pode ir para a fila/tela do plano (o plano mostraria "Claude rodando"):
   `activity`/`request` do especialista vão no evento do chat.
4. Proposta aceita → **comentário-instrução no plano atual** (autor = quem aceitou), que já retoma o executor
   principal; a `analisar-bug` reconhece "Proposta do Especialista aceita" e gera a nova versão (sem executar se
   pedido).
5. Migrações: `ExecutionPlanContext` (tabelas `Case*`, colunas de divergência, índices da fila) e `DefaultContext`
   (seed `Specialist*` no Skills Configurations).
6. Front zoneless: condições em campos comuns só atualizam com signal/`detectChanges`.

## 2. Fases

### Onda 1
**B1 — Domínio e banco do caso** · `CaseChat` (card único; sessão do especialista, máquina, totais), `CaseMessage`
(número por card, autor pessoa/especialista/sistema, tipo text|voice|proposal|action|system, markdown, transcrição,
`PeopleOnly`, `Deep`, menções, passos, tokens/tempo/modelo, `RequestId`), `CaseFile` + `CaseFileContent` (bytea, 10
MB, 20 por mensagem, 100 MB por card), `CasePoint`, `CaseProposal` (mudanças jsonb, status, quem decidiu, executar),
`CaseAction` (confirmação de ação com efeito fora da conversa). Plano: `DivergenceText/At/ConfirmedBy/At`. Fila: kind
`specialist`, índices. Migração `CaseChat0080`. Testes de domínio.

**B2 — Aplicação, API, fila e guarda** · `CaseChatApplication` (mensagens com anexos, quem responde — regra da
decisão 2 —, enfileirar o especialista na máquina de quem escreveu/dono do plano, contexto compacto, pontos,
propostas, ações, digitando, histórico dos comentários do plano), `CaseChatController` (`api/v1/CaseChat`), eventos
`caseChatUpdated`, pista na fila (`lanes`, prompts, modelo `SpecialistModel`/`SpecialistDeepModel`, sessão no
`CaseChat`, custo na mensagem), token do caso + `CaseSpecialistMiddleware`.

**B3 — MCP, proposta → plano, divergência, config** · `CaseChatMcpTools` (`prmake_case_context|messages|reply|point|
propose|action|action_done|file`), aceitar proposta (permissão: dono do plano, admin, gestor) → instrução no plano,
log `divergence` + `POST {id}/divergence/confirm` + portão (etapa `pr` não começa com divergência pendente), situação
sem handoff velho após cancelar, seed `Specialist*` (DefaultContext).

### Onda 2
**E1 — Executor 1.2.0** · pista do especialista (até 2 por máquina, além do `MaxConcurrency`), `next?lanes=…`,
`--mcp-config` com o token do caso, `PRMAKE_TOKEN`/`PRMAKE_SPECIALIST`, settings próprios (nega o token do usuário),
retoma a sessão do caso, timeout curto (20 min).

**S1 — Skill `especialista-do-caso`** · SKILL.md (papel, ciclo por mensagem, nunca altera o plano, ações com
confirmação, proposta ao concluir, formato rico), `scripts/prmake-case.sh` (`file`, `upload`), `skill.json`.
**S2 — `analisar-bug`** · lê "Proposta do Especialista aceita" e gera nova versão; grava `divergence` quando a base
difere da análise; não responde comentário no log da etapa (o chat é do especialista).

### Onda 3
**F1 — Especialista do caso** · serviço + modelo, `case-chat-dialog` (avatares com foto, ao vivo, bolhas ricas,
pontos, arquivos, propostas com "Ver o que muda", ações), compositor (colar, arrastar, arquivos, voz com
`MediaRecorder` + ditado, @menção, "só entre pessoas", "investigar a fundo", atalhos), botão no cabeçalho, prévia ao
vivo na coluna da direita, celular (tela cheia, botão fixo).
**F2 — Tela do plano** · M1 cabeçalho com rótulos e ação principal, M2 jornada, M3 "Em que pé está", M4 decisões,
M5 alerta de divergência, M6 etapas concluídas agrupadas, M7 "Arquivos e textos", sem comentários inline.
**F3 — Cancelar guiado** · diálogo com motivos + "Conversar primeiro".

### Onda 4
**Q1 — Integração** · build back/front, testes (`dotnet test` do módulo, specs do front), e2e local (harness 0075/0077)
com o roteiro do 77881; PRs nos dois repos (sem merge).
