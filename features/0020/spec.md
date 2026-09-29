Tipo: Melhoria (front)
Prioridade: média
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário).

Objetivo: permitir abrir a **linha do tempo** do card em **tela cheia**, com animação — o chat "cresce" do lugar onde está até virar um modal e, ao fechar, "encolhe" de volta ao tamanho e posição originais.

Requisitos:
1. **Botão de tela cheia** no cabeçalho da timeline (ícone `open_in_full`); aberta, o mesmo lugar mostra o ícone de fechar (`close_fullscreen`).
2. **Animação de abrir**: a própria timeline sai do lugar e cresce até ocupar quase a tela inteira, sobre um fundo escurecido.
3. **Animação de fechar**: encolhe de volta até o lugar original; o fundo some junto. Fecha pelo botão, pela tecla **Esc** e clicando no fundo.
4. **Mesma timeline, sem recarregar**: é o mesmo componente (não uma cópia) — lista, texto sendo digitado, edição em andamento, tempo real e posição de leitura continuam.
5. **Esconder o botão "Importar do Teams"** por enquanto, mantendo o código para uso futuro (liga-se de novo trocando uma flag).
6. Acessibilidade: com `prefers-reduced-motion`, abre/fecha sem animação; página de trás não rola enquanto aberta.

Fora de escopo: mudanças no backend.
