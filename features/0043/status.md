# Status — Feature 0043

Branch `feature/0043` em `prform.api-0043` (spec/plano) e `prform-app-0043` (front).

| Fase | Status |
|---|---|
| F1 | ✅ concluída |
| F2 | ✅ concluída |
| F3 | ✅ concluída |
| F4 | ✅ concluída |
| F5 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-01 — spec, plano e branches criados.
- 2026-10-01 — F1/F2: viewport-fit=cover + safe areas, `--cime-topbar-*`, `100dvh`; `MAT_TOOLTIP_DEFAULT_OPTIONS`
  com `touchGestures: 'off'` (a etapa do plano não fica mais com `touch-action: none`); campos 16px em toque;
  `MobileDialogsService` (diálogos grandes em tela cheia ≤576px); gaveta ≤768px que fecha ao navegar/tocar fora/Esc;
  `BackNavigationService` + botão Voltar (camadas: gaveta, diálogo, tela cheia, etapa aberta; voltar do sistema fecha
  só a do topo; sem histórico → tela pai/Dashboard). `MAT_DIALOG_DEFAULT_OPTIONS.closeOnNavigation=false` (o serviço
  fecha só o diálogo do topo).
- 2026-10-01 — F3/F4: tela de PR ≤900px em abas (PRs/Plano/Linha do tempo) com a `.content-area` como única rolagem,
  cabeçalhos presos sob as abas e rodapé de ações preso; `⋯` no cabeçalho do plano (≤470px de painel); rolagem
  automática só 5 s depois do último toque; tela cheia ≤768px ocupa a tela (lista → etapa, anterior/próxima, 16px) e
  trava a `.content-area`. Teste local (Postgres 55443, card 7010): gesto de toque começando na etapa rola
  (850→1247 px), gaveta fecha ao navegar, Voltar do app/sistema fecha camada por camada; desktop 1600×900 igual.
- 2026-10-01 — F5: varredura de todas as rotas, diálogos do menu do usuário, login, primeiro acesso e handover em 375×667 e
  390×844 (auditoria automática: rolagem horizontal da página e campos < 16px — nenhum). Corrigidos: cabeçalho das
  telas de usuários/serviços/plugins (botão quebrava), `min-width` de desktop no conteúdo dos diálogos (perfil,
  usuários, serviços, esqueci a senha), cabeçalho do diálogo de arquivos, altura do diff (`max(240px, …)`), redirect
  de `/auth/user`. Diálogos grandes (conversa, arquivos, abrir PR, perfil, integrações, consumo de IA, api-key,
  skills, executores) abrem em tela cheia no celular.
- 2026-10-01 — T1: `ng build` de produção ok (só avisos de budget de CSS, que já existiam); tela de PR (3 abas + tela
  cheia) sem rolagem horizontal nem campo < 16px em 375, 390 e 430 px; desktop 1600×900 e tablet 820 px sem mudança
  de layout. Falta o teste no iPhone real (Safari e app instalado) — fica com o usuário.
