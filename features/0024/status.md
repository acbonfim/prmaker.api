# Status — Feature 0024 (Da análise à correção, chamados, PRs por repositório, Timeline completa, skills pelo PRMake)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0024` no backend (`../prform.api-0024`) e no front (`../solvace.prform.web/prform-app-0024`, a criar), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Modelo + API: fase análise/correção, dono/tipo/dependências/`waiting`, perguntas, links, ações do usuário; migração | 1 | — | ✅ concluída | Claude | `8d79fdf` |
| B3 | Skills no repositório (`skills/`), endpoints `Skills`, pacote, `install.sh` | 1 | — | ✅ concluída | Claude | `41663a6`, `8a84745` |
| B2 | Automação: PR mesclado → etapa/plano concluído; chamado resolvido → etapa concluída; registros na Timeline | 2 | B1 | ✅ concluída | Claude | `c7d995b`, `c7ec8e4` |
| S1 | `prmake-plan.sh`: `ask`/`wait-answers`/`answer`/`link`/`correction`, `control` estendido | 2 | B1 | ✅ concluída | Claude | `cc21076` |
| S2 | `SKILL.md` analisar-bug: propor soluções, perguntas, plano de correção, fluxo de branches, nunca merge | 2 | S1 | ✅ concluída | Claude | `cc21076` |
| S3 | Atualização automática das skills (`self-update`, hook `SessionStart`, `setup`) | 2 | B3 | ✅ concluída | Claude | `6a97f9c` |
| F1 | Abas Análise/Correção; dono, tipo, dependências e "aguardando" nas etapas | 2 | B1 | ✅ concluída | Claude | front `85fa31c` |
| F2 | Perguntas (faixa de destaque, opções, texto livre, tempo real) | 2 | B1 | ✅ concluída | Claude | front `85fa31c` |
| F3 | Links/chamados/PRs da etapa, status, ações do usuário (iniciar/concluir/pular) | 2 | B1, B2 | ✅ concluída | Claude | front `85fa31c` |
| F4 | Tela "Skills" (instalar, versão, download) | 2 | B3 | ✅ concluída | Claude | front `85fa31c` |
| T1 | Teste ponta a ponta local | 3 | todas | ✅ concluída | Claude | — |
| Q1 | PRs e deploy | 4 | T1 | ✅ concluída | usuário + Claude | api #30, web #20 |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- Decidido: chamados só com link + status manual e histórico por etapa (Q-a); revamp backend só `development` (Q-b). Hook de atualização instalado automaticamente (Q-c) — ver `plan.md` §4.
- Regra do usuário: **o Claude só abre PRs, nunca faz merge** (o plano conclui quando os PRs são mesclados por outra pessoa).
- **B1/B2** — testado com a API local (Postgres 18 isolado, porta 55434): perguntas deixam a etapa "aguardando" (contagem atualiza a cada resposta) e ela volta a andar na última; resposta pela skill fica `via claude`; plano de correção ligado à análise (origem de outro card recusada); chamado aberto → aguardando, fechado → aguardando novo chamado, resolvido → etapa concluída (histórico dos dois); dependências liberam etapas na ordem; PRs do card (simulados em `PullRequestsGithub`) anexados sozinhos à etapa do repositório — sem GitHub configurado vale o último status gravado —, etapa concluída com "2 PRs mesclados" e **plano de correção concluído sozinho**; Timeline com um registro por marco (perguntas, respostas, plano criado, etapas, chamado, PRs mesclados, resumo final) sempre com "Falta: …".
- **B3/S3** — instalação numa HOME temporária pelo comando da tela: 4 skills instaladas, `.venv` da analisar-bug com `pytds`, token `600`, hook `SessionStart` acrescentado sem apagar o `settings.json` existente (e sem duplicar ao reinstalar). Atualização: sem mudança fica calada; skill publicada de novo → atualiza e avisa "Releia a SKILL.md"; arquivo editado à mão → não sobrescreve (avisa; `--force` substitui); sem rede → sai calado. Contexto de build da imagem inclui `skills/` (verificado com `docker build`).
- **S1/S2** — no bash 3.2: perguntas respondidas uma pela tela e outra pelo terminal, `wait-answers` devolve as duas com a origem; `correction` cria o plano ligado; `link --blocks`; `control` mostra prontas/aguardando/perguntas; `use`; `start` retoma o plano de correção numa sessão nova. `open-pr` usa o mesmo endpoint da `gerar-prmake` (não testado contra o GitHub).
- **F1–F4/T1** — Chrome headless contra a API local: perguntas em destaque e respondidas pela tela; abas Análise/Correção (Correção por padrão); chamado anexado pela tela → etapa aguardando → marcado resolvido → concluída; etapa do usuário bloqueada até o PR e concluída pela tela; PR aparece na etapa assim que registrado; plano "Concluído" quando os PRs mesclam; diálogo "Skills do Claude" pelo menu do usuário. Sem erros de página do plano.
- **Não instalado ainda na máquina do usuário**: `~/.claude/skills` segue com a versão da 0023 (havia uma análise rodando). Depois do deploy, instalar pelo comando da tela *Skills* (substitui pelas versões do repositório e coloca o hook).

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktree `feature/0024` no backend. O link público do Freshservice redireciona para o login (testado) — status só via API com o número do chamado.
- 2026-09-28 — Decisões Q-a (chamado = link + status manual + histórico) e Q-b (revamp backend só `development`).
- 2026-09-28 — Decisão Q-c: o instalador coloca o hook `SessionStart` sem perguntar.
- 2026-09-28 — B1, B2, B3, S1–S3, F1–F4 e T1 concluídas. Falta o Q1 (PRs e deploy — merge pelo usuário) e instalar as skills pela tela depois do deploy.
- 2026-09-28 — PRs acbonfim/prmaker.api#30 e acbonfim/prmakerweb#20 mesclados (autorizado pelo usuário); deploys ok (backend run 36513753306 com a migração `AddCorrectionPlans`; front run 36514178585). Produção: `GET /Skills` lista as 4 skills; bundle publicado traz a tela Skills e as perguntas.
