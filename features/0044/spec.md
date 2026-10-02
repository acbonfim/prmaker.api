# Feature 0044 — Consumo do Claude em tokens, sem números acumulados

## Contexto
Avaliando o card 69795 (3 execuções do executor na MESMA sessão do Claude: análise + 2 "continuar"), as telas
pareciam ter gastado muito mais do que gastaram:

1. **Últimos pedidos** mostrava US$ 0,68 / 1,23 / 1,80 — o Claude Code devolve o custo **acumulado** da sessão retomada
   e o executor gravava como se fosse do pedido (soma 3,71; o real foi 1,80). O orçamento do dia somava errado também.
2. **Plano** mostrava 39 respostas: a última foto que a skill mandou no meio da execução, sem as respostas finais.
3. **MCP × script** somava o cache lido como "tokens de entrada" (1,9 M, dos quais 1,75 M com 90% de desconto) e, na
   correção que continua a sessão da análise, somaria a análise de novo.

## Objetivo
- Custo **por pedido** (a diferença para o acumulado do pedido anterior da mesma sessão), inclusive nos pedidos já gravados.
- Consumo **final** da sessão no plano (o executor lê o transcript quando o processo termina).
- Correção que continua a sessão da análise mostra só o que gastou (linha de base = o que a sessão tinha no plano pai).
- Relatório com as partes da entrada.
- Mostrar sempre em **tokens**: entrada, saída e total, e as partes com desconto (cache lido) e com acréscimo (cache
  escrito), com o custo estimado de cada parte.
- Trocar os tooltips de consumo por um **popover** (menu) — mais fácil de ler e funciona no celular.

## Regras
- Preços: "AI Configurations" → `AiModelPricesUsdPerMillion` (0042), agora com `cacheRead`/`cacheWrite` opcionais por
  modelo (padrão 10% e 125% da entrada). `GET api/v1/AiUsage/prices` para a tela.
- Custo informado pelo Claude Code (pedido) é o principal; a tabela de preços dá a estimativa por parte.
