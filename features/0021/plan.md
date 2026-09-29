# Feature 0021 — PRMake em telas menores/celular e tempo real a partir das skills

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0021` no backend (`../prform.api-0021`) e no front (`../solvace.prform.web/prform-app-0021`), a partir de `master`.

## 1. Levantamento (2026-09-28)
- **Rodapé da tela do card** (`pages/authenticated/register/register.component.html`): `p-splitbutton` "Copiar" (full/descrição/root cause/template), Abrir PR, Salvar, Limpar, Detalhes do Card, Handover, `app-devops-actions-menu`, Gerar com IA — todos `mat-raised-button` com `margin-left: 10px` num flex com `wrap`. Abaixo de 768 px (`isMobile`) tudo vira um `p-splitbutton` "Salvar" com Ações DevOps / Gerar com IA **desabilitados** e sem Detalhes/Handover.
- **Copiar**: `fullDescription` é só a descrição (o template foi desligado); "Template" copia `template`, que nunca é carregado (sempre `null`). Descrição/Root Cause são editados em `app-pr-description-panel`/`app-root-cause-panel` (usados no popover da barra do card e no modal "Abrir PR"), que já têm o slot `[panel-actions]` no cabeçalho.
- **Popovers**:
  - `cc-popover` (CDK Overlay; Ações DevOps, atalhos ⚡ da lista de PRs, filtros): posições abaixo/acima × centro/início/fim + `push`, mas **não reposiciona quando o conteúdo cresce depois de aberto** (Alterar status, Abrir PR rápido, confirmação das ações DevOps) e não tem altura máxima → sai da tela perto das bordas/em janelas baixas.
  - `p-popover` (PrimeNG) de Descrição/Root Cause: `min(900px, 92vw)` × `min(60vh, 560px)`, posicionado pelo PrimeNG só na abertura; em janela baixa/celular a caixa fica cortada.
- **Tempo real** (SignalR via relay, 0013): a API já emite `pullRequestCardUpdated` (`register-saved`, `github-pr-opened`, `github-pr-updated`, `github-pr-status`, `summary-saved`) no grupo `pullrequest:{card}` e a tela do card já escuta; nos logs de produção o relay responde **202** aos publishes da skill (ex.: card 74517, 2026-09-28 21:02). Lacunas:
  - `POST /PullRequest/{card}/github` num card sem registro **cria o registro**, mas só emite `github-pr-opened` → a tela recarrega só a lista de PRs e continua "Card ainda não salvo".
  - `HandoverApplication.Save`/`SetVisibility` **não emitem nada** (skill `gerar-handover`).
  - Home (`app-recent-cards`, `app-recent-handovers`) **não usa tempo real**.
  - Obs.: ainda há um front antigo em `prmakeweb.runasp.net` tentando `api.softhouse.app.br/ws/negotiate` (404) — esse não recebe tempo real; fora do escopo.

## 2. Decisões
- **Rodapé = barra de ações com transbordo** (`app-action-toolbar` + diretiva `appToolbarItem`): os itens são projetados como estão (os popovers continuam vivos quando escondidos, com `display: none`); a barra mede a largura de cada item (guardando a última medida de quem está escondido) e, com `ResizeObserver`, esconde da direita para a esquerda o que não cabe, mostrando o botão **⋯** com um `cc-popover` que lista os escondidos. Item simples → executa; item com popover (Ações DevOps / Ações inteligentes) → abre o popover do próprio componente ancorado no ⋯ (`openAt(origin)`).
- Ordem: Abrir PR · Salvar · Limpar · Ações DevOps · Ações inteligentes (esconde de trás para frente).
- **Ações inteligentes** = componente `app-smart-actions-menu` no mesmo padrão do `app-devops-actions-menu` (cc-popover, itens com ícone). Handover exige o card do DevOps carregado (como hoje); Gerar com IA exige o número do card.
- **Detalhes do card** = primeira opção do Ações DevOps (não altera o DevOps, sem confirmação).
- **Copiar** = botão de ícone `content_copy` no cabeçalho dos painéis de Descrição e Root Cause (copia o markdown), desabilitado quando vazio. Some o `copyFullDescriptionToClipboard`/`copyCustomButtons`/`mobileButtons` da tela.
- **cc-popover**: `ResizeObserver` no painel chama `overlayRef.updatePosition()`; `viewportMargin` 8; `max-height: calc(100dvh - 32px)` com rolagem interna. Assim o CDK escolhe de novo a posição/push quando o conteúdo cresce.
- **p-popover Descrição/Root Cause**: altura `min(60vh, 560px, calc(100dvh - 120px))`; em ≤ 768 px, caixa fixa ocupando a largura (inset 8 px), sem a seta.
- **Tempo real**:
  - Backend: nova ação `handover-saved` no grupo do card; novo grupo global `pullrequest-recent` com evento `pullRequestRecentUpdated` (`{ cardNumber, action }`) emitido junto de `register-saved`, `github-pr-opened` e `handover-saved` (best-effort, como os demais).
  - Front: `github-pr-opened` sem registro carregado → recarrega o registro também; modal de Handover escuta `handover-saved` do card (recarrega se não estiver gerando); home entra no grupo `pullrequest-recent` e recarrega as duas listas (debounce 600 ms, sem skeleton) e também no `_resynced`.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | B1 (backend: eventos), F1 (rodapé: Ações DevOps + Detalhes, Ações inteligentes, copiar nos painéis), F3 (popovers) |
| 2 | F2 (barra com transbordo "⋯"), F4 (tempo real no front) |
| 3 | T1 (build + teste visual em 1440/1024/768/390 px com a API simulada) |
| 4 | Q1 (merge na master e deploy) |

- **B1** — `PullRequestRealTimeEvents`: `Actions.HandoverSaved`, `RecentGroup`, `EventRecentUpdated`; `NotifyCardUpdatedAsync` publica também no grupo global para as ações que mudam as listas da home; `HandoverApplication` recebe `IRealTimeNotifier` e notifica em `Save`/`SetVisibility`.
- **F1** — `devops-actions-menu`: opção "Detalhes do card" (output `openDetails`) e `openAt(origin)`; `smart-actions-menu` novo; painéis com copiar; rodapé sem Copiar/Detalhes/Handover/Gerar com IA; limpeza do código do modo celular antigo.
- **F2** — `components/action-toolbar/` (componente + diretiva) e uso no rodapé.
- **F3** — `cc-popover` (reposicionar/altura máxima) e estilos globais do `cime-panel-popover`.
- **F4** — `register.component.ts` (`github-pr-opened` sem registro), `handover-dialog` (escuta `handover-saved`), `recent-cards`/`recent-handovers` (grupo `pullrequest-recent`).
- **T1** — `ng build`; Chrome headless (puppeteer) com as chamadas da API simuladas: rodapé em 1440/1024/768/390 px (itens escondidos vão para o ⋯, nada quebra linha), popovers perto das bordas e após crescer, Descrição/Root Cause no celular; `dotnet build`.
- **Q1** — merge `feature/0021` → `master` nos dois repos e push (deploy pelo GitHub Actions). Conferir em produção a tela do card no celular e a home recebendo o card salvo pela skill.
