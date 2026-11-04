# Feature 0075 — Análise legível (fotografia de código, diff anotado, mocks) + Situação do card

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0075` a partir da `master` (worktree `../prform.api-0075`; no front, worktree
> `../solvace.prform.web/prform-app-0075` com `node_modules` linkado de `../prform-app-0019/node_modules`).

| Repo | Caminho | Sigla |
|---|---|---|
| Backend | `prform.api` (módulo `Solvace.ExecutionPlans`, host `solvace.prform.api`) | `B*` |
| Frontend | `../solvace.prform.web/prform-app` | `F*` |
| Skills | `skills/analisar-bug`, `skills/gerar-handover` (no back) | `S*` |

**Entrega em dois PRs por repo**, cada um independente em produção:
- **PR-A — análise legível**: B1, B2, B3, F1, F2, F3, S1, S2, S3.
- **PR-B — situação do card + QA**: B4, B5, B6, B7, F4, F5, S4.

---

## 1. Análise (o que já existe e vamos reusar)

- **Plano**: `ExecutionPlan`/`ExecutionStep`/`ExecutionArtifact`/`ExecutionLink` em
  `CIME/modules/Solvace.ExecutionPlans/src/solvace.executionplans.domain/Entities/`; contexto `ExecutionPlanContext`
  (schema `execution`); aplicação `ExecutionPlanApplication*.cs` (Automation: `SyncPullRequestsAsync`,
  `WriteMilestonesAsync`, `RemainingText`); controller `ExecutionPlanController` (`api/v1/ExecutionPlan`); MCP
  `Execution/PrmakeMcpTools.cs` (regra de `advance` da 0063 = modelo para a regra de citações).
- **Arquivos**: `UploadArtifactAsync` (10 MB/arquivo, 50 MB/plano, tipo `image` inferido pela extensão), conteúdo em
  `ExecutionArtifactContents` (`bytea`); `GET {id}/artifacts/{aid}/content`.
- **DevOps**: `DevOpsActionsService` (`dev-test-in-qa`, `ready-for-qa`), `Execution/DevOpsActionRunner.cs` (caminho do
  MCP), `QaValidatedAsync`; chaves `BugDevTestInQaState/Column` (seed `20260930200000`).
- **Branches**: `BranchStrategy` (Skills Configurations) — PR `-qa → qa` nos fluxos `legacy`/`revamp-frontend`.
- **Config**: `SkillsConfigurationKeys` + migração de seed (só acrescenta o que falta).
- **Front**: `plan-markdown.pipe.ts`, `timeline-markdown.pipe.ts` (marked puro), `cc-popover` (CDK), `git-diff-viewer`
  (`parsePatch`), `plan-files-dialog` (imagem por blob, `highlightSql`), `execution-plan.component` (bloco "Aguardando
  você", `PlanHeadline`), `register.component` (`infoFacts`), `home-cards`, `handover-dialog`.
- **Realtime**: eventos do plano (`ExecutionPlanRealTimeEvents`), grupo do card.

### Pontos de atenção
1. **Sanitizer do Angular** remove `data-*`/`id`/`style` do `[innerHTML]`; mantém `class`, `title`, `href`. Os links de
   ref serão `<a class="code-ref" href="#ref/<id>">` e o clique é delegado no contêiner. O handover usa
   `SafeHtmlPipe` (bypass) — lá o link vira `<code>` simples.
2. `marked.setOptions` é global e chamado em 3 lugares — as extensões vão numa instância própria (`new Marked()`), sem
   mexer nas globais.
3. **Performance (0070)**: lista de refs só com cabeçalhos (projeção sem `Snapshot/Proposal`); conteúdo por ref, sob
   demanda, com cache no front; limites de tamanho no domínio.
4. `prmake_file` só aceita texto → imagens sobem pelo `prmake-plan.sh upload` (multipart). Não mandar base64 pelo modelo.
5. Sincronização de PR só roda em requisição → Dev Test in QA automático nos gatilhos da spec (B6), idempotente.
6. **Migrações** de `ExecutionPlanContext` (tabela nova + colunas) e de `DefaultContext` (seed de config) rodam no
   deploy (advisory lock, fatal se falhar) — testar no Postgres local antes.
7. Skills no Windows (Git Bash): bloco "Windows/Git Bash (0035)" em todo script novo com `python3`/`jq`; Chrome no
   Windows por `PROGRAMFILES`.

---

## 2. Fases

### Onda 1 (paralelas)
**B1 — Code refs: modelo e API** · entidade `ExecutionCodeRef` (`PlanId`, `CardNumber`, `Ref` slug ≤ 60, `Repository`,
`Path`, `CommitSha`, `Branch`, `StartLine`, `EndLine`, `FirstLine`, `Language`, `Change`
`change|create|remove|suspect|read`, `Title` ≤ 200, `Why` ≤ 2000, `Snapshot`, `Proposal`, `Implemented`,
`Annotations` jsonb `[{side, from, to, text}]`, `CreatedAt/UpdatedAt`), único `(PlanId, Ref)`, índice `(CardNumber,
Ref)`; validações (limites da spec D2, `why` obrigatório fora de `read/suspect`, `proposal` em
`change/create/remove`, anotações dentro do trecho); migração. Endpoints: `PUT {id}/code-refs/{ref}` (upsert, executor),
`PATCH {id}/code-refs/{ref}` (anotações/proposta/implemented), `DELETE`, `GET {id}/code-refs` (cabeças),
`GET card/{card}/code-refs` (cabeças de todos os planos do card, a mais recente por ref) e
`GET card/{card}/code-refs/{ref}` (completa; prefere o plano atual, depois o pai, depois o mais recente). Config
`CodeRefContextLines`/`CodeRefMaxLines` em Skills Configurations (seed). MCP `prmake_code_ref` (cria/ajusta sem
`snapshot` obrigatório: `why`, `annotations`, `proposal`, `change`) e `prmake_plan` listando as cabeças. Realtime
`codeRef`. Testes de domínio (limites, validação).

**F1 — Markdown com extensões** · `helpers/markdown-ext.ts`: instância `new Marked()` com (a) callouts `> [!TIPO]`,
(b) links `ref:<id>` → `<a class="code-ref code-ref--<change>" href="#ref/<id>" title="…">`, (c) **auto-link** de
`caminho:linha(-fim)` em código inline e texto (regex de caminho com extensão conhecida) → `href="#cite/<caminho>:<linhas>"`,
(d) imagens `anexo:<nome>` → `<img class="md-attachment" alt title="anexo:<nome>">` (src vazio, resolvido no F3).
`planMarkdown`/`timelineMarkdown` usam o helper; handover público sem (b)–(d). Testes do helper (casos do item 1.6,
texto antigo igual, XSS).

**S2 — `coderef` no script** · `prmake-plan.sh coderef <card> <ref> --repo <r> --file <caminho> --lines A-B
[--change …] [--title …] [--why …] [--after <arquivo com o trecho novo>] [--notes <json>] [--phase …]` →
`scripts/coderef.py` (lê na cópia da master `revamp-repos.sh master`, recorta foco ± contexto do config, `difflib`
para a proposta, linguagem pela extensão, `git rev-parse HEAD`), `PUT code-refs`. `--implemented <sha>` (correção):
`git show <sha> -- <caminho>`. Bloco Windows/Git Bash. Usa o contrato de B1 (testar com B1 local).

### Onda 2
**F2 — Popover da ref** · `components/code-ref/`: `code-ref-popover.component` (cabeçalho, por quê, abas
Proposta/Atual/Feito, anotações numeradas com destaque cruzado, copiar, GitHub, expandir em `MatDialog`, folha
inferior em tela estreita) + `code-ref-host.directive` (delegação de clique em `a.code-ref`/`a.code-cite` no contêiner;
overlay CDK `flexibleConnectedTo(elemento)`) + `code-ref.service` (cabeças por card em cache, conteúdo por ref,
realtime). Diff reaproveitando o `parsePatch` do `git-diff-viewer` (extrair para helper). Highlight: `highlight.js/lib/core`
com as linguagens da spec por `import()` dinâmico. Citação sem ref (`#cite/`) → casa com a cabeça (fim do caminho +
sobreposição de linhas) ou mostra "sem fotografia". Aplicar a diretiva na Timeline, plano (resumo, descrição,
checkpoint, logs, perguntas), comentários, prévia `.md` dos arquivos.

