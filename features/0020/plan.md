# Feature 0020 — Timeline em tela cheia (com animação) e "Importar do Teams" escondido

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0020` no backend (`../prform.api-0020`, só os documentos) e no front (`../solvace.prform.web/prform-app-0020`), a partir de `master`. **Só front.**

## 1. Levantamento (2026-09-28)
- `src/app/components/card-timeline/` (`card-timeline.component.{ts,html,css}`): componente standalone, signals, zoneless; usado só em `pages/authenticated/register` (`<div class="pr-timeline"><app-card-timeline [cardNumber]>`).
- Cabeçalho: ícone, título, contador (`margin-left: auto`) e o botão "Importar do Teams" (`toggleImport()` + painel `timeline__import`, MSAL/Graph).
- A timeline tem estado local importante: rascunho do novo registro, edição/exclusão em andamento, grupo do tempo real (`timeline:<card>`), rolagem do `#body`.

## 2. Decisões
- **Mesmo elemento, não um diálogo**: um `MatDialog` criaria outra instância (outra assinatura do tempo real, perda do rascunho). A tela cheia move o próprio `.timeline` para o `<body>` enquanto aberta e devolve ao fechar — o estado fica todo intacto. Mover para o `body` evita que `transform`/`overflow` de algum ancestral quebre o `position: fixed`.
- **Animação**: Web Animations API animando `top/left/width/height` do retângulo de origem (onde a timeline está na página) até o retângulo final (centralizado, ~92% da tela, máx. 1200 px de largura) e o inverso ao fechar; `border-radius` e sombra acompanham; fundo escuro com fade. ~320 ms, `cubic-bezier(0.2, 0, 0, 1)`. Anima geometria (e não `scale`) para o texto não distorcer durante o crescimento.
- **Lugar reservado**: enquanto aberta, o host mantém a altura medida (a página de trás não "pula") e o fechamento volta para o retângulo atual do host (se a página rolou/redimensionou, encolhe para o lugar certo).
- **Rolagem**: mover o nó zera o `scrollTop` → guarda a distância do fim antes e restaura depois (quem estava lendo o fim continua no fim).
- **Camadas**: fundo e modal com `z-index` 1000, inseridos no `<body>` **antes** do `.cdk-overlay-container` (também 1000): cobrem a barra do topo (`page-container`, z 1000, que fica antes no DOM) e deixam tooltips, menus e snackbars por cima. (A 1ª versão usava 998/999 e o T1 mostrou o sino/avatar do topo por cima do modal.)
- **Fila de transições**: abrir/fechar encadeados numa promise — clicar em fechar (ou Esc/fundo) durante a abertura fecha logo depois que ela termina, em vez de o clique se perder.
- **Teams**: `teamsImportEnabled = false` no componente esconde o botão e o painel; código mantido.
- **Destruição**: se o componente for destruído aberto (troca de rota), devolve o elemento ao host e remove o fundo antes.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | F1 (Teams escondido + botão), F2 (tela cheia animada) |
| 2 | T1 (teste no navegador) |
| 3 | Q1 (PR, deploy) |

- **F1** — flag `teamsImportEnabled`; cabeçalho com espaçador; botão `open_in_full`/`close_fullscreen` com tooltip.
- **F2** — `expand()`/`collapse()` com a animação da seção 2; Esc, clique no fundo, trava de rolagem do `html`, `prefers-reduced-motion`, limpeza no `ngOnDestroy`; título com o número do card quando aberta.
- **T1** — build de produção; teste em navegador (Chrome headless) com a timeline montada: abre e fecha animando (medições do retângulo no meio da animação), Esc/fundo fecham, rascunho e rolagem preservados, voltou ao lugar exato, sem erros no console.
- **Q1** — PR → master do front (deploy) e do backend (documentos).
