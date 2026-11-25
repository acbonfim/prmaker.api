# 0081 — Ajustes do Especialista do caso + Tour guiado da tela do card

Pedido do usuário (2026-11-04), depois do primeiro uso real da 0080. Muitos usuários estão usando a tela de forma
errada (comentário como chat, cancelar em vez de conversar, esperar o Claude sem saber o que ele faz) e isso passa a
imagem de que o sistema não funciona.

## 1. "Claude rodando" vira ícone no cabeçalho do plano
- A faixa "Claude rodando em <máquina> desde ..." (e "Na fila do Claude ...") sai do corpo do plano e vira um ícone no
  cabeçalho, ao lado do ícone de "sem sinal do Claude".
- Enquanto o Claude trabalha o ícone fica animado (se mexendo); na fila, pulsa devagar; parado, fica discreto.
- Clicar abre um popover com tudo o que a faixa mostrava: máquina, desde quando, tentativa, origem/quem pediu, o que
  está fazendo agora, as últimas atividades (ao vivo), e as ações (Parar/Cancelar, Começar agora, Rodar mesmo assim,
  Meus executores).
- O ícone existe sempre que há plano (parado = "o Claude não está rodando agora" + "Pedir ao Claude"), para o
  usuário sempre saber onde olhar. A faixa vermelha de falha continua no corpo (é um erro que pede ação).

## 2. Especialista do caso
- 2.0 Fechar a conversa mantém o estado em memória (mensagens, rascunho, arquivos anexados ainda não enviados, resposta
  a, imagens já baixadas, posição da rolagem). Abrir de novo é instantâneo, sem recarregar.
- 2.1 Mensagens novas chegam por WebSocket mesmo com a conversa fechada/minimizada (a tela do card mantém a
  assinatura do card; o modal e a versão compacta leem o mesmo estado).
- 2.2 A linha do tempo sai da coluna da direita e vira um botão "Linha do tempo" ao lado do "Root Cause". Clicar abre
  a linha do tempo direto em tela cheia (o mesmo componente, com rascunho e tempo real).
- 2.3 A coluna da direita passa a ser a conversa com o especialista em versão compacta, ocupando o espaço da antiga
  linha do tempo: mensagens com markdown, o que ele está fazendo, quem está digitando, proposta pendente ("Ver o que
  muda"), ações aguardando confirmação, pontos combinados, arquivos, e um campo para escrever (rascunho compartilhado
  com o modal). "Expandir" abre o modal grande. No celular a aba "Linha do tempo" vira "Especialista".

## 3. Tour guiado (Onboarding / Product Tour) da tela do card
- Spotlight (máscara escura com recorte no elemento) e balões explicativos, passo a passo, com "Pular tour" em todos os
  passos, Voltar/Próximo, teclado (setas/Esc) e progresso "3 de 12".
- Abre sozinho só na primeira vez que o usuário entra na tela depois da atualização (flag por usuário no
  localStorage, com versão do tour). Quando o tour ganha passos novos (versão nova), quem já viu recebe só as
  novidades.
- Logo ao entrar na tela de pull requests. Sem card aberto, mostra os passos de entrada e pede para buscar um card;
  quando o plano carrega, continua com os passos do card.
- Os passos apontam para os elementos reais da tela (`data-tour="..."`), não para imagens: quando a tela muda o tour
  acompanha; passo cujo elemento não existe no momento é pulado; em desenvolvimento, passo sem elemento gera aviso.
- Responsivo: balões reposicionam ao redimensionar/rolar; no celular os passos da coluna da direita abrem a aba certa.
- "Como usar esta tela" (botão de ajuda no topo): guia completo com todas as instruções, gerado do mesmo cadastro de
  passos do tour, com "Mostrar na tela" em cada item e "Rever o tour".
- Biblioteca: **driver.js** (MIT, sem dependências, ~5 kB gz, spotlight + popover, reposiciona sozinho, acessível por
  teclado, independe de framework). Shepherd.js e Intro.js têm licença AGPL/comercial; ngx-ui-tour depende de
  versões específicas do Angular/Material.
