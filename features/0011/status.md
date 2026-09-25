# Status — Feature 0011 (Menu "Ações DevOps")

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0011` no backend e no front (`../solvace.prform.web/prform-app`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | Configuração por campo nos plugins (`FieldSettings`) | 1 | — | ✅ concluída | Claude | `99651bf` |
| B2 | Campos da 0011 no "AI Configurations" (migração) | 2 | B1 | ✅ concluída | Claude | `99651bf`, (ajuste handover oculto) |
| B3 | Resumo não técnico (colunas + endpoint + comentário) | 2 | — | ✅ concluída | Claude | `99651bf`, `221a2eb` |
| B4 | Ações no DevOps (endpoints + config efetiva) | 2 | B1, B2 | ✅ concluída | Claude | `99651bf` |
| F1 | Rótulos e campos opcionais em "Minhas integrações" | 2 | B1 | ✅ concluída | Claude | front `c7d6ec2` |
| F2 | Botão e menu "Ações DevOps" | 3 | B4 | ✅ concluída | Claude | front `469c547` |
| F3 | Editor do resumo não técnico | 3 | B3, F2 | ✅ concluída | Claude | front `469c547` |
| S1 | Skill `gerar-prmake` salva o resumo pela API | 3 | B3 | ✅ concluída | Claude | fora do repo (`~/.claude/skills/gerar-prmake`) |
| Q1 | Publicação e teste | 4 | todas | ⬜ pendente | usuário | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Decisões
- Estimativa inicial: habilita só quando o usuário salvou os 3 valores (6/6/0 aparecem como sugestão), "igual às integrações" (usuário, 2026-09-25).
- Card US: o menu abre com o aviso de que as ações de US virão depois (usuário, 2026-09-25).
- Resumo: ver, editar, salvar e gerar novamente com IA. Salvar grava no banco e cria **ou atualiza** o mesmo comentário da discussion (`SummaryCommentId`) (usuário, 2026-09-25).
- Configuração no "AI Configurations" (spec). O plugin vira pessoal e opcional, com todos os campos pessoais opcionais, para não bloquear a IA.

## Notas de handoff
- Banco de dev = produção: gerar as migrações com a factory temporária (`MySqlServerVersion(8.0.36)` + connection string fictícia) e removê-la depois. Não rodar a API local nem `database update`.
- Commitar só os arquivos da fase (`git commit -- <arquivos>`). No front, `src/environments/environment.ts` é alteração local do usuário e não deve ser commitado.
- O app é zoneless: nos componentes antigos, `cdr.detectChanges()`; nos novos, signals.

## Notas da implementação
- **Migrações** (`AddPluginFieldSettings`, `AddDevOpsActionsToAIConfigurations`, `AddPullRequestSummary`) validadas num MySQL 8.0 descartável local (porta 33911, dados no scratchpad): subir → descer → subir de novo ok; chave já existente no JSON mantida; área com barra invertida única; plugin do Teams intocado; `Options` → longtext.
- **"AI Configurations" vira pessoal + opcional**: `get-all-by-id?id=3` continua devolvendo os campos fixos (PromptBug/PromptUS/TemplatePassagemConhecimento) com o valor global. Campos fixos marcados `hidden` não aparecem em "Minhas integrações"; **outras chaves fixas que existirem no plugin de produção aparecem lá como somente leitura** — ocultar depois, se incomodar (FieldSettings).
- **Tempo real**: salvar o resumo emite `summary-saved` (a tela atualiza só o resumo, sem o aviso de "card atualizado em outro lugar").
- **Skill**: resumo agora vai por `POST /PullRequest/{card}/summary` (grava no PRMake e atualiza o mesmo comentário). Resposta 404 (API sem a 0011) cai no `azure-comment.sh` antigo; `LEGACY_COMMENT=1` força o caminho antigo. Backup dos arquivos originais no scratchpad da sessão.
- **Não testado de ponta a ponta** (a API local aponta para o banco de produção): as chamadas ao DevOps (PATCH de campos, comentários) e as telas só foram compiladas — validar no Q1.

## Ajustes pós-teste (branch `feature/0011-ajustes`)
1. **"Salve o card antes" não liberava após salvar**: o `savePullRequest` não guardava o `id` do registro recém-criado na tela; agora guarda (e o menu também olha `prState.register()`).
2. **Resumo: salvar ≠ publicar + tela de contexto**: `POST PullRequest/{card}/summary` ganhou `publish` (padrão `true`, usado pela skill); `false` só grava no PRMake. Colunas `SummaryUpdatedAt`/`SummaryPublishedAt` (migração `AdjustPullRequestSummary`, com backfill dos já publicados). O diálogo tem as abas **Resumo** (visualizar/editar, gerar de novo, **Salvar**, **Publicar/Atualizar na discussion**, status salvo/publicado/desatualizado) e **Contexto enviado à IA** (situação do card, problema, discussion, timeline, PRs, descrição, RC, commits das branches dos PRs com diff — marcáveis — e o prompt final copiável).
3. **IA inventando correção**: o novo `BugSummaryPrompt` usa `{context}` e regras de veracidade (só diz que foi corrigido com evidência: PR, descrição, RC ou diff; sem isso, "em análise"). O contexto traz `Evidência de solução: SIM/NÃO` e a tela avisa quando não há. A migração só troca o prompt se ainda for o padrão anterior; modelo customizado sem `{context}` recebe o contexto + a regra no fim.
- Migração validada no MySQL descartável: troca do prompt padrão, `Down` restaura, prompt editado pelo admin preservado, backfill das datas.

## Log
- 2026-09-25 — Planejamento: `plan.md` e `status.md` criados; branches `feature/0011` criadas nos dois repos.
- 2026-09-25 — B1–B4, F1–F3 e S1 implementadas; backend e front compilando. Falta o Q1 (publicação e teste).
- 2026-09-25 — Ajustes pós-teste (liberação do resumo após salvar, salvar ≠ publicar, aba de contexto, prompt sem invenção) em `feature/0011-ajustes`.
