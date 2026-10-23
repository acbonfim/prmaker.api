# Plano — 0071

| Fase | O quê |
|---|---|
| B1 | Contexto por item: busca da ER (pergunta + termos da IA, fusão RRF), peso por tipo (UC/TELA/PRF/RN/TUT/FAQ sobem em "como fazer"), expansão por `Refs`, textos inteiros em UMA consulta (`GetAnswerContextAsync`) |
| B2 | Pergunte: usa o contexto por item + seções (blocos por BM25, vários por seção); itens viram resultados com título do cabeçalho (a tela rola até o item) |
| B3 | Prompt para leigo, formato em blocos (`<<<RESPOSTA`), classificação "como fazer" também por regra (não só pela IA) |
| B4 | Velocidade: plano da IA em paralelo com a busca por item; seções só dos projetos prováveis (amplia se faltar); trechos numa consulta; cache de resposta pela pergunta + marca da Base |
| T1 | Testes: pergunta da transferência (corpus real do espelho, se existir + corpus mínimo no repo), fusão, expansão, prompt |
| Q1 | Deploy + 1–2 perguntas reais (sem bateria) |
| F1 | Front (se preciso): etapas de progresso |

Dependências: B1 → B2 → B3; B4 depois de B2; T1 acompanha cada fase.
