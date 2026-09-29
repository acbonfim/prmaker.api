# Feature 0022 — Feedback na troca de tela, refresh que não trava, tela de PR limpa e card na URL

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0022` no backend (`../prform.api-0022`, só os documentos) e no front (`../solvace.prform.web/prform-app-0022`), a partir de `master`. **Só front.** (A `feature/0021` — telas menores/celular — está em andamento em outra sessão.)

## 1. Diagnóstico (2026-09-28)
- **Trava na troca de tela** — `AuthService.tryRefreshingTokens()` (chamado pelo `AuthGuard` quando o access token expirou):
  - no `error` do POST de refresh está `reject;` (a função **não é chamada**) → com erro de rede/401/refresh revogado a promise **nunca termina**, o guard fica esperando para sempre e a navegação não acontece, sem nenhum aviso;
  - resposta sem `object` → `refreshRes.object.accessToken` lança → navegação falha em silêncio;
  - sem timeout; guard, login e o timer de 59 min podem disparar refreshes simultâneos.
- **Sem feedback**: as rotas são lazy (`loadComponent`/`loadChildren`) + `AuthGuard` assíncrono; nada é mostrado entre o clique e a tela nova. O `<ngx-loading-bar>` só existe dentro de algumas telas.
- **Estado antigo** — `CardPrStateService` é `providedIn: 'root'`: descrição, root cause, registro e PRs do último card sobrevivem à saída da tela. Ao voltar, a tela nova nasce sem card, mas os painéis leem o estado antigo e o `effect` do construtor copia descrição/root cause para `pullRequest` → `hasAnythingToClear` = true → só o Limpar liberado.
- **Card na URL**: `autoSearchFromQueryParams()` já lê `?card=` (atalhos da home); a busca manual não escreve na URL.

## 2. Decisões
- **Refresh**: `tryRefreshingTokens()` devolve sempre `true/false` — `firstValueFrom` + `timeout(15 s)`, `try/catch`, valida `object.accessToken/refreshToken`; promise única em andamento (chamadas simultâneas reaproveitam). Signal `refreshing` para o aviso. Com `false`, o guard já limpa a sessão e manda para o login com "Sessão encerrada".
- **Feedback**: no `App`, eventos do router → barra fina indeterminada no topo (aparece após 150 ms para não piscar em navegação instantânea); após 1,5 s, pílula "Carregando…" ou "Renovando sessão…". `NavigationError` → snackbar "Não foi possível abrir a tela" com **Recarregar**.
- **Tela limpa**: `prState.reset()` no construtor da tela de PR, antes do `effect` (cache de repositórios preservado).
- **URL**: na busca, `router.navigate([], { queryParams: { card }, replaceUrl: true })` (não empilha histórico; a tela não escuta mudanças de query, então voltar/avançar não ficam inconsistentes); Limpar remove o `card`.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | F1 (refresh), F2 (feedback de navegação), F3 (tela limpa + URL) |
| 2 | T1 (teste no navegador com API simulada) |
| 3 | Q1 (PRs e deploy) |

- **T1**: refresh com erro/timeout/resposta inválida → navegação termina (login); refresh ok → navega; barra/pílula aparecem numa navegação lenta; `NavigationError` mostra aviso; sair e voltar da tela de PR → limpa (Limpar desabilitado); buscar → `?card=` na URL; F5 → mesmo card; Limpar → sem `card`.
