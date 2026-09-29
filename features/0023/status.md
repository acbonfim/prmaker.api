# Status — Feature 0023 (Plano de execução da skill analisar-bug no PRMake)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0023` no backend (`../prform.api-0023`) e no front (`../solvace.prform.web/prform-app-0023`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Módulo `Solvace.ExecutionPlans` (schema `execution`), API, tempo real, migração | 1 | — | ✅ concluída | Claude | `b4a5d9b` |
| S1 | Skill: `prmake-plan.sh` (fila local, retentativas, pausa) + `SKILL.md` | 1 | — | ✅ concluída | Claude | `1bfb2f1` |
| F1 | Serviço + `app-execution-plan` (etapas animadas, detalhes, status, ações, tela cheia) | 2 | B1 | ✅ concluída | Claude | front `16b350a` |
| F2 | Visualizador de arquivos (SQL, markdown, JSON, imagem; copiar, baixar, zip) | 2 | B1 | ✅ concluída | Claude | front `16b350a` |
| F3 | Seção entre PRs e Linha do tempo (layout responsivo) | 2 | F1 | ✅ concluída | Claude | front `16b350a` |
| T1 | Teste ponta a ponta local (Postgres do docker, API, skill, navegador) | 3 | B1, S1, F1–F3 | ✅ concluída | Claude | — |
| Q1 | PRs e deploy | 4 | T1 | ✅ concluída | usuário + Claude | api #29, web #19 |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **B1** — testado com a API local (Postgres 18 isolado em docker, porta 55433; auth falso só com `is-user-active`; api-key de dev assinada com a chave de Development): criar plano, upsert de etapas (pendente que some é removida), etapa em andamento tira o plano de pendente, logs com `clientId` repetido não duplicam, atividade da etapa acompanha o último log, pausar → `control` devolve `wait` e a skill não tira da pausa, continuar, cancelar etapa (motivo + nome) → aparece em `cancelledSteps`, etapa concluída não cancela, cancelar plano cancela as pendentes com motivo e trava novas alterações (`stop`), download (`attachment`/`inline`, `nosniff`), `.zip` com pastas por tipo, 10 MB+1 recusado, 20 atualizações simultâneas na mesma etapa → 20 × 200 (xmin + retentativas com espera aleatória). Migração `InitialExecutionPlans` aplicada pelo `--migrate`.
- **S1** — skill atualizada em `~/.claude/skills/analisar-bug` (cópia em [`skill/`](./skill/)). Testado contra a API local (também no bash 3.2 do macOS): `start` cria (etapas padrão) e **retoma** o plano aberto (mesmo sem o `.prmake-plan.json`), `steps`, `step`, `log` (mensagem longa vira pedaços de 18 000), `checkpoint`, `sync` (só envia o que mudou, pelo sha256; imagem em qualquer pasta vira `image`), `pull`, `control` (0/10/11), `wait` (tela pausa → skill espera → tela continua → segue), etapa cancelada na tela aparece para pular, plano cancelado → `stop` e 400 nas alterações. API fora do ar → envios na fila local `.prmake-outbox.jsonl`, reenviados em ordem na chamada seguinte. **Bug achado e corrigido no teste**: o reenvio da fila sobrescrevia o corpo do envio atual (um log foi enfileirado com o corpo de outra chamada) — a fila usa arquivo próprio. **Antes do deploy**: `start` contra produção → 404 → `PLANO INDISPONIVEL` e os comandos viram no-op (a análise segue como antes, verificado com o token real, só GET); 5xx também; 401/403 param com mensagem de token.
- **F1–F3** — `app-execution-plan` entre PRs e Linha do tempo (grid 3 colunas; ≤ 1280 px PRs + plano à esquerda e timeline à direita; ≤ 900 px empilhado), `plan-files-dialog`, `FullscreenPanel` (mesma mecânica da tela cheia da timeline, reaproveitável — a timeline pode migrar para ele depois). Estilos de markdown sem `:host` (na tela cheia o painel vive no `<body>`).
- **T1** — ponta a ponta local: Postgres 18 (docker, porta 55433), API em Development na 5083 (hub em processo; **no dev foi preciso `RealTime__AllowedOrigins__0=http://localhost:4200`**, porque o array vazio do `appsettings.Development.json` não sobrescreve a lista do `appsettings.json`), auth falso (`is-user-active`), build de dev do front servido na 4200 e Chrome headless (puppeteer-core) com sessão de teste; a skill real (`prmake-plan.sh`) apontando para a API local. Resultados:

  | Cenário | Resultado |
  |---|---|
  | Card sem plano | "Nenhum plano…" + comando `/analisar-bug <card>` copiável |
  | Skill cria o plano com a tela aberta | aparece sozinho (tempo real) em ~1 s |
  | Atividade da etapa | "Lendo os repro steps do card" ao vivo na etapa em andamento (com "digitando…") |
  | Pausar pela tela | chip "Pausado", etapa em andamento fica pausada; `control` → exit 10 |
  | `wait` + Continuar pela tela | `wait` sai com 0 em 5,1 s ("retomado por Usuária Tela") |
  | Cancelar etapa pela tela (motivo) | etapa apagada/riscada com o motivo; `control` lista para pular |
  | `sync` de 5 arquivos | contadores Scripts 2 · Análises 1 · Dados 1 · Anexos 1 no rodapé |
  | Visualizador | SQL com destaque/linhas, markdown renderizado (tabela), imagem ajustada e 100 %, copiar/baixar |
  | Tela cheia | duas colunas; log novo chega ao vivo no painel da etapa |
  | Concluir | chip "Concluído", barra cheia; F5 → mesmo estado |
  | Sem sinal (último sinal há 12 min) | faixa "Sem sinal da skill há 12 min… `/analisar-bug 7012`"; some quando a skill volta a mandar |
  | Histórico (2 planos no card) | menu lista os dois; abrir o antigo mostra o antigo |
  | Limpar | painel volta a "Busque um card…" |
  | 1200 px e 390 px | layouts médio e celular ok |

  Nenhum erro de página vindo do plano (as falhas no console eram de endpoints que dependem do banco local vazio: `UserIntegration/status`, `PluginConfiguration`, `Azure/card`). Capturas em `../prform.api-0023/.t0023/shots/` (fora do git).
- **Para o Q1**: o deploy do backend aplica a migração `InitialExecutionPlans` pelo Job `--migrate` (schema novo `execution`, só criação de tabelas); o relay não muda (grupo novo). A skill já está instalada na máquina e, até o deploy, responde `PLANO INDISPONIVEL` e segue como antes. A `feature/0021` (em outra sessão) também mexe em `register.component.*` (rodapé) — conflito provável só de contexto.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0023` no backend e no front.
- 2026-09-28 — B1 concluída (`b4a5d9b`).
- 2026-09-28 — S1 concluída (`1bfb2f1`).
- 2026-09-28 — F1–F3 (front `16b350a`) e T1 concluídas; títulos padrão das etapas com acentuação.
- 2026-09-28 — Merge da `master` (0021) nas duas branches (conflito só de contexto no CSS da tela do card; teste no navegador repetido depois do merge). PRs acbonfim/prmaker.api#29 e acbonfim/prmakerweb#19 mesclados; deploys ok (backend run 36506598396 com a migração `InitialExecutionPlans`; front run 36507037055). Produção: `GET ExecutionPlan/card/<card>/current` → 204 e o bundle publicado traz a seção do plano.
