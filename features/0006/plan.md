# Feature 0006 — Comentários grandes na linha do tempo sem truncar

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0006` nos dois repos, a partir de `master` (com 0001–0005).

## 1. Análise (causa confirmada)
- **O texto é cortado ao salvar, não na tela.** A coluna `TimelineEntries.Description` é **`varchar(2000)`** (`solvace.timeline.infra/Contexts/TimelineContext.cs:22`, `HasMaxLength(2000)`; migração `InitialTimelineModule`).
- **Criação (`POST /Timeline`: tela, skills `prmake-timeline`/`analisar-bug`):** o código não limita o tamanho, mas o MySQL do MonsterASP roda **sem modo estrito**. Por isso ele **corta calado** o que passa de 2000 caracteres, em vez de dar erro. Os dois registros da evidência (ids 85 e 86, card 74229) terminam exatamente no caractere 2000 (`…com \`U`, `…registrada e`).
- **Importação do Teams:** corta de propósito em 2000 (`TimelineApplication.cs:73`, "Respeita o limite da coluna").
- **Edição (`PUT`):** mesmo corte silencioso da criação.
- **Front:** não limita tamanho (sem `maxlength`), e a renderização em markdown (0004) exibe o texto inteiro. Porém os erros de criar/editar são **ignorados em silêncio** (`card-timeline.component.ts`, `error: () => {…}`).
- **O que já foi perdido não volta pelo banco.** Os dois registros de exemplo têm o texto completo guardado localmente pela skill `analisar-bug` (`~/.claude/cards/74229/analises/analise-inicial-v1-legado.md` → id 85, `analise-inicial.md` → id 86) e podem ser regravados depois do deploy (Q1).

## 2. Solução
- **Coluna `longtext`** (até 4 GB no MySQL). A migração `TimelineDescriptionLongText` faz `ALTER … MODIFY Description longtext NOT NULL`, **só amplia o tipo, sem perda de dados**, e é aplicada no deploy pelo `StartupMigrator`, como as demais.
- **Limite na aplicação:** `TimelineEntry.MaxDescriptionLength = 100_000` caracteres (~16× a maior análise atual, ~6 KB). Acima disso, `DomainException` com mensagem clara ("A descrição pode ter no máximo 100.000 caracteres") em vez de corte silencioso. Vale para criar e editar.
- **Importação do Teams:** continua cortando para não travar a importação, mas no novo limite (100.000).
- **Front:** ao falhar criar/editar, mostra a mensagem do backend (snackbar) em vez de não fazer nada; o texto digitado continua no campo.
- Contrato do endpoint **não muda**.

## 3. Fases

### B1 — Descrição sem limite de 2000 (backend)
- [x] `TimelineEntry`: `MaxDescriptionLength = 100_000` + validação em `SetDescription`.
- [x] `TimelineContext`: `Description` → `longtext`, sem `HasMaxLength`.
- [x] Migração `TimelineDescriptionLongText` (factory de design-time temporária, sem conectar no banco; removida depois).
- [x] Importação do Teams: corte no `MaxDescriptionLength`.
- [x] `TimelineController` Create/Update: `DomainException` → **400 `{ error }`** (antes caía no middleware genérico como erro de servidor).
- [x] Build + SQL da migração conferido (`dotnet ef migrations script`).

### F1 — Mensagem de erro ao salvar registro (front)
- [x] `card-timeline`: erro de criar/editar → snackbar com a mensagem do backend.
- [x] `ng build`.

### Q1 — Publicação e recuperação (usuário)
- [ ] Merge → deploy aplica a migração.
- [ ] Teste: registro com mais de 2000 caracteres salva e aparece inteiro.
- [ ] Regravar os registros 85 e 86 com os textos completos (com autorização do usuário — escrita em produção).
