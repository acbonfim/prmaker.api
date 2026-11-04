# Plano — Feature 0081

Só front (`prform-app`, branch `feature/0081`); o back recebe só a documentação (o WebSocket `caseChatUpdated` da 0080
já cobre o 2.1).

| Fase | Descrição | Depende |
|---|---|---|
| F1 | Ícone do Claude no cabeçalho do plano + popover (atividades ao vivo, ações) | — |
| F2 | `CaseChatStore` (estado por card em memória + WebSocket enquanto a tela está aberta); modal lendo o store | — |
| F3 | Conversa compacta na coluna da direita; linha do tempo vira botão ao lado do Root Cause (tela cheia); aba do celular | F2 |
| F4 | Tour guiado (driver.js): serviço, cadastro de passos com `data-tour`, auto-início por usuário/versão, "Como usar esta tela" | F1, F3 |
| Q1 | Build + e2e local (harness da 0080) com capturas | F1–F4 |
