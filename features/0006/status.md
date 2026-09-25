# Feature 0006 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0006` em `prform.api` (backend) e `solvace.prform.web/prform-app` (front), a partir de `master`.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|
| B1 | Descrição sem limite de 2000 (coluna longtext + limite 100.000) | back | — | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | 4df53f7 |
| F1 | Mensagem de erro ao salvar registro | front | — | ✅ | Claude (sessão principal) | 2026-09-24 | 2026-09-24 | front 0fe0187 |
| Q1 | Publicação, teste real e recuperação dos registros 85/86 | ambos | B1, F1 | ⬜ | | | | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

### B1 — Descrição sem limite de 2000 ✅ (4df53f7)
- `TimelineContext`: `Description` passa a `longtext` (sem `HasMaxLength`). Migração **`20260924170858_TimelineDescriptionLongText`** (`TimelineContext`). SQL conferido com `dotnet ef migrations script`: só `ALTER TABLE TimelineEntries MODIFY COLUMN Description longtext CHARACTER SET utf8mb4 NOT NULL` — **amplia o tipo, sem perda de dados**. Aplicada no deploy pelo `StartupMigrator`. O `Down` volta a `varchar(2000)`; por isso o EF avisa "pode perder dados", mas o aviso vale só para um rollback.
- Gerada com factory de design-time temporária (MySQL 8.0.36 fixo, connection string fictícia, sem conectar no banco), removida em seguida.
- `TimelineEntry.MaxDescriptionLength = 100_000`: acima disso, `DomainException` "A descrição pode ter no máximo 100.000 caracteres (enviados: N)." (números em pt-BR). Vale para criar e editar. Registros antigos não passam pela validação ao ler do banco (o EF usa o campo `_description`).
- Importação do Teams: corte no novo limite, no lugar de 2000.
- `TimelineController` Create/Update: `DomainException` → **400 `{ error }`** (antes virava erro genérico no `ExceptionHandlerMiddleware`).
- Validação: build da API ok. Teste descartável (entidade + modelo EF): **15/15** — 2000/2001/6099/100.000 salvam inteiros; 100.001 é recusado com a mensagem; trim antes do limite; edição acima do limite recusada mantendo o texto; importado com 2500; coluna `longtext` sem max length no modelo.

### F1 — Mensagem de erro ao salvar registro ✅ (front 0fe0187)
- `card-timeline`: erro ao criar ou editar → snackbar com `error`/`message` do backend (ou mensagem genérica), por 8 s. O texto digitado continua no campo. Antes o erro era ignorado em silêncio.
- `ng build` ok. Não testado no navegador.

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0005 mergeada (#10 back, #6 front). `feature/0006` criada a partir de `master` nos dois repos. Causa confirmada: `varchar(2000)` + MySQL sem modo estrito (corte silencioso). Plano criado; B1 iniciada. |
| 2026-09-24 | B1, F1 | Concluídas (4df53f7, front 0fe0187). Falta Q1: deploy (aplica a migração), teste real e regravar os registros 85/86. |
