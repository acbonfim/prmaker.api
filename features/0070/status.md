# Status — Feature 0070 (performance)

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ cache do usuário ativo, compressão, preflight em cache, Server-Timing + log de request lenta | Claude | b301cf3 |
| B2 | ✅ Base Solvace sem carregar a Base inteira (seção por chave, cabeças, export por projeto, busca incremental) | Claude | 046a0d3 |
| B3 | ✅ ER: contagens no banco, documento em pedaços (outline/parts), doc-types sem modelo, diff em cache | Claude | 046a0d3 |
| B4 | ✅ índices (ReverseIndexEntries Kind/UpdatedAt, ExecutionPlans CreatedByUserId+Status) | Claude | 046a0d3 |
| B5 | ✅ memória: sem buffers gigantes retidos no ArrayPool (JSON em segmentos, texto em streaming, Normalize em pedaços), cargas pesadas uma por vez, cache da busca menor | Claude | 675f622 + fix for-card |
| F1 | ✅ tela da ER: evento de andamento atualiza a lista em memória | Claude | front 633bfa1 |
| F2 | ✅ documento da ER em pedaços sob demanda + janela de desenho (desmonta o que se afasta) | Claude | front 633bfa1, ce2351d |
| F3 | ✅ seção grande da Base Solvace em janela; sumário só com ## quando há muitos cabeçalhos | Claude | front 633bfa1, ce2351d |
| F4 | ✅ andamento do plano: últimas 200 por lista; timeline do card: últimas 80 (anteriores num clique) | Claude | front 633bfa1 |
| T1 | ✅ local (abaixo) | Claude | — |
| B6 | ✅ cache das leituras da Base por instância, validado por uma marca do banco (1 consulta) — PR de continuação | Claude | ver PR |
| T2 | ✅ deploy #120/#63 (revisão 00122): sem OOM; Server-Timing mostrou ~60 ms por consulta (banco em outra região) → B6 | Claude | — |

## T1 — local (fora do git em `.t0070/`)
Postgres 18 isolado (docker, porta 55470) com a Base real semeada do espelho local (94 projetos, 407 seções, 21,7 M
caracteres, 24 mil itens no índice), auth falso que conta chamadas, master (worktree `prform.api-0070-base`) × feature
com os limites do Cloud Run simulados (`DOTNET_PROCESSOR_COUNT=1`, `DOTNET_GCHeapHardLimit=384 MB`).

**API** (`bench.sh`, `stress.sh`, `memrun.sh`, `/debug/memory` = memória viva depois de GC completo):

| | master | feature |
|---|---|---|
| heap vivo depois das rotas | 339 MB | **169 MB** |
| estresse (sequência do OOM de 07/10 14:06, 3 rodadas × 9 em paralelo) | **61 OutOfMemory**, 500s | **0** |
| `docs/design` (3 M caracteres) — bytes na rede | 3,4 MB | 1,56 MB (gzip) · `?content=false` 115 KB |
| `Architecture/projects` — bytes | 823 KB | 209 KB |
| `doc-types` — bytes | 32 KB | 2 KB (`?template=false`) |
| chamadas ao cime-auth por 54 requisições | 54 | 2 |

Respostas antigas × novas comparadas rota a rota (`compare.py`): iguais (módulos, módulo, documento, doc-types, projetos,
grafo, seções, versões, índice, busca da ER, items, impact, 5 buscas da Base); export: 519 arquivos idênticos (só o
`generatedAt` muda). MCP (`prmake_base_search`/`get`) ok.

**Navegador** (`e2e/e2e.mjs`, Chrome headless, documento `edv-solvace-apps/design`):

| | master | feature |
|---|---|---|
| ER: primeira pintura | 5,2 s | **0,8 s** |
| ER: nós DOM depois de rolar tudo | 298.126 | **16.889** |
| ER: heap JS depois de rolar | 42,9 MB | **23,7 MB** |
| Base Solvace (mesma seção): nós DOM | 594.066 | **25.743** |
| Base Solvace: heap JS | 80,9 MB | **50,2 MB** |
| sumário → último item (ER e Base Solvace) | ok | ok |

