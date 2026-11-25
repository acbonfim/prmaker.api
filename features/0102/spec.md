# 0102 — Base Solvace: perguntas pelo provedor de IA ou pelo executor

Pedido do usuário (2026-11-25):

> quero que seja configuravel pra na base solvace, as buscas e perguntas seja possivel configurar nas minhas integracoes,
> se a busca e todo aquele processamento vai ser feito via api (Provedor de IA), ou se vai ser usado o executor (claude
> code na maquina). Seria uma flag configuravel. Via provedor eh lento e impreciso.
>
> E via executor ele vai usar um modelo mais basico — como sonnet com effort baixo.

## O que precisa

1. Uma opção em **Minhas integrações**: "Perguntas da Base Solvace" → **Provedor de IA** (como hoje) ou **Executor
   (Claude Code na sua máquina)**. Cada pessoa escolhe a sua; o padrão global continua "Provedor de IA".
2. Com **Executor**, o Pergunte (com anexos) e o "Analisar a fundo" da Base Solvace viram um pedido para o executor da
   própria pessoa: o Claude Code consulta a Base pelas ferramentas `prmake_base_*` (busca, itens, módulo, impacto) e devolve
   a resposta na tela — sem a chave de API da pessoa.
3. Modelo do executor nessas perguntas: **Sonnet com effort baixo** (configurável pelo admin; "a fundo" com effort maior).
4. A tela mostra o andamento (na fila / o que o Claude está fazendo) e permite cancelar; sem executor online, explica e
   oferece responder pela IA do servidor desta vez.
