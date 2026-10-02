# Feature 0043 — PRMake usável no celular (iPhone, Safari e app instalado)

## Contexto
Testando o PRMake num iPhone, a tela de Pull Request (`/auth/register`) é praticamente inutilizável: o plano de
execução não rola, o menu não recolhe, ler o plano em tela cheia é ruim e, com o app instalado (PWA, `display:
standalone`), não existe botão voltar. O mesmo acontece no PC com o app instalado. A feature é **só de front**
(`solvace.prform.web/prform-app`); não há mudança de API.

## Objetivo
Ter uma experiência digna no celular (iPhone Safari e PWA instalado; Android Chrome junto), sem piorar o desktop:
1. Rolar o plano, os passos, os logs e a timeline sem falhas.
2. Menu principal que recolhe sozinho ao escolher uma tela.
3. Leitura confortável do plano (e de cada passo) em tela cheia.
4. Botão **Voltar** no canto esquerdo da barra superior, também no app instalado no PC.
5. Revisão geral de todas as telas no celular.

## 1. Rolagem do plano e dos passos
**Problema:** o scroll aparece, mas o gesto nem sempre rola.

**Causas encontradas no código:**
- **Tooltip do Material bloqueando o toque.** Todo elemento com `matTooltip` recebe `touch-action: none` inline em
  iOS/Android. Cada linha de passo é um `<button class="step__row">` com `[matTooltip]="stepTooltip(step)"`
  (`execution-plan.component.html:318-321`), então um arrasto que começa em cima de um passo não rola a lista. O
  mesmo vale para a lista de PRs, os horários dos logs e o textarea da timeline (209 tooltips no app). Não existe
  `MAT_TOOLTIP_DEFAULT_OPTIONS` em `app.config.ts`.
- **Caixas de rolagem aninhadas e pequenas.** No celular (≤900px) os painéis têm altura fixa: PRs 260px, plano 420px,
  timeline 360px (`register.component.css:180-194`). Dentro do plano ainda ficam `.plan-logs` (220px), `pre` e
  `.plan-req__detail` (160px), e tudo isso dentro de `.content-area`. Nenhum tem `overscroll-behavior`.
- **O plano rola sozinho durante a atualização ao vivo.** `scrollToStep()` (`execution-plan.component.ts:1252`) e
  `scrollLogsToEnd()` (`:1267`) disparam com eventos de realtime, com o poll de 30s e com o tick de 15s, e brigam com
  o dedo do usuário.
- **`100vh` fixo.** `.page-container`/`.main-content` usam `100vh` (`page-container.component.css:2,22`). No iOS o
  fim da área fica escondido atrás da barra do Safari.

**Requisitos:**
- Tooltips não podem bloquear o gesto nativo: `touchGestures: 'off'` global (ou equivalente em dispositivos de toque).
  O conteúdo do tooltip do passo precisa estar acessível de outra forma no toque (ex.: ao abrir o passo).
- No celular, **uma área de rolagem principal por tela**. Proposta para a tela de PR: abas/segmentos
  **PRs | Plano | Timeline** ocupando a altura disponível, em vez de três caixas empilhadas de altura fixa. Dentro do
  plano, logs e blocos de código podem rolar, mas com `overscroll-behavior: contain`.
- Rolagem automática só quando o usuário não está interagindo (ex.: nenhum toque/scroll nos últimos 5s) e nunca
  durante o arrasto.
- Trocar `100vh` por `100dvh` (com fallback) nos contêineres de página.

## 2. Menu principal
**Problema:** ao escolher uma opção, o menu continua aberto.

**Hoje:** o menu é uma `div.sidebar` (`page-container.component.html:9-11`) que só abre e fecha pelo hambúrguer
(`toggleSidebar()`). `side-menu.component.ts:80-88` escuta `NavigationEnd` só para marcar a rota ativa, e
`navigateTo()` (`:118-120`) não fecha nada. Em ≤576px o menu aberto ocupa 100% da largura e **empurra** o conteúdo
para baixo (sem sobreposição e sem fundo escurecido). Em 577–768px o menu fechado vira um trilho de ícones de 78px que
come espaço da tela.

**Requisitos:**
- Em telas pequenas (≤768px) o menu é uma **gaveta sobreposta** com fundo escurecido. Fecha ao escolher uma opção, ao
  tocar fora, no Esc e no botão Voltar (item 4). Fechada, não ocupa espaço (sem trilho de ícones).
- No desktop o comportamento atual continua, com o trilho e o estado expandido/recolhido.
- Corrigir o link "Início": aponta para `auth/home`, mas a rota é `auth/dashboard`, então nunca aparece como ativo.

## 3. Leitura do plano em tela cheia
**Problema:** ler o conteúdo do plano em tela cheia no celular é muito ruim.

**Hoje:** o botão `open_in_full` usa o `FullscreenPanel` (`helpers/fullscreen-panel.ts`). Ele calcula o tamanho por
`window.innerWidth/innerHeight` (cerca de 92%, margem mínima de 16px), ignora safe area e teclado, e mantém **sempre
duas colunas**: passos com `min(440px, 42%)` e detalhe ao lado (`execution-plan.component.css:696-712`). Num iPhone de
390px sobram ~150px para os passos e ~208px para o texto. O markdown inline usa 12,5px, e não há `img { max-width }`.
A timeline usa o mesmo padrão de tela cheia.

**Requisitos (celular):**
- A tela cheia ocupa a tela inteira (`100dvh`, respeitando safe areas), sem margem.
- **Uma coluna por vez:** lista de passos e, ao tocar num passo, o detalhe do passo em tela cheia, com Voltar para a
  lista e navegação anterior/próximo passo.
