# Status — Feature 0019 (PRMake como PWA instalável)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0019` no backend (`../prform.api-0019`) e no front (`../solvace.prform.web/prform-app-0019`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| F1 | Manifest, ícones e metas do `index.html` | 1 | — | ✅ concluída | Claude | front `5df5c12` |
| F2 | `@angular/service-worker` + `ngsw-config.json` (só o shell, sem dados da API) | 1 | — | ✅ concluída | Claude | front `5df5c12` |
| F4 | nginx: SW/`ngsw.json` sem cache, manifest, ícones | 1 | — | ✅ concluída | Claude | front `5df5c12` |
| F3 | `PwaService`: aviso "Nova versão — Atualizar" | 2 | F2 | ✅ concluída | Claude | front `5df5c12` |
| T1 | Teste local em container (instalação, cabeçalhos, atualização A→B, API/tempo real) | 3 | F1–F4 | ✅ concluída (API/tempo real → Q1) | Claude | — |
| Q1 | Deploy e teste em produção | 4 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **F1**: ícones com monograma "PR" branco sobre gradiente azul (`#4f9cf9` → `#1f5fc4`), decidido pelo usuário; gerados com `sharp` a partir de SVG (quadrado arredondado com transparência para `any`; fundo cheio com o texto na zona segura para `maskable` e `apple-touch-icon`). Manifest: `theme_color` `#212730` (cabeçalho), `background_color` `#14181d` (fundo do login). Título da aba passou de "Pull Request Maker" para "PRMake". **O `favicon.ico` atual é o logo do Slack** — não foi trocado (fora do escopo); vale trocar pelo monograma.
- **F2**: `@angular/service-worker` fixado em 20.3.7 (o `^20.3.7` puxava um 20.3.x mais novo que o core e dava ERESOLVE); `package.json` com `^20.3.0`, como os demais. `ngsw.json` do build: 5 arquivos no prefetch, 71 chunks e 17 assets lazy, **0 dataGroups**.
- **F3 — mudança em relação ao Listo**: o aviso é uma faixa própria no `App` (signal no `PwaService`), e não um `MatSnackBar`: o PRMake abre snackbars em vários componentes e o `MatSnackBar` mostra um por vez — qualquer "salvo" derrubaria o aviso de versão nova. Serviço em `src/app/services/pwa.service.ts` (padrão do repo, que não tem `core/`).
- **T1** (imagem Docker local na porta 8089 + Chrome headless via `puppeteer-core`):
  - Cabeçalhos: `/`, `/index.html`, rotas SPA, `ngsw.json`, `ngsw-worker.js`, `safety-worker.js` → `no-cache`; manifest `application/manifest+json` 1 h; `/icons/` 1 dia; `main-*.js` 1 ano immutable; `X-Robots-Tag` em tudo; IP EC2 us-west-2 → 403 também nos arquivos novos.
  - Chrome: SW ativo no scope `/`; `Page.getAppManifest` e `Page.getInstallabilityErrors` **sem erros** (instalável); após reload a página é controlada pelo SW; Cache Storage só com o shell do localhost + 2 fontes do `fonts.gstatic.com`, nenhum grupo de dados.
  - **Atualização A→B** (B = título "PRMake (versão B)", trocando o container com a aba aberta): ao voltar para a aba aparece "Nova versão do PRMake disponível. **Atualizar**"; 5 s depois continua na A (não recarrega sozinho); clique → carrega a B e o aviso some.
  - Não testado localmente: login/API/tempo real com o SW ativo (o front local aponta para a produção) → conferir no Q1. Pelo desenho não há risco: sem `dataGroups`, requisições para outro domínio não passam pelo cache do SW, e WebSocket nunca passa pelo SW.
  - Ambiente: o `credsStore: desktop` do Docker trava dentro do sandbox do Claude (pull/build ficavam parados em "load metadata") → usar `DOCKER_CONFIG` temporário sem helper de credenciais para imagens públicas.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0019` no backend e no front.
- 2026-09-28 — F1–F4 e T1 concluídas (front `5df5c12`). Falta o Q1 (PR/deploy do front e teste em produção).
