# Feature 0023 — Plano de execução da skill analisar-bug no PRMake (tempo real, arquivos, pausar/continuar)

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0023` no backend (`../prform.api-0023`) e no front (`../solvace.prform.web/prform-app-0023`), a partir de `master`. A skill vive fora dos repositórios (`~/.claude/skills/analisar-bug`); uma cópia versionada fica em [`skill/`](./skill/).

## 1. Levantamento (2026-09-28)
- **Skill hoje**: cria `~/.claude/cards/<card>/{scripts,analises,dados}`, lê o card (`GET Azure/card/<card>`), investiga, grava a análise em markdown e publica **só no fim** na Timeline (`prmake-timeline.sh`). Token = api-key do usuário (`x-api-key`, `~/.claude/prmake-token.txt`). Nada do andamento chega ao PRMake.
- **Módulo Timeline** (modelo a seguir): domain/application/infra, `TimelineContext` no schema `timeline`, controller no host, tempo real "sinal + refetch" (`IRealTimeNotifier.NotifyGroupAsync("timeline:{card}", "timelineUpdated", {...})`). O relay (0013) aceita qualquer nome de grupo/evento — grupo novo não exige mudança nele.
- **Migrações**: `StartupMigrator` (`--migrate`, Cloud Run Job no deploy) aplica os contextos do host sob advisory lock — o contexto novo entra na lista.
- **Tela do card** (`register.component`): `.pr-content-row` = PRs (60 %) + `app-card-timeline` (40 %). A tela cheia da timeline (0020) move o próprio elemento para o `<body>` e anima a geometria; rodapé de 62 px (`timeline__footer`).
- **Cloud Run**: corpo de request até 32 MB; imagem/anexo de bug raramente passa de poucos MB.

## 2. Decisões
### 2.1 Banco: PostgreSQL (mesmo database, schema novo `execution`)
- Já é o banco de tudo (sem infra nem custo novo), transacional (plano, etapas, logs e arquivos consistentes entre si), entra no backup e nas migrações que já existem. Redis/Mongo exigiriam serviço novo, custo e backup próprio sem ganho real no volume esperado (dezenas de planos/dia).
- **Arquivos em `bytea`**, numa tabela separada do metadado (listar não carrega conteúdo). Limites: **10 MB por arquivo**, **50 MB por plano** (o `.zip` é montado em memória no Cloud Run). Se um dia o volume crescer, só o conteúdo migra para o Cloud Storage (a API não muda).

### 2.2 Modelo (módulo `Solvace.ExecutionPlans`, `ExecutionPlanContext`)
- **ExecutionPlan**: `Id` (Guid), `CardNumber`, `Kind` (`analisar-bug`), `Title`, `Summary`, `Status`, `StatusReason`, `StatusChangedBy`, `CreatedBy/CreatedByUserId`, `CreatedAt`, `StartedAt`, `FinishedAt`, `LastActivityAt` (heartbeat da skill), `UpdatedAt`.
- **ExecutionStep**: `Id`, `PlanId`, `Key` (slug estável, único no plano — upsert idempotente), `Order`, `Title`, `Description` (markdown: o que vai ser feito), `Status`, `StatusReason`, `Activity` (o que está fazendo agora, curto), `Checkpoint` (onde parou, para retomar), `StartedAt`, `FinishedAt`.
- **ExecutionLog** (os "pedaços"): `Id` bigint identity (ordem global), `PlanId`, `StepKey?`, `Kind` (`info|progress|finding|decision|warning|error`), `Message` (markdown, até 20 000 caracteres), `ClientId?` (único no plano → reenvio da fila local não duplica), `CreatedAt`.
- **ExecutionArtifact** + **ExecutionArtifactContent**: `Id`, `PlanId`, `StepKey?`, `Name` (único no plano → upsert), `Kind` (`script|analysis|data|image|attachment`), `ContentType`, `Size`, `Sha256`, `Description`, datas; conteúdo 1:1 na outra tabela.
- **Status do plano**: `pending → running → completed | failed | cancelled`; `running ↔ paused`. `completed` e `cancelled` são finais. **Status da etapa**: `pending | running | completed | failed | cancelled` (cancelada = pulada/cancelada, sempre com motivo).
- Regras no domínio: cancelar o plano cancela as etapas pendentes/em andamento (motivo "Plano cancelado por <nome>"); concluir o plano com etapas pendentes as cancela com "Não executada"; etapa `running` passa o plano de `pending` para `running`; atualização da skill num plano `paused` é aceita (logs/arquivos) mas **não** tira o plano da pausa.

### 2.3 API (`api/v1/ExecutionPlan`, `[Authorize]` x-api-key — a skill usa a api-key do usuário)
| Método | Rota | Uso |
|---|---|---|
| POST | `` | cria o plano (card, kind, título, etapas) — skill |
| GET | `card/{card}` | planos do card (resumo, mais recente primeiro) |
| GET | `card/{card}/current` | plano mais recente com etapas + arquivos (sem conteúdo) |
| GET | `{id}` | plano completo |
| PUT | `{id}/steps` | upsert da lista de etapas (a skill refina o plano depois de ler o card) |
| PATCH | `{id}/steps/{key}` | status/atividade/checkpoint/descrição de uma etapa — skill |
| POST | `{id}/steps/{key}/cancel` | usuário cancela (pula) uma etapa pendente, com motivo |
| POST | `{id}/logs` | lote de logs `[{clientId, stepKey, kind, message}]` — skill |
| GET | `{id}/logs?afterId=&stepKey=&limit=` | logs incrementais |
| POST | `{id}/status` | `{status, reason}` — usuário (pausar/continuar/cancelar) ou skill (concluir/falhar) |
| POST | `{id}/control` | heartbeat da skill; devolve status do plano + etapas canceladas (a skill decide se segue, espera ou para) |
| POST | `{id}/artifacts` | multipart (`file`, `kind`, `stepKey`, `description`) — upsert por nome |
| GET | `{id}/artifacts/{artifactId}/content` | conteúdo (`?download=true` → `attachment`) |
| GET | `{id}/artifacts/zip` | todos os arquivos num `.zip` (pastas por tipo) |
| DELETE | `{id}/artifacts/{artifactId}` | remove um arquivo |

- **Tempo real**: grupo `execplan:{card}`, evento `executionPlanUpdated` `{ cardNumber, planId, action, stepKey }` (`created|steps|step|log|status|artifact`), best-effort como a timeline. O front refaz o GET (sinal + refetch); logs vêm incrementais (`afterId`).

### 2.4 Pausar / continuar / parar e "nunca se perder"
- **Pausar (PRMake)** → plano `paused`. A skill checa `control` entre etapas (e em pontos longos): se pausado, roda `prmake-plan.sh wait` (bloqueia consultando com backoff, mantendo o heartbeat) até alguém clicar **Continuar** (→ `running`, a skill segue) ou **Cancelar**.
- **Cancelar** → plano `cancelled`; a skill para e informa no terminal. **Cancelar etapa** → a skill pula aquela etapa.
- **Heartbeat**: toda chamada da skill atualiza `LastActivityAt`. Plano `running`/`paused` sem sinal há > 5 min → a tela avisa "Sem sinal da skill há X min" com o comando para retomar (`/analisar-bug <card>`), copiável.
- **Retomar** (sessão caiu, outra máquina, dia seguinte): a skill, ao iniciar, busca `card/{card}/current`; se houver plano não finalizado, **retoma o mesmo plano** (etapas concluídas ficam, continua na primeira não concluída usando o `checkpoint` e baixando os arquivos já enviados).
- **Queda de rede na skill**: cada envio tem 3 tentativas com backoff; se falhar, vai para uma fila local (`<card>/.prmake-outbox.jsonl`) reenviada na próxima chamada (`ClientId` evita duplicar logs; arquivos são upsert por nome). O id do plano fica em `<card>/.prmake-plan.json`.
- **Queda no navegador**: o `_resynced` do WsService recarrega o plano; enquanto o plano está ativo há também uma consulta de segurança a cada 30 s (caso o WS esteja fora).

### 2.5 Front
- Componente `app-execution-plan` (standalone, signals — app zoneless), entre PRs e timeline. Layout: 3 colunas (PRs · Plano · Linha do tempo) em telas largas; até 1280 px, PRs e Plano empilhados à esquerda e a timeline à direita; ≤ 900 px tudo empilhado (como hoje).
- **Cabeçalho** (56 px, igual ao da timeline): ícone, "Plano de execução", **chip de status animado** (em andamento: ponto pulsando + anel; pendente: ampulheta; pausado: ícone de pausa piscando lento; concluído: check com "pop"; cancelado: riscado/apagado; falhou: vermelho), progresso `n/total` com barra fina, menu de ações (Pausar/Continuar/Cancelar, Histórico de planos) e botão de tela cheia.
- **Etapas**: trilha vertical (stepper). Nó com ícone por status (✓ concluída, anel girando em andamento, número pendente, ✕ cancelada, ! falhou), conector que "enche" ao concluir, etapa em andamento com brilho e a **atividade atual** ao vivo. Cancelada: `opacity .45`, título riscado, tooltip com o motivo. **Hover** → tooltip com a descrição; **clique** → expande detalhes (descrição em markdown, onde parou, logs ao vivo da etapa, arquivos da etapa, "Cancelar etapa" se pendente).
- **Tela cheia**: o mesmo mecanismo da timeline (elemento vai para o `<body>` e cresce animado), com duas colunas: etapas à esquerda e detalhes/logs da etapa selecionada à direita. Helper reutilizável `FullscreenPanel` (a timeline pode migrar para ele depois).
- **Rodapé** (mesma altura do da timeline): botões **Scripts · Análises · Dados · Anexos** com contador, e **Baixar tudo (.zip)**. Cada um abre o **visualizador de arquivos** (dialog): lista à esquerda, prévia à direita — SQL/código em monoespaçado com números de linha, markdown renderizado, JSON formatado, imagem com zoom (ajustar/100 %/arrastar); ações **Copiar**, **Baixar** (`.sql` etc.), abrir imagem em nova aba. Conteúdo baixado com o `HttpClient` (x-api-key) → blob.

### 2.6 Skill
- Script novo `scripts/prmake-plan.sh` (bash + curl + jq): `start`, `steps`, `step`, `log`, `checkpoint`, `upload`, `sync`, `status`, `control`, `wait`, `resume-info`, `flush`.
- `SKILL.md`: passo 0 cria/retoma o plano (etapas padrão: identificar card · buscar dados · investigar código · consultar ambiente (opcional) · causa raiz · montar análise/scripts · publicar); depois de ler o card, refina as etapas; a cada etapa: `step running` → `log` em pedaços → `upload`/`sync` dos arquivos → `checkpoint` → `step completed`; `control` entre etapas; ao corrigir (quando pedido), adiciona etapas de correção. A publicação na Timeline continua.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | B1 (módulo + API + migração), S1 (script e SKILL.md da skill) |
| 2 | F1 (serviço + componente do plano + tela cheia), F2 (visualizador de arquivos), F3 (layout da tela do card) |
| 3 | T1 (teste ponta a ponta local: Postgres do docker + API + skill contra a API local + front no navegador) |
| 4 | Q1 (PRs e deploy — usuário) |

- **B1**: projetos domain/application/infra, `ExecutionPlanContext` (schema `execution`), migração `InitialExecutionPlans`, controller, registro no host/sln/`StartupMigrator`, limites de tamanho.
- **S1**: `prmake-plan.sh` com fila local e retentativas; `SKILL.md` atualizado; cópia em `features/0023/skill/`.
- **F1–F3**: ver 2.5.
- **T1**: criar plano pela skill contra a API local → ver na tela em tempo real; pausar pela tela → `wait` bloqueia; continuar → segue; cancelar etapa → skill pula; cancelar plano → etapas canceladas com motivo; derrubar a API no meio → fila local e reenvio; F5/queda do WS → a tela volta ao estado certo; arquivos: SQL (copiar/baixar `.sql`), imagem (prévia/zoom), markdown, zip.
