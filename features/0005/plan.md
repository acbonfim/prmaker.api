# Feature 0005 — Filtros na lista de Pull Requests (tela de register)

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0005` nos dois repos, a partir de `master` (com 0001–0004). **Só o front muda.**

## 1. Análise
- A lista de PRs do card é o `components/github-pr-list` (p-orderList), dentro do `app-card-panel` "Pull Requests" do `register.component.html`. Os dados já vêm todos do `GET /PullRequest/{card}/github` (`prState.githubPrs()`), com `status` (`OPEN`/`MERGED`/`CLOSED`/`LEGACY`), `branchPrefix`+`branchName` e `repositoryId` → dá para filtrar **no front**, sem mudar backend nem endpoint.
- **Altura dos cabeçalhos (spec 2):** o da linha do tempo tem o botão "Importar do Teams" (`mat-button`, 40px) → ~64px; o do painel de PRs tem um botão de ícone compacto → ~50px (48px sem card). Os dois usam `padding: 12px 16px` e crescem conforme o conteúdo.
- **Componente de referência (spec 4):** `ComandaCerta.App/src/app/shared/filter-bar` (`cc-filter-bar` + `filter-bar.models.ts`), que usa o `shared/popover/cc-popover` (CDK Overlay) e os tokens de tema `--cc-*`. Uma caixa por filtro com os selecionados como chips (colapsa em "+K"), popover com busca, "Selecionar todos" e opções com checkbox; em tela de celular, um botão único com todos os filtros. Os valores de cada filtro vêm de um `FilterProvider` (`search(term)`).
- O CIME é só tema escuro; os valores escuros dos tokens do ComandaCerta já batem com a paleta do CIME (ex.: `--surface-2`/`--cc-surface-2` = `#2e343f`, `--surface-input`/`--cc-surface-input` = `#1a1f28`). `@angular/cdk` já é dependência.

## 2. Solução
- **Cópia fiel** do `cc-filter-bar`, `filter-bar.models.ts` e `cc-popover` para `components/filter-bar/` e `components/popover/` (mesmo template, CSS, comportamento e seletores). Tokens `--cc-*` definidos no `styles.scss` com os valores escuros do ComandaCerta.
- **Ajuste mínimo na cópia:** ao abrir um filtro, as opções são recarregadas com o termo atual. No original a busca inicial roda só na primeira abertura, e aqui a lista de PRs muda: troca de card, novo PR aberto ou atualização do status.
- **Filtros (spec 5):** Status, Branch e Repositório, todos de múltipla escolha. Dentro de um filtro vale **OU**; entre filtros vale **E**.
- **Valores possíveis (specs 3 e 6):** calculados dos PRs **do card aberto**, sem valores fixos. Status na ordem OPEN, MERGED, CLOSED, LEGADO; branch = `prefixo+nome`; repositório = `repositoryId`. Aparecem já carregados ao abrir, e a busca filtra essa lista.
- **Onde:** barra de filtros no topo do corpo do painel "Pull Requests", acima da lista. Aparece quando o card tem PRs; se nenhum PR bater, mostra "Nenhum PR corresponde aos filtros". Trocar de card limpa os filtros.
- **Cabeçalhos (spec 2):** o cabeçalho do `app-card-panel` e o da linha do tempo passam a ter a **mesma altura fixa** (56px, conteúdo centralizado), independente dos botões.

### Decisões

| # | Tema | Padrão adotado | Observação |
|---|---|---|---|
| D1 | Cor de destaque do filtro ativo (`--cc-special`) | **Primária do CIME (azul)** | No ComandaCerta é roxo; trocar é mudar 1 token no `styles.scss` |
| D2 | "Pré-carregados/pré-selecionados" (specs 3 e 6) | Opções já listadas ao abrir, com os valores existentes no card; **nenhuma marcada** | Se a ideia for abrir já com algo marcado (ex.: só OPEN), é um ajuste pequeno |

## 3. Fases

### F1 — Componentes de filtro (cópia do ComandaCerta)
- [ ] `components/popover/cc-popover.component.ts` (cópia).
- [ ] `components/filter-bar/filter-bar.models.ts` e `filter-bar.component.ts` (cópia + recarga das opções ao abrir).
- [ ] Tokens `--cc-*` no `styles.scss`.

### F2 — Filtros no painel de PRs + altura dos cabeçalhos
- [ ] `github-pr-list`: `FilterProvider` com Status/Branch/Repositório a partir dos PRs do card; lista filtrada (E entre filtros, OU dentro); mensagem para filtro sem resultado; filtros limpos ao trocar de card.
- [ ] `app-card-panel` e `card-timeline`: cabeçalho com altura fixa igual.
- [ ] `ng build` sem erros; teste da lógica de filtro.

### Q1 — Validação (usuário)
- [ ] Card com vários PRs (repos, branches e status diferentes): conferir as opções, a combinação dos filtros, a troca de card e a altura dos dois cabeçalhos.
