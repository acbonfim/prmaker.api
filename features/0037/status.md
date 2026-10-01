# Status — Feature 0037

Branch `feature/0037` em `prform.api-0037` (backend + skills) e `prform-app-0037` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | `WaitingOn` na etapa + `resolve` + migração `AddStepWaitingOn` | ✅ concluída | a361ca1 |
| B2 | Pendências calculadas, `GET pending`, tempo real, Timeline | ✅ concluída | a361ca1 |
| F1 | Painel: aviso "Aguardando você", estado na linha, botões na linha | ✅ concluída | front f7fabc6 |
| F2 | Sino do topo, chip do card, recentes, título da aba/badge | ✅ concluída (notificação do navegador fica para depois) | front f7fabc6 |
| S1 | Permissões do `sql-query.sh`/`cognito-query.sh` + doctor + `permissions` | ✅ concluída | (ver log) |
| S2 | `block`/`unblock`, `control`/`watch` com pendências e regra de etapa travada | ✅ concluída | (ver log) |
| S3 | `sql-query.sh --ping`, preflight e análise não fecha sem banco | ✅ concluída | (ver log) |
| S4 | Resumo PT/EN = orientação ao cliente; plano conclui no fechamento (item 2) | ✅ concluída | (ver log) |
| Q1 | Teste local | ✅ backend + front + skill | |

## Handoff
- S1–S4 feitas depois que o usuário liberou a edição de `skills/` (branch `feature/0037-skill`). A versão das skills é o hash do
  conteúdo (`skills/README.md`) — publicada no próximo deploy da API.
- Contrato que a skill deve usar (já no backend): `PATCH steps/{key}` `{status:"waiting", waitingOn:"user", reason:"<o que fazer>"}`
  (reason obrigatório; 400 sem ele); qualquer outro status limpa o waitingOn; `control` devolve `steps[].waitingOn/reason/changedBy`,
  `userPending`, `userActions[]`. O `watch` atual já acorda com o *Já resolvi* (`etapa X: waiting -> running`).
- Teste local: `.t0037/` (fora do git) — Postgres `cime-pg-0037` na 55438 com os schemas do host + `auth` (migrações da Cime.Auth e um
  usuário com o ExternalId do token, para `GET pending` e o sino funcionarem), `scen.sh` (cenário do card 74669), `s1.mjs`/`s2.mjs`.

## Log
- 2026-10-01 — análise e plano.
- 2026-10-01 — spec ganhou o item 2 (resumo PT/EN = orientação ao cliente; plano conclui no fechamento) → fase S4.
- 2026-10-01 — B1/B2 (a361ca1): WaitingOn, resolve, UserActions/UserPending, GET pending, grupo execplan-pending, Timeline; migração com backfill.
- 2026-10-01 — F1/F2 (front f7fabc6): aviso "Aguardando você", estado/ação na linha, "Parada com um aviso", sino com lista, selo nos recentes,
  (N) no título, chip do card.
- 2026-10-01 — Q1 local: waiting/user sem reason → 400; control com 3 pendências (pergunta, unblock, etapa do usuário); tela com aviso, sino (3),
  chip, Timeline "⚠️ Aguardando você"; "Já resolvi" → etapa running, título (3)→(2), `watch` da skill instalada acordou
  (`confirmar-dados: waiting -> running`); "Concluir" na linha libera a próxima etapa do usuário.