- Tipografia de leitura: corpo ≥ 16px, entrelinha ~1,5, largura de leitura confortável. Imagens com
  `max-width: 100%`. Tabelas e `pre` com rolagem horizontal própria, sem estourar a página.
- A rolagem da página de baixo fica realmente travada (hoje trava `html`, mas quem rola é `.content-area`).
- O mesmo vale para a tela cheia da timeline e para o detalhe do passo aberto inline.
- No desktop, as duas colunas continuam.

## 4. Botão Voltar
**Problema:** no app instalado (celular ou PC) não há botão voltar do navegador, e o app não tem nenhum (`Location`
ou `history.back` não são usados em lugar nenhum).

**Requisitos:**
- Botão **Voltar** (seta) no **canto esquerdo da barra superior**, sempre na mesma posição, em todas as telas
  autenticadas.
- Ordem do que ele fecha: gaveta do menu aberta → diálogo/tela cheia aberta → tela anterior do histórico do app.
- Se não houver tela anterior *dentro do app* (ex.: abriu direto por um link), vai para a tela "pai" ou para o
  Dashboard. Nunca sai do app nem volta para o login.
- Fica oculto ou desabilitado no Dashboard sem histórico.
- O gesto/tecla de voltar do sistema (Android, Alt+← no PC) deve ter o mesmo efeito sobre diálogos e tela cheia
  (fechar em vez de trocar de tela).
- Conviver com o hambúrguer sem apertar a barra: no celular, Voltar à esquerda e menu ao lado (ou o menu passa para
  outro ponto, a definir no plano).

## 5. Revisão geral para celular
Pontos já encontrados (lista mínima, não exaustiva):
- **PWA/iOS:** `index.html` usa `apple-mobile-web-app-status-bar-style: black-translucent`, mas o viewport não tem
  `viewport-fit=cover`. A barra superior fixa e as barras de rodapé não respeitam `env(safe-area-inset-*)`, então
  ficam sob o notch e o indicador de início. Ajustar viewport e safe areas em barra superior, rodapés, gaveta,
  diálogos e telas cheias.
- **Zoom do iOS ao focar campos:** inputs e textareas com fonte < 16px (barra do plano 13px, notas 13px, chat 14px,
  Base Solvace 13–14px, filter-bar 14px). No celular, todos com ≥ 16px.
- **Alvos de toque:** botões de 26–34px (plano 32px, ações dos painéis 34px, ações das notas 26px). No celular, ≥ 44px
  de área tocável.
- **Ações só no hover:** editar/excluir comentário da timeline e ações das mensagens do chat ficam com `opacity: 0`
  até o hover. Ações rápidas da lista de PRs só aparecem no clique direito. O dia do calendário de férias usa
  `mouseenter`. Em `(hover: none)`, tudo isso precisa estar visível ou acessível por toque.
- **Cabeçalho do plano:** 56px fixos com até ~8 botões de 32px, sem quebra, cortado por `overflow: hidden`. No
  celular, ações secundárias vão para um menu "⋯", como o rodapé (`app-action-toolbar`) já faz.
- **Diálogos:** no celular viram tela cheia (ou bottom sheet), com cabeçalho fixo, botão fechar/voltar e conteúdo
  rolável. Casos atuais: perfil 520px, usuários 520/620px, serviços 480px, chat 980px × 88vh, arquivos, executores,
  skills, minha api-key, integrações, consumo de IA, abrir PR, resumo, detalhes do card, handover, Gerar com IA.
- **Larguras fixas:** `git-diff-viewer` com `min-width: 800px` e `calc(100vh - 580px)`, p-table de client-access com
  `50rem`, grids `minmax(300–450px)` (manager, services, plugin, férias), tabelas do consumo de IA e de executores.
  Nada pode gerar rolagem horizontal da página.
- **Breakpoints:** hoje são 11 valores soltos (520 a 1280px) em 22 arquivos. Definir poucos breakpoints padrão
  (ex.: 576 / 768 / 1024) num lugar só e usar em todas as telas.
- **Rotas quebradas:** o redirect vazio de `user` vai para `/auth/user/dashboard`, que não existe.

**Telas a revisar no celular** (todas, uma a uma): login, primeiro acesso, handover público, Dashboard, Pull Request
(painéis, plano, timeline, rodapé, Root Cause), usuário, gerenciar usuários, serviços, plugins, Base Solvace
(arquitetura, mapa, perguntas, guia), client-access, férias, saldos, aprovações, além de todos os diálogos, telas
cheias e popovers citados acima.

## Critérios de aceite
- No iPhone (Safari e PWA instalado), na tela de PR: rolar o plano começando o gesto em cima de qualquer passo
  funciona sempre. Rolar logs e timeline também. Nada rola sozinho enquanto o usuário mexe.
- Escolher uma opção no menu fecha o menu. Tocar fora também.
- Tela cheia do plano no celular: uma coluna, texto ≥ 16px, abrir um passo e voltar para a lista sem perder a
  posição.
- Botão Voltar visível no canto esquerdo em todas as telas autenticadas. Fecha menu/diálogo/tela cheia antes de
  trocar de tela e nunca sai do app.
- Nenhuma tela com rolagem horizontal da página em 360, 375, 390 e 430px de largura. Nenhum zoom automático ao focar
  campos. Nada escondido sob o notch ou o indicador de início.
- Desktop sem regressão: o layout de três colunas, o trilho do menu e a tela cheia em duas colunas continuam como hoje.
- Validação registrada no `status.md` com capturas em 390×844 (iPhone 14/15) e 375×667 (iPhone SE) via Chrome
  headless em modo mobile, mais o teste no iPhone real (Safari e PWA) feito pelo usuário.

## Fora do escopo
- App nativo e mudanças de API/backend.
- Redesenho visual do desktop.
