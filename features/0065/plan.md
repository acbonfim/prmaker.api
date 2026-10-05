# Feature 0065 — Plano de execução

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch (nos dois repos): `feature/0065` a partir de `master`. Worktrees: `prform.api-0065` e `solvace.prform.web/prform-app-0065` (com `node_modules` ligado ao `prform-app-0019`).

| Repo | Caminho | Sigla |
|---|---|---|
| Backend | `prform.api` | `B*` |
| Frontend (Angular 20 + Material/PrimeNG) | `../solvace.prform.web/prform-app` | `F*` |

## 1. Análise do que já existe

- **Shell**: `PageContainerComponent` (`components/page-container`) = topbar + `.sidebar` + `.content-area` com **um** `<router-outlet>`; rotas em `pages/authenticated/authenticated.routes.ts` (dashboard, register, vacations, plugin-manager…).
- **Tela de PR** (`register.component.ts`, ~1300 linhas): o card vem de `?card=` (`autoSearchFromQueryParams`, `syncCardInUrl` com `replaceUrl`), assina `queryParamMap`, tem timers (`prWatchTimer`), grupos de tempo real (`switchCardGroup`) e detecção de edição local (descrição/RC ≠ salvo). **App zoneless**.
- **`CardPrStateService`** é singleton: card, registro, descrição, RC, PRs e IA do card em tela. Com duas telas de PR vivas, uma sobrescreve a outra → D8.
- **Home**: `HomeCardsService` (`Home/cards`: só os 10 últimos) + `Azure/cards/summary` (título/estado); `UserPendingService.byCard` = "Aguardando você" (já em tempo real); `HomeCardsComponent` tem os mapas `PLAN_STATUS`, `prTone`, etc. para reaproveitar.
- **Logout** só apaga `token` e `access` (`StorageService.logout`) — `localStorage` próprio sobrevive.
- **BackNavigationService** mantém pilha de URLs e histórico; trocar de aba precisa de `replaceUrl`.
- Alturas que dependem do topbar: `register.component.css:251` (`min-height: calc(100dvh - topbar - …)`), `home-cards` e árvores sticky do `reverse-engineering`/`architecture` (`100vh - 150px`) → conferir com a faixa.

## 2. Arquitetura

```
PageContainer
 ├─ topbar
 └─ main-content
     ├─ sidebar
     └─ .content-column            (novo: coluna flex)
         ├─ <app-tab-bar>          (F3)  ← TabsService (F1)
         └─ .content-area → <router-outlet>   ← TabRouteReuseStrategy (F2)
```

- **TabsService** (root, signals): `tabs[]`, `activeId`; `Tab { id, url, title, icon, createdAt, inactiveSince, frozen, dirty, card? }`. Operações: `open()`, `activate(id)`, `close(id)`, `setDirty`, `touch`. Sincroniza com o Router (`NavigationEnd` atualiza URL/rótulo da aba ativa). Relógio de congelamento (30 s + `visibilitychange`).
- **TabRouteReuseStrategy**: só atua no **filho direto do PageContainer** (a "tela da aba") e **somente durante uma troca de aba** (`switching = {from, to}`): `shouldReuseRoute`→false, `shouldDetach`→true (guarda por `from`), `shouldAttach/retrieve` (por `to`, se a URL guardada bate). Navegar dentro da aba (menu lateral) destrói a tela como hoje. `freeze(tabId)` = `componentRef.destroy()` do handle guardado.
- **Persistência**: `localStorage['prmake.tabs.v1.<externalId>']` = `{ tabs, activeId, savedAt }`, gravado com debounce; lido ao montar o PageContainer. Regras de restauração: URL atual == URL da ativa (F5) → retoma; URL de pouso `/auth/dashboard` (acabou de logar) → vai para a ativa salva; link direto para outra URL → ativa a aba que já tem essa URL ou abre uma nova (se cheio, troca a ativa).
- **Metadados do card** (F4): `TabCardInfoService` junta `Home/cards/by-numbers` (B1), `Azure/cards/summary`, `UserPendingService.byCard` e o evento de tempo real `RECENT_EVENT` (recarga em lote, debounce 600 ms) para **todas** as abas de card numa chamada.

## 3. Fases

| Fase | O que entrega | Repo | Depende de | Onda |
|---|---|---|---|---|
| **B1** | `GET Home/cards/by-numbers?cards=a,b` (máx. 10, mesma `HomeCardResponse`, reusa `BuildAsync`; cards de qualquer usuário, sem filtro de escopo) + doc no controller | back | — | 1 |
| **F1** | Modelo `Tab`, `TabsService` (abrir/ativar/fechar, limite 10, dirty, congelar por tempo, persistência por usuário, restauração, sincronismo com Router) + rotas com `data.tab` (título/ícone por tela) | front | — | 1 |
| **F2** | `TabRouteReuseStrategy` (detach/attach/freeze) registrado em `app.config.ts`; restauração de rolagem do `.content-area` por aba; `replaceUrl` na troca | front | F1 | 2 |
| **F3** | `<app-tab-bar>` (faixa 30 px, rolagem horizontal, +, ×, botão do meio, ❄, amarelo, tooltips, acessibilidade `role=tablist`), `.content-column` no PageContainer, `--cime-tabs-h` e ajustes de altura (register, home-cards, árvores sticky) | front | F1 | 2 |
| **F4** | `TabCardInfoService` + rótulo `#card · título`, ícones de plano/PR, amarelo "aguardando você" | front | B1, F3 | 3 |
| **F5** | Integração das telas: `register` republica `CardPrStateService` ao ativar e informa `dirty`; troca de card dentro da aba atualiza a aba; `home-cards`/sino/links: clique normal = aba atual, Ctrl/Cmd/meio = nova aba; confirmação ao fechar aba suja; aviso de limite | front | F2, F3 | 3 |
| **Q1** | `ng build`, `dotnet build`, teste manual (harness e2e local + Chrome), regressão (F5, logout/login, 10 abas, congelar com constante reduzida, dois cards em abas), PRs (sem merge) | ambos | todas | 4 |

Ondas 1–3 podem rodar em paralelo dentro de cada onda; as fases são pequenas e serão feitas na sessão principal, nessa ordem, com um commit por fase.

## 4. Riscos

1. **Estado compartilhado** (`CardPrStateService`, grupos de tempo real do card): duas telas de PR vivas. Mitigação D8 + reassinar o grupo ao ativar; teste com 2 cards.
2. **Tela detached continua com timers** (`prWatchTimer`, ws) até congelar. Aceito (é o "ativa"); congelamento a 10 min limita o custo. Se pesar, F5 pausa o polling de telas inativas via sinal de `TabsService`.
3. **API interna do router**: `DetachedRouteHandle` é opaco; `componentRef.destroy()` usa o formato interno (`DetachedRouteHandleInternal`) — isolar num único ponto com `try/catch` e fallback (descartar o handle).
4. **Voltar/histórico** (`BackNavigationService`): validar que `replaceUrl` na troca não quebra a pilha.
5. **Alturas fixas** (`calc(100dvh - topbar …)`): revisar com a faixa de 30 px.
6. **Edição não salva ao fechar/congelar**: coberto por `dirty` (D4).
