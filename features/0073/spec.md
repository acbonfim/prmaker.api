# Feature 0073 — Busca da engenharia reversa fora da memória da API

## Problema

Em 26–27/10/2026 os endpoints de `ReverseEngineering` (`docs/*/sessions`, `index/search`, `items`) devolveram **500 com
`OutOfMemoryException`** em 10–23 s, repetidamente, na `cime-pullrequest` (revisão 00136, **1 GiB**). O fix `f5677bc`
(cache da busca montado em fluxo) já estava em produção desde 14/10. O erro é determinístico: toda instância fria
(scale-to-zero + `max_instances=2`) precisa remontar o cache da busca e o pico passa do limite; repetir não adianta.

Paliativo já aplicado: memória da API de 1 GiB → **2 GiB** (PR #133, revisão 00137). Esta feature remove a causa.

## Causa (no código)

`ReverseSearch.EnsureAsync` (`Solvace.Knowledge.application`) mantém num estático o índice **inteiro** da ER
(`ReverseIndexEntries`, ~24 mil itens, ~22–29 milhões de caracteres): `StreamIndexEntriesAsync` materializa cada entidade
com o `Body` e as 6 listas JSON (`Tags`, `Tables`, `Refs`, `Evidence`, `Modules`, `Synonyms`); depois guarda por item o
`Body` normalizado, `Title`/`Tags`/`Tables` normalizados e as palavras de título/tags. Resultados:

- pico de memória a cada instância fria (a pilha do OOM está na leitura das colunas JSON do EF);
- o pico soma com `Architecture`/Base, `HeavyReads`, até 80 requests concorrentes na mesma instância;
- `EntriesAsync` (8 usos em `ReverseEngineeringApplication`) devolve a lista inteira só para filtrar poucos itens;
- cada publicação (`Invalidate`) volta a custar a remontagem inteira.

## Objetivo

Nenhum request da ER monta o índice inteiro na memória. A busca e as consultas por item trabalham no Postgres e trazem
só os candidatos. Resultados da busca equivalentes aos de hoje (mesmos itens no topo), com memória da API **independente
do tamanho da Base**.

## Escopo

1. **Medir antes** (B0): reproduzir o OOM localmente (harness da 0070: Postgres isolado com a Base real semeada,
   `DOTNET_GCHeapHardLimit` simulando o Cloud Run) e registrar o baseline por rota (pico, heap vivo, tempo). Sem baseline não
   há como provar a melhora.
2. **Consultas leves no repositório**: projeção "cabeça" do índice (módulo, doc, id, tipo, título, nível, ordem, removido,
   tags, tabelas, módulos — sem `Body`/`Evidence`), consulta por refs, por tipo e por módulo no banco.
3. **Busca em dois estágios**: o Postgres escolhe os candidatos (`LIKE '%termo%'` sobre uma coluna gerada, sem acento/caixa, com os
   termos já expandidos pelos sinônimos do glossário; os candidatos saem ordenados por uma pontuação aproximada com os mesmos
   pesos do ranking e cortados em 1000); o ranking atual (`ArchitectureSearch`/`ReverseSynonyms`) roda **só nos candidatos**. O
   ranking é por **substring** do texto normalizado, por isso o full-text do Postgres (tokens) não serve. Só o glossário
   (itens `GLO`, pequenos) fica em cache, validado pela marca do índice.
4. **Trocar os 8 usos de `ReverseSearch.EntriesAsync`** por consultas direcionadas e aposentar o `Snapshot` estático
   (`Invalidate` vira no-op ou some).
5. **Migração** `KnowledgeContext` (schema `knowledge`): coluna/índice de busca em `ReverseIndexEntries`. Verificar antes se o
   Postgres do MonsterASP permite `CREATE EXTENSION unaccent` (extensão "trusted"); senão, normalizar na gravação
   (coluna de texto já sem acento) e usar `simple`.
6. **Testes**: equivalência antiga × nova no conjunto semeado (topo-N igual ou diferença explicada), unitários em
   `solvace.knowledge.tests`, estresse com limite de memória menor que o de produção.

## Fora de escopo

- Front-end (as rotas e as respostas ficam iguais; skills, MCP e executor não mudam).
- Busca da Base Solvace (`Architecture`) — já tratada na 0070.
- Trocar o motor de busca por serviço externo.

## Critérios de aceite

- Zero `OutOfMemoryException` no estresse local com `DOTNET_GCHeapHardLimit` de **384 MB** (menor que o 1 GiB que falhou).
- Primeira busca numa instância fria sem remontar nada: p95 ≤ 1 s local; sem pico de memória proporcional à Base.
- Respostas de `index/search`, `items`, `impact`, `for-card`, `answer-context` equivalentes às da master no conjunto semeado.
- Em produção: nenhum OOM nos logs da `cime-pullrequest` por 3 dias úteis; depois disso, decidir se a memória volta a 1 GiB
  (custo) — decisão do usuário.
