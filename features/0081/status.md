# Status — Feature 0081

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| F1 | Ícone do Claude + popover | concluída | Claude | front | faixa "Claude rodando/na fila" virou ícone `plan__run` (anima rodando, ampulheta na fila, discreto parado) com popover ao vivo e ações; a faixa vermelha de falha continua no corpo |
| F2 | CaseChatStore | concluída | Claude | front | `services/case-chat-store.service.ts`: conversa por card em memória (LRU 6), WebSocket enquanto a tela do card acompanha, rascunho/anexos/resposta/rolagem/imagens guardados; `CaseChatStatusService` virou vista do store |
| F3 | Conversa compacta + linha do tempo no topo | concluída | Claude | front | `case-compact` substitui `case-preview` na coluna da direita (preso ao card buscado, não ao digitado); botão "Linha do tempo" ao lado do Root Cause abre em tela cheia (`openFrom`); aba "Especialista" no celular; `?focus=timeline` abre a linha do tempo |
| F4 | Tour guiado | concluída | Claude | front | driver.js 1.10.2; `components/guided-tour/` (serviço, `pr-screen.tour.ts` com 19 passos em `data-tour`, guia "Como usar esta tela"); entrada sem card → continua ao abrir o card; flag por usuário/versão no localStorage |
| Q1 | Build + e2e | concluída (local) | Claude | | `ng build` ok; e2e no harness da 0080 (`e2e/f0081.mjs`, executor simulado por `e2e/simrun.sh`); capturas em `e2e/` |

## Log
- 2026-11-04 — spec e plano a partir do pedido do usuário (ajustes 1–3).
- 2026-11-04 — F1–F4 e Q1 local. Testado: tour inteiro (19 passos, "Pular tour" em todos, "Concluir tour" no último, não
  reabre depois de concluir); sem card só boas-vindas + busca, e ao buscar o card continua em "3 de 19"; "Mostrar na
  tela" do guia começa no passo escolhido; celular abre a aba certa no passo do especialista. Ícone do Claude anima
  rodando e o popover recebe a atividade nova ao vivo. Conversa compacta envia, o rascunho aparece no modal, reabrir o
  modal não faz GET nem mostra "Abrindo a conversa…"; mensagem de outra pessoa chega por WebSocket com o modal fechado.
  Linha do tempo abre em tela cheia pelo botão do topo e fecha com Esc.

## Pendências
- Validar com usuários reais se o tour abre na hora certa (primeira entrada na tela) e ajustar textos.
- `?focus=timeline` (atalho da Home) agora abre a linha do tempo em tela cheia — conferir com quem usa o atalho.
- Para mudar um passo do tour: editar `pr-screen.tour.ts` e subir `version` (+ `since` dos passos novos) — quem já viu
  recebe só as novidades. Em desenvolvimento, passo sem elemento na tela gera aviso no console.
