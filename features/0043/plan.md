# Plano — Feature 0043

Só front (`prform-app`). Breakpoints padrão: **576** (celular), **768** (tablet/celular deitado), **1024**.

| Fase | O quê | Depende |
|---|---|---|
| F1 | Base mobile: `viewport-fit=cover`, safe areas, `100dvh`, tooltips sem bloquear o toque (`touchGestures: 'off'`), inputs ≥ 16px e alvos ≥ 44px em toque, ações de hover visíveis em `(hover: none)`, diálogos em tela cheia no celular | — |
| F2 | Shell: menu como gaveta sobreposta (≤768px) que fecha ao navegar/tocar fora/Esc; link "Início"; botão **Voltar** no canto esquerdo (fecha gaveta → diálogo/tela cheia → histórico do app → tela pai/Dashboard) | F1 |
| F3 | Tela de PR: abas PRs/Plano/Timeline no celular com uma área de rolagem; `overscroll-behavior: contain`; rolagem automática do plano só sem interação do usuário; cabeçalho do plano com "⋯" | F1 |
| F4 | Tela cheia do plano e da timeline no celular: ocupa a tela, uma coluna por vez (lista → passo com Voltar e anterior/próximo), leitura ≥ 16px, imagens/tabelas contidas, trava a rolagem de `.content-area` | F3 |
| F5 | Revisão das demais telas e diálogos (Dashboard, Base Solvace, usuários, serviços, plugins, férias, client-access, login, handover público, diálogos) e rota `user/''` | F1, F2 |
| T1 | Teste no Chrome headless em 375×667, 390×844 e 430×932 (e desktop 1600×900) com capturas; checagem de rolagem horizontal e de fontes < 16px | F1–F5 |