Tempo real (`e2e/rt.mjs`): sessão nova + 10 eventos de andamento → 0 recargas de `GET modules` (antes, uma a cada 4 s);
"gerando" aparece na lista.

## B6 — depois do deploy
O `Server-Timing` em produção mostrou o que faltava: ~60 ms por consulta (banco em Salt Lake City, API em us-central1) e
rotas com 6–10 consultas (`modules` 650 ms, `modules/{key}` 900 ms de servidor). As leituras de cabeças de projeto,
módulos, revisões abertas, contagens do índice e sugestões pendentes passam a sair de um cache por instância, validado
a cada requisição por UMA consulta (quantidade + soma do `xmin` das tabelas da Base; o índice da ER por quantidade +
última atualização — sem depender de relógio). Local: `modules` 6 → 1 SQL, `modules/{key}` 10 → 3, `projects` 5 → 1,
`graph` → 1; respostas iguais à master; gravação/andamento numa instância aparece na outra na hora (`invalidate.py`).
Heap limitado a 60% (`DOTNET_GCHeapHardLimitPercent`) segura o RSS abaixo de 512 MB no estresse extremo mas dá 1 OOM
gerenciado; ficou o padrão (75%, 0 OOM) — decidir junto com a memória do Cloud Run.

## T2 — produção depois do #121 (revisão 00123, instância quente, tempo de servidor pelo Server-Timing)
| rota | antes (p50 prod 05–07/10) | agora |
|---|---:|---:|
| `ReverseEngineering/modules` | 1,4 s | 72–81 ms |
| `ExecutionPlan/pending` | 0,2 s (+ ida ao cime-auth) | 125–149 ms |
| `doc-types?template=false` | 0,48 s | 3–18 ms |
| `modules/legado-rca` | 1,5–3 s | 190–198 ms |
| `modules/edv-solvace-apps` | 12 s | 185 ms |
| `docs/funcional?content=false` | 2,2 s (944 KB) | 245–288 ms |
| `sections/guia-o-que-e` | 12,9 s | 188–235 ms |
| `Architecture/projects` | 12 s | 75–104 ms |
| `Architecture/search` | 12,9 s | 266 ms |
| `export/manifest` | 5 s | 190–293 ms |
| `index/search` | 1 s | 1–1,6 s (CPU) → trecho só dos que voltam (#122) |

Primeira chamada numa instância nova ainda paga o aquecimento dos caches de busca (`Architecture/search` ~10 s,
`modules/{key}` ~4 s): agora raro, porque as instâncias deixaram de cair por OOM.

## Notas
- A "chamada dupla" vista no DevTools era o preflight CORS (OPTIONS) — o front e a API estão em domínios diferentes e o
  `x-api-key` exige preflight; agora fica em cache 2 h por URL. Os pedaços vêm em blocos fixos de 4 (URL repetida = cache).
- Causa do OOM que sobrou depois do #119 (achada com dump de heap): buffers do `ArrayPool` compartilhado — o escape do
  JSON alugava 6× o tamanho do documento (64 MB para 3 MB de texto) e o pool guardava um por thread; idem a leitura de
  texto grande pelo Npgsql e o `Normalize(FormD)`. Corrigido sem mudar o JSON gerado (teste de equivalência).
- Não feito (fica para outra demanda): busca no PostgreSQL (`tsvector`), pendências do plano pelo relay em vez do polling,
  memória/min-instances do Cloud Run (F1/F2 da spec — decidir com o custo; 512 MiB ficou suficiente no teste local),
  front e API no mesmo domínio (acabaria com o preflight).
- Bloco "Integrações" da página do projeto vem aberto com todas as ligações (70 no edv-solvace-apps, ~27 mil px) — fora do escopo.
