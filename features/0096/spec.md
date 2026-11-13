# 0096 — Especialista guardião: altera o plano direto, sem proposta para ajuste

Pedido do usuário (2026-11-13, card 78344):

> Em conversa com o especialista eu estou pedindo um ajuste simples e ele se perde, está criando itens de trabalho para
> ajustes simples.
>
> A minha intenção é que o especialista seja o guardião do caso: ele consegue usar todas as ferramentas do PRMake para
> fazer o que o usuário precisa. Vejo hoje que ele ainda tem muitas limitações.

## O que aconteceu no 78344

1. Pedido: reescrever em primeira pessoa o resumo "Aguardando: Excluir os 3 posts…" (situação da 0075 publicada na
   discussion) e atualizar o comentário.
2. O especialista não tinha como mexer na situação/espera (`prmake_handoff` recusada pelo guard; republicar com texto
   editado só pelo botão da tela) → abriu a **proposta #1** (e ainda sobre outro texto, o do fechamento).
3. Aceita a proposta, o Claude do tratamento criou a **v2 do plano de correção**: plano novo → item **#803** igual ao
   **#800** (ainda aguardando), comentário **novo** na discussion (o id do comentário é por plano) e v1 `running` +
   v2 `pending` lado a lado.
4. O texto continuou em terceira pessoa: a frase "feita por quem trata o card (fora do código)" é fixa
   (`CardSituation.Markdown`) e o `prmake_handoff` manda escrever em terceira pessoa.

## Decisões (2026-11-13)

- **Plano direto, na mesma versão.** O que a pessoa pede na conversa e muda o plano (etapa pendente, texto da etapa,
  espera/handoff, situação publicada, perguntas e respostas, arquivos/links/evidências do plano, pausar/retomar) o
  especialista faz **direto, no plano atual**, como quem pediu, com o cartão "feito a pedido de" e registro no plano/Timeline.
  **Versão nova só quando a solução muda** — continua pela proposta aceita por uma pessoa — e ela **reaproveita o item de
  trabalho e o comentário da discussion** da versão anterior (a anterior é encerrada como substituída).
- **Tratamento rodando:** o especialista **pausa o plano, aplica e retoma**; o tratamento continua já lendo o plano novo.
- **Texto da situação/espera em primeira pessoa** (o comentário sai no nome de quem trata o card): "Estou aguardando…",
  "faço fora do código".

## Fora do escopo

- Apagar comentário da discussion do DevOps (o PRMake só cria/atualiza o dele).
- Merge de PR (continua humano).
