# Status — Feature 0050

Branch `feature/0050` em `prform.api-0050` (API, executor, skill) e `prform-app-0050` (front, `node_modules` →
`prform-app-0019`). Executor sobe para **1.0.8** (autoatualiza).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | 8f3a332 |
| B2 | ✅ concluída | Claude | 8f3a332 |
| B3 | ✅ concluída | Claude | e2ae93d |
| E1 | ✅ concluída | Claude | 9dc525d |
| S1 | ✅ concluída | Claude | 4f89d44 |
| F1 | ✅ concluída | Claude | front 5e2ec3e |
| F2 | ✅ concluída | Claude | front 5e2ec3e |
| T1 | ✅ concluída | Claude | — (harness fora do git em `.t0050/`) |

## Decisões (perguntas em aberto da spec, sem resposta → padrão do plano)
- Sem `TicketTemplate` configurável: o texto do chamado continua gerado pela skill (1ª linha = título; corpo com o
  link do card, cliente, ambiente, banco, o que o script faz, rollback e validação).
- Rótulo do banco: só "Consultando o banco" (sem o alias do servidor).
- Atividades somem no `finish` (e quando o pedido volta para a fila).

## Log
- 2026-10-02 — spec, plano e status; worktrees criados a partir de `origin/master` (17adf7b).
- 2026-10-02 — B1: `ExecutionRequest.CurrentActivity/CurrentActivityTool/CurrentActivityAt/RecentActivities`
  (migração `ExecutionRequestActivity`); heartbeat aceita `activity`/`recent`, `ExecutionActivity.Normalize` corta em
  120 caracteres e descarta o que parece segredo (senha=, -p<algo>, connection string, token, JWT); hora no futuro vira
  "agora"; evento realtime `activity` com o payload (a tela não refaz o GET); não toca `LastActivityAt`. Fila com
  `waitCode=updating` quando o executor livre está desatualizado. `prmake_queue` devolve a atividade.
- 2026-10-02 — B2: tipo `ticket` (`chamado*.md`); `ExecutionPhaseFiles.Reject`: na análise, gravação da skill/MCP de
  `.sql` que altera dados (fora de comentários e aspas) ou de `ticket` é recusada com mensagem de regra; o usuário pela
  tela anexa o que quiser. `sync` do `prmake-plan.sh` avisa e segue no arquivo recusado; `chamado*.md` sobe como `ticket`.
- 2026-10-02 — B3: etapa `kind=ticket` não passa para o usuário (conclusão da etapa de que depende, `waiting`...) sem
  `script` + `ticket` com a key dela; `prmake_control` traz `ticketStepsMissingFiles`; `prmake_plan` lista os arquivos
  por etapa.
- 2026-10-02 — E1: `ActivityLabel` (tool_use → rótulo pt-BR, repo pelo mapa da 0048, nunca o comando) +
  `ActivityTracker` (últimas 10); o laço do heartbeat manda na mudança (mínimo 5 s) além dos 30 s.
- 2026-10-02 — S1: SKILL.md, `correcao.md`, `consultas.md`, `analise-template.md` (este mandava gerar o "SQL de
  correção" na análise — origem do caso do 75067): análise só com consultas somente leitura; script de alteração e
  chamado na correção, na etapa do chamado (com `dependsOn` numa etapa que prepara o script); citar os arquivos pelo nome.
- 2026-10-02 — F1/F2: linha "agora" no aviso do pedido e sob a etapa em andamento (relógio de 1 s só com o Claude
  rodando), histórico das últimas 10 ao clicar, "sem novidade há X min" a partir de 3 min, fila com "desde HH:mm" e
  tentativa; botão "Chamados" no rodapé; na etapa `ticket`, bloco "Texto do chamado" com Copiar título / Copiar corpo /
  baixar o script.
- 2026-10-02 — T1 (Postgres 55450, API local na 5083, `.t0050/`): `e2e.py` 21/21 OK (atividade, segredo descartado,
  corte, finish limpa, `updating`, recusas na análise, upload do usuário livre, etapa do chamado travada sem arquivos e
  liberada com eles, cada plano com os seus arquivos). Executor 1.0.8 real + `fake-claude` emitindo tool_use: rótulos
  "Consultando o banco" (comando com `-pS3nh@` não vazou), "Procurando “TriggerUser” no edv-solvace", "Lendo
  UserService.cs (edv-solvace)", "Gravando 00_consulta-usuarios.sql no plano" chegaram em ≤ 5 s. Chrome headless: aviso
  e etapa com a atividade e o "há X s", histórico, bloco do chamado com os botões (`shots/`). Erros de console só do
  `PluginConfiguration` com o banco vazio (pré-existente).
