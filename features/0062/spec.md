# Feature 0062 — Ações GitHub (Abrir PR + Copiar PR)

Só front (`prform-app`), sem mudança no backend.

1. O botão **Abrir PR** vira **Ações GitHub**, com o ícone do GitHub.
2. O novo botão abre um popover igual ao dos outros botões do rodapé (Ações DevOps / Ações inteligentes).
3. O popover tem a opção **Abrir PR** (a ação que o botão original já fazia).
4. O popover tem a opção **Copiar PR**:
   - 4.1. Abre uma lista de checklist com todos os PRs do card com status **mesclado** ou **aberto**.
   - 4.2. Cada PR mostra o status: roxo = mesclado, verde = aberto.
   - 4.3. No rodapé, o botão **Copiar** copia para a área de transferência, no formato
     `<card> [HV] - link do PR` (HV = prefixo/rótulo do ambiente de destino do PR), ou em **tabela**
     com as colunas CARD, AMBIENTE, PR — o usuário escolhe o formato.
