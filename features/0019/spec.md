Tipo: Melhoria (front)
Prioridade: média
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário).

Objetivo: transformar o front do PRMake (`solvace.prform.web/prform-app`) em **PWA instalável** — o usuário instala o PRMake como app (Chrome/Edge no desktop, Android e iOS pela tela inicial) e, quando sai uma versão nova, recebe um **aviso para atualizar a tela com um botão**, sem recarregar sozinho. Mesmo modelo usado no Comanda Certa/Listo (feature 0004 de lá: `@angular/service-worker`, manifest, `PwaService` com snackbar "Atualizar").

Contexto: o front é Angular 20 (zoneless, Material + PrimeNG) servido por nginx no Cloud Run. Hoje o `index.html` já vai sem cache e os arquivos com hash com 1 ano; não há manifest nem service worker, então não dá para instalar e uma aba aberta o dia todo fica na versão antiga até alguém dar F5.

Requisitos:
1. **Instalável**: `manifest.webmanifest` (nome "PRMake", ícones 72–512 + maskable + apple-touch-icon, `display: standalone`, cores da marca), `theme-color` e metas de iOS no `index.html`. Chrome/Edge devem oferecer "Instalar app".
2. **Service worker** (`@angular/service-worker`, mesma versão do Angular), ligado só no build de produção; guarda o shell do app (index, JS/CSS, ícones, fontes do Google) para abrir rápido.
3. **Sem dados da API em cache**: nenhuma resposta da API/auth fica guardada no aparelho (dados de clientes, cards e api-keys). Chamadas à API, SignalR/WebSocket e o relay passam direto pela rede, sem interferência do service worker.
4. **Aviso de atualização**: quando o service worker baixa uma versão nova, mostrar um aviso persistente "Nova versão do PRMake disponível" com o botão **Atualizar**, que recarrega a página. Nunca recarregar sozinho (o usuário pode estar no meio de um formulário de PR). Conferir versão nova ao voltar para a aba e periodicamente (a aba fica aberta o dia todo). Se o service worker entrar em estado irrecuperável, avisar e pedir para recarregar.
5. **nginx**: `ngsw.json`, `ngsw-worker.js` e `safety-worker.js` sempre revalidados (`no-cache`) — hoje cairiam na regra de `.js` com 1 ano e o aviso de versão nova nunca apareceria; manifest com content-type certo e cache curto. Manter os cabeçalhos da 0018 (`X-Robots-Tag`, bloqueio de robôs).
6. **Plano de saída**: documentar como desligar o service worker em produção se der problema (publicar o `safety-worker.js` no lugar do `ngsw-worker.js`).

Fora de escopo: uso offline com dados, notificações push, botão próprio de "Instalar" no menu (o navegador já oferece; pode vir numa melhoria).
Backend: sem mudanças.
