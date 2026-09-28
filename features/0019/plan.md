# Feature 0019 — PRMake como PWA instalável, com aviso de versão nova

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0019` no backend (`../prform.api-0019`, só os documentos) e no front (`../solvace.prform.web/prform-app-0019`), a partir de `master`. **Só front.**

## 1. Levantamento (2026-09-28)
- **Referência — Listo/Comanda Certa** (feature 0004 de lá, commits `033673d` e `8599e70` em `ComandaCerta.App`):
  - `@angular/service-worker` + `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' })`; `"serviceWorker": "ngsw-config.json"` no build de produção.
  - `ngsw-config.json`: grupos `app` (prefetch: index, manifest, CSS, `main-*`, `polyfills-*`), `chunks` e `assets` (lazy) e `fonts` (Google Fonts). Lá também há `dataGroups` com consultas da API para offline — **aqui não** (spec, req. 3).
  - `core/pwa/pwa.service.ts`: `versionUpdates` → `VERSION_READY` → `MatSnackBar.open('Nova versão … disponível.', 'Atualizar')` sem duração, `onAction` → `document.location.reload()`; `checkForUpdate()` no `visibilitychange`. Iniciado no construtor do `App`.
  - `manifest.webmanifest` + ícones em `public/icons/` (72–512, maskable 192/512, `apple-touch-icon`); `index.html` com `<link rel="manifest">`, `theme-color`, `apple-touch-icon`.
  - nginx: `ngsw.json|ngsw-worker.js|safety-worker.js|worker-basic.min.js` com `no-cache` (o fix `8599e70` existiu porque caíam na regra de estáticos com cache longo); manifest com `application/manifest+json` e 1 h.
- **PRMake (front, `origin/master` `9cfdfca`)**:
  - Angular 20.3, **zoneless** (`provideZonelessChangeDetection`), Material (azure) + PrimeNG; `MatSnackBar` já usado em vários componentes.
  - Sem `@angular/service-worker`, sem manifest; `public/` só tem `favicon.ico` e `web.config` (resto do IIS antigo); logo em `src/assets/images/logo.svg` é horizontal (284×47) — **não serve como ícone quadrado**.
  - `docker/nginx.conf`: a regra `\.(js|css|png|…)$` dá **1 ano `immutable`** → pegaria `ngsw-worker.js` e os ícones sem hash. `index.html` já `no-cache`. Cabeçalhos/bloqueio de robôs da 0018 precisam continuar.
  - `index.html`: `lang="en"`, título "Pull Request Maker".
  - Deploy: `deploy.yml` (push na `master`) → imagem nginx → Cloud Run. Nada muda no pipeline.

## 2. Decisões
- **Só o shell em cache**, nenhum `dataGroups`: API/auth em outro domínio passam direto pela rede (o service worker só intercepta o que está configurado; navegações vão para o `index.html` em cache). SignalR (negotiate POST + WebSocket) não é interceptado. Como nada da API fica no aparelho, o logout não precisa limpar cache.
- **Aviso**: faixa própria no `App` "Nova versão do PRMake disponível." + **Atualizar** (texto do Listo). Não usa `MatSnackBar` como lá: o PRMake abre outros snackbars e só cabe um por vez — o aviso seria derrubado. `checkForUpdate()` ao voltar para a aba **e a cada 30 min** enquanto visível (no PRMake a aba fica aberta o dia inteiro no desktop). `unrecoverable` → snackbar "O PRMake precisa ser recarregado." + **Recarregar**.
- **Zoneless**: `registerWhenStable` depende da estabilidade da aplicação; no zoneless ela vem das `PendingTasks` — com a primeira tela fazendo polling/tempo real o app pode nunca ficar "estável", aí vale o teto de 30 s (comportamento aceito). Confirmar no T1 que o SW registra.
- **Ícones**: gerar a partir de um símbolo quadrado da marca. Decidido: monograma "PR" branco sobre gradiente azul. Cores do manifest: `theme_color`/`background_color` = fundo escuro do cabeçalho do app (`--surface-3`, `#212730`), ajustável.
- **nginx**: `location` exatos para os arquivos do SW (`no-cache`) e `manifest.webmanifest` (`application/manifest+json`, 1 h), **antes** da regex de estáticos; ícones em `/icons/` com cache de 1 dia (sem hash no nome). Todos com `X-Robots-Tag` (em nginx, `add_header` num `location` substitui os herdados).
- **Saída de emergência**: `safety-worker.js` (vem no build do `@angular/service-worker`) copiado sobre `ngsw-worker.js` desregistra o SW nos clientes no próximo acesso — documentar em `deploy/README.md` do front.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | F1 (manifest, ícones, `index.html`), F2 (service worker + `ngsw-config.json`), F4 (nginx) |
| 2 | F3 (`PwaService`: aviso de atualização) |
| 3 | T1 (teste local em container) |
| 4 | Q1 (deploy e teste em produção) |

- **F1 — Instalável**: `public/manifest.webmanifest` (name/short_name "PRMake", `lang: pt-BR`, `display: standalone`, `start_url`/`scope` `./`, cores); `public/icons/` (72, 96, 128, 144, 152, 192, 384, 512, maskable 192/512, `apple-touch-icon` 180); `index.html`: manifest, `theme-color`, `apple-touch-icon`, `apple-mobile-web-app-capable`/`title`, `lang="pt-BR"`, título "PRMake", `<noscript>`.
- **F2 — Service worker**: `npm i @angular/service-worker@^20.3` (mesma versão do core); `"serviceWorker": "ngsw-config.json"` na configuração `production` do `angular.json`; `provideServiceWorker(...)` no `app.config.ts` (`enabled: !isDevMode()`, `registerWhenStable:30000`); `ngsw-config.json` com `app` (prefetch), `chunks`, `assets` e `fonts` (lazy), sem `dataGroups`; conferir `navigationUrls` (padrão exclui URLs com extensão e `__`) contra rotas que não podem cair no `index.html`.
- **F3 — Aviso de atualização**: `src/app/services/pwa.service.ts` (padrão do Listo + checagem a cada 30 min e `unrecoverable`), iniciado no construtor do `App`. Nunca recarrega sozinho.
- **F4 — nginx**: regras da seção 2 em `docker/nginx.conf`, mantendo robôs/`X-Robots-Tag` da 0018.
- **T1 — Teste local** (imagem Docker do front, `http://localhost` conta como contexto seguro):
  1. `ngsw.json` gerado; DevTools → Application: manifest sem erros, SW ativo, "Instalar" oferecido; app instalado abre em janela própria.
  2. Cabeçalhos: `ngsw.json`/`ngsw-worker.js` `no-cache`, manifest `application/manifest+json`, chunks com hash 1 ano, 403 dos robôs e `robots.txt` intactos.
  3. **Atualização**: subir a versão A, abrir; gerar a B (mudança visível) e trocar o container → ao voltar para a aba aparece o aviso; nada recarrega sozinho; **Atualizar** carrega a B.
  4. API, login/refresh, tempo real (SignalR), upload/download de imagens funcionando com o SW ativo; aba anônima e `ng serve` sem SW.
  5. Nenhuma resposta da API no Cache Storage.
- **Q1 — Deploy**: PR → master do front (o backend só leva os documentos). Em produção: instalar no Chrome/Edge e no celular; fazer um segundo deploy qualquer e ver o aviso chegar numa aba aberta. Rollback: revisão anterior do Cloud Run **não remove** o SW já instalado nos clientes (ele continua servindo o shell em cache até achar versão nova) — para desligar, usar o `safety-worker.js` (seção 2).
