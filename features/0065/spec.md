# Feature 0065 — Abas internas na aplicação

Hoje o usuário trabalha com um card por vez: abrir outro card (ou ir a outra tela) troca a tela inteira e perde o contexto
(card aberto, rolagem, edição em andamento). A aplicação passa a ter **abas internas**, numa faixa fina logo abaixo do
topbar, onde cada aba guarda uma tela da aplicação (Home, Pull Request de um card, Férias, Configurações…).

## Requisitos (do usuário)

1. **Ocupar pouco espaço**, principalmente na tela de Pull Request — a faixa é fina (~30 px; ~26 px no celular).
2. **Alternar rápido, sem recarregar**: a tela da aba que sai fica guardada e volta como estava (campos, rolagem, painéis).
3. Botão **+** cria uma aba nova, aberta na **Home**.
4. A aba tem **nome personalizado**: na tela de Pull Request mostra o número do card + informações importantes como na
   Home (status do plano, PRs; **amarela** quando o plano está **aguardando o usuário**). Sem espaço, as informações
   são **truncadas com reticências**.
5. As abas ficam ativas, mas **após mais de 10 minutos sem interação congelam** para liberar memória; ao abrir de novo,
   reativam.
6. **Máximo de 10 abas** abertas.
7. As abas abertas são **gravadas** e **resistem a login/logout**.

## Decisões (defaults — alterar aqui se discordar)

| # | Decisão |
|---|---|
| D1 | A faixa fica **entre o topbar e a área de conteúdo, à direita do menu lateral** (o menu continua de ponta a ponta). Não altera `--cime-topbar-total`; nasce a variável `--cime-tabs-h`. |
| D2 | Mecanismo: **`RouteReuseStrategy` por aba**. A URL é o estado da aba (`/auth/register?card=75294`); trocar de aba guarda o componente da tela que sai e reanexa o da que entra. Duas abas na mesma tela (dois cards) têm instâncias separadas. |
| D3 | **Sem interação** = tempo desde que a aba deixou de ser a ativa. A aba ativa nunca congela. Limite: 10 min (constante). |
| D4 | **Congelar** = destruir a instância da tela (libera DOM, timers e inscrições de tempo real); a aba mantém URL e rótulo, aparece esmaecida com ❄. **Reativar** = reabrir pela URL (recarrega os dados). Aba com **edição não salva** (ex.: descrição/RC alterados) **não congela**, e fechá-la pede confirmação. |
| D5 | Persistência em **`localStorage` por usuário** (`externalId`): sobrevive a F5, logout e novo login (o logout só apaga token/acesso). Não vai ao servidor, então não acompanha o usuário entre dispositivos. Ao restaurar, só a aba ativa abre; as demais nascem congeladas. |
| D6 | Com 10 abas, o **+** fica desabilitado (dica "Limite de 10 abas"); abrir card em nova aba (Ctrl/Cmd+clique) avisa o limite. |
| D7 | Trocar de aba usa `replaceUrl` (não polui o histórico). O "Voltar" continua dentro da aba ativa. |
| D8 | O `CardPrStateService` (singleton com o card em tratamento) é compartilhado entre telas: ao ativar uma aba de PR, a tela **republica** seu estado nele, para não herdar o da aba anterior. |
| D9 | Nome da aba de card: `#<card> · <título no DevOps>`; ao lado, ícone de plano (cor pelo status) e de PRs (aberto/mesclado/fechado, com contagem). **Amarela** = o card está em `UserPendingService.byCard` ("Aguardando você"). Tooltip com o resumo completo. Prioridade ao truncar: número do card e indicadores ficam; o título cede com reticências. |
| D10 | Backend: um endpoint leve devolve o resumo (`HomeCard`) de **cards específicos** (`GET Home/cards/by-numbers`), pois `Home/cards` só traz os 10 últimos. O título/estado vem de `Azure/cards/summary` (já existente). |
| D11 | Fechar a última aba abre uma **Home** nova (sempre há ≥ 1 aba). Fechar a ativa ativa a vizinha da direita (ou da esquerda). Botão do meio fecha. |
| D12 | Fora do escopo: reordenar por arrastar, fixar aba, sincronizar entre dispositivos, abas em login/telas públicas (`/handover/:card`, `/first-access`). |

## Critérios de aceite

- Criar até 10 abas; o + desabilita no limite.
- Trocar de aba é instantâneo e mantém campos digitados, rolagem e painéis abertos.
- Dois cards em duas abas de Pull Request mostram cada um o seu conteúdo.
- Aba parada > 10 min congela (esmaecida/❄) e reativa ao clicar; a ativa e as com edição não salva não congelam.
- F5, logout e login devolvem as mesmas abas, a mesma ativa.
- Aba de card mostra `#card · título`, plano e PRs, e fica amarela quando aguarda o usuário; trunca sem quebrar o layout, inclusive no celular.
- Tela de Pull Request não perde altura útil além da faixa de ~30 px.