- 2026-10-01 — back (#54) e front (#32) mesclados e publicados.
- 2026-10-01 — S1–S4 (branch `feature/0037-skill`): `PERMISSION_GROUPS` (devops-v1 + readonly-v2 com `sql-query.sh`/`cognito-query.sh`),
  `doctor` confere, `prmake-skills.sh permissions` reaplica; `block`/`unblock`; `control` separa "aguardando o usuario" e mostra as pendências;
  `watch` imprime `PENDENCIA RESOLVIDA` no *Já resolvi*; `sql-query.sh --ping` (exit 2 VPN, 3 login, 4 credenciais); regras: block na hora,
  card com dados não fecha sem o banco, seção "Banco de dados" obrigatória, orientação ao cliente = resumo PT/EN, sem `orientar-cliente`/
  `validar-cliente`, plano conclui no `fechar-card`.
- 2026-10-01 — Q1 da skill (API local): block → 200 e "aguardando o usuario" no control; block sem texto → erro; *Já resolvi* pela API → watch
  `PENDENCIA RESOLVIDA pelo usuario (Admin Teste)`; unblock → running; `permissions` num HOME falso: 6 regras novas, idempotente; doctor acusa
  a regra ausente; `--ping` sem credenciais → 4, host inalcançável → 2.

## Melhorias (rodada 2 — branch `feature/0037-melhorias` nos dois repos)
| Item | O quê | Status |
|---|---|---|
| M1 | Sino: clicar na pendência abre o card na tela de PR (a tela passa a seguir o `?card=` da URL) | ✅ |
| M2 | Comentário instantâneo: aparece na hora com "enviando…" (animação), caixa livre; erro devolve o texto; edição/remoção também | ✅ |
| M3 | Acesso aos bancos: etapa 4 na tela *Skills do Claude* (permissão + credenciais com segurança); `prmake-skills.sh db-credentials [list]` (só em terminal interativo, senha sem eco, arquivo 600, testa com `--ping`); `doctor` mostra as credenciais; `contexto` diz se há credencial; `sql-query` sem credencial → exit 4 com a orientação; skill diz claramente que está sem acesso e nunca pede senha no chat | ✅ |
| M4 | Conversa com o Claude em popup (ícone do rodapé / "Abrir conversa"): bolhas, dia, ✓/✓✓, "o Claude leu e está analisando" (backend grava `NotesReadNumber/At` quando a skill lê os comentários — migração `AddNotesRead`), destino em menu (plano análise/correção + etapa com status), Enter envia, sugestões, colar/arrastar | ✅ |
| M5 | Ao criar o plano o card é salvo no PRMake (registro em PullRequest, como o botão Salvar) — `IExecutionCardRegistrar` | ✅ |

Teste local: sino 74700 → 74669 troca o card; plano criado → `prform.PullRequests` com o card (FormId 1, usuário do token); comentário pela tela →
`notesReadNumber` null; `prmake-plan.sh notes` → 1 e a tela mostra "analisando o seu comentário #1…"; POST atrasado 2,5 s → bolha "enviando…" na hora;
`db-credentials` sem TTY recusa, com TTY grava (600) e `list`/`doctor` mostram sem senha; `sql-query` sem credencial → exit 4.

## Rodada 3 — Base Solvace e integrações (branch `feature/0037-base-solvace` nos dois repos)
| Item | O quê | Status |
|---|---|---|
| K1 | Sugestão incorporada com a IA saía da lista só após F5: o painel resolvia a sugestão depois de avisar "salvou" (a tela fechava o painel e o aviso se perdia). Agora resolve antes e a tela confere a lista | ✅ |
| K2 | Busca no conteúdo (`GET Architecture/search`): seções e artigos, sem acento/caixa, radical simples, nomes técnicos inteiros, trecho + cabeçalho mais próximo | ✅ |
| K3 | "Pergunte à Base Solvace" (`POST Architecture/ask`): IA gera termos/projetos prováveis → busca no conteúdo → IA escolhe os trechos e explica; sem IA, cai na busca no conteúdo com o motivo. Campo na visão geral, Enter no filtro, disparo automático quando a busca simples não acha e o texto é pergunta; resultado abre a seção rolando até o trecho (`?h=`) | ✅ |
| K4 | Integração pessoal **opcional** (ex.: Claude) bloqueava a tela de PR depois de abrir "Minhas integrações": o front recalculava o status contando as opcionais (o backend não conta) | ✅ |

Teste local: base semeada com o espelho `~/.claude/solvace-kb` (62 projetos, 243 seções); "Como saber se o usuário fez login com sucesso?" → 1º resultado
`login › Login e permissões › Peças`, clique rola até "Peças"; `TB_WCM_USER LAST_SITE_ID` → login › Peças. O caminho com IA não foi testado aqui (sem plugin de IA no banco isolado).