**F3 — Imagens no markdown** · resolver `img.md-attachment` pelo nome nos artefatos do plano/card (B3) → blob
autenticado → object URL (revogar ao destruir); clique abre zoom (reuso da prévia do `plan-files-dialog`); imagens
consecutivas no mesmo parágrafo lado a lado (CSS), legenda pelo `alt`.

**B2 — Regra das citações** · `CodeCitations.Extract(markdown)` (mesma regra do F1, em C#) e conferência contra as refs
do card: `prmake_advance` de `montar-analise`/`propor-solucoes` (e `PATCH steps` para `completed` nessas keys) recusa
com a lista de citações sem fotografia e refs sem `why`/`proposal`; texto lido = arquivos `analysis` da etapa + checkpoint.
Plano antigo/sem citações → passa. Testes.

**B3 — Arquivos por nome + `prmake_attachment`** · `GET card/{card}/artifacts/by-name/{name}/content` (procura no plano
atual e no pai; mesmos headers do `content`); `prmake_attachment` passa a achar arquivos do plano (não só de
comentários). Testes.

**S1 — Modelo da análise e das soluções** · reescrever `references/analise-template.md` (passo 4) e o passo 6 de
`correcao.md` com o formato A1 (resumo em callout, *O que vai ser feito* em tabela com refs, *Onde parou*, ✅/❓,
seções fixas), a regra "toda citação de código = `coderef` antes de publicar" e o `why`/anotações (por que este
código, por que faz sentido). Checkpoint de `propor-solucoes` cita refs por id. SKILL.md: linha na tabela de comandos.

**S3 — Mocks visuais** · `scripts/mock-shot.sh <html> <png> [--width --height --mobile]` (Chrome/Chromium/Edge headless,
macOS/Linux/Windows) + seção `ref.sh analise mock`: quando fazer (mudança visível), receitas por mundo (legado ASP:
markup + CSS do tema limitless da master; revamp Angular: template do componente + estilos globais compilados com
`npx sass`), dados fictícios, destaque da mudança, upload (`upload … image`, + HTML-fonte), embed `anexo:` lado a lado,
legenda de simulação.

### Onda 3 (PR-B)
**B4 — Situação do card** · `CardSituationService` (aplicação de ExecutionPlans): a partir do plano atual (correção, ou
análise), links, etapas, perguntas, card do DevOps (`IExecutionIntegrations`) → `CardSituationResponse { kind
(ticket|pr-review|qa-validation|answer|user|none), since, title, facts[], prs[], tickets[], devops{state,column},
qa{movedAt, checklist}, handoff, next, markdown }`. Campo `Handoff` (≤ 8k) + `HandoffAt` no `ExecutionPlan` (migração);
MCP `prmake_handoff(card, text)`; `GET card/{card}/situation`; realtime `situation`; Timeline: entrada por **mudança de
`kind`** (substitui o "⏳ Etapa aguardando" de `ticket`/`pr`; guarda `SituationKind`/`SituationAt` no plano). Resumo
na Home (`HomeCardPlan.situation`). Testes por tipo.

**B5 — Revisões dos PRs** · `IGitHubService.GetPullRequestReviewsAsync` (aprovações, alterações pedidas, revisores
pendentes) chamado no `SyncPullRequestsAsync` para PRs abertos (cache curto, como o CI); `ExecutionLink.ReviewState`,
`Approvals`, `ReviewersPending` (migração). Entra no B4.

**B6 — Dev Test in QA automático + Ready for QA na conclusão** · reconhecer PR de QA (destino casa com o `qa` do
`BranchStrategy` ou `QaBranchPattern`, seed `qa`); no `SyncPullRequestsAsync`, PR de QA `merged` + `validar-qa`
pendente + `AutoDevTestInQa` (seed `true`) + `DevTestInQaAt` nulo → `DevOpsActionsService` com a integração do dono do
plano, grava `DevTestInQaAt` (migração), Timeline, `validar-qa` com o usuário; erro → aviso no plano. Gatilho extra:
heartbeat do executor varre planos de correção com PR aberto (no máximo a cada 5 min por instância). `POST
{id}/steps/validar-qa/complete` aceita `moveReadyForQa: true` → `ready-for-qa` na hora (mesma regra do
`QaValidatedAsync`). Testes (idempotência, branch não-QA, config desligada).

**B7 — Publicar a situação no DevOps (manual) + handover** · `POST card/{card}/situation/publish` → comentário na
discussion pelo PRMake (integração do usuário), Timeline; `GET Handover/...` sem mudança — a skill `gerar-handover`
inclui a situação (S4). *Depende da decisão D1.*

**F4 — Cartão "Situação atual"** · componente `card-situation` no topo do plano (tipo, há X, fatos, PRs com
aprovações/CI, chamado, Dev Test in QA, roteiro de QA, handoff com refs/links via F1/F2, próximo passo, "Copiar
resumo", "Publicar no DevOps" se B7); fato no `infoFacts` do card; chip na Home; realtime. Estados vazio/erro.

**F5 — Concluir "Validar em QA"** · ao concluir `validar-qa`, diálogo com "Mover para Ready for QA agora" (marcado) →
B6; mostrar o resultado (movido / erro com motivo).

**S4 — Skill: handoff e QA** · ao entrar em espera (passo 8 "sem nada pronto do seu lado", chamado, PR, QA):
`prmake_handoff` com o contexto da passagem; `fechar-card`/`validar-qa` alinhados ao B6 (o PRMake move o Dev Test in
QA; a skill só confere); `gerar-handover` lê `GET card/{card}/situation` e põe a seção "Situação atual"; versões das
skills (`skill.json`).

### Onda 4
**Q1 — Integração e e2e local** (`.t0075/`, fora do git) · Postgres 18 docker isolado + auth falso + api-key dev:
card com refs (o exemplo do item 1.6 com arquivos reais do legado), proposta e anotações, Antes × Depois; plano com
chamado aberto, PR aberto (aprovação), PR de QA mesclado → Dev Test in QA, conclusão com Ready for QA (DevOps falso).
Chrome headless: prints do popover (desktop/celular), do cartão de situação e da Timeline. Regressão: textos antigos
iguais, handover público, memória/tempo (Server-Timing) das rotas do plano.

**Q2 — Produção** · deploy pelos PRs; primeira análise real com o fluxo novo; ajustar textos da skill pelo resultado.

---

## 3. Ondas e dependências

| Onda | Fases | Depende de |
|---|---|---|
| 1 | B1, F1, S2 | — (S2 testa com B1 local) |
| 2 | F2, F3, B2, B3, S1, S3 | B1 (F2, B2, S1), F1 (F2, F3), B3 (F3) |
| 3 | B4, B5, B6, B7, F4, F5, S4 | B4 ← B5; F4 ← B4, F1/F2; F5 ← B6; S4 ← B4, B6 |
| 4 | Q1, Q2 | todas |
