# Status — Feature 0073 (busca da ER fora da memória)

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B0 | ✅ baseline medido (abaixo); `unaccent`/`pg_trgm` deixaram de ser necessários | Claude | 232354b |
| B1 | ✅ repositório: `QueryIndexAsync`, `StreamSearchCandidatesAsync`, `GetIndexItemsByRefsAsync`, `FindIndexItemsAsync`, `GetIndexReferrersAsync`, `GetIndexImpactAsync`, `GetIndexCitingModuleAsync`, `GetIndexModuleKeysAsync` (SQL no `KnowledgeRepository.Index.cs`; padrões em memória na interface) | Claude | ver PR |
| B2 | ✅ migração `SearchText0073` (função `search_normalize` + 5 colunas geradas + 3 GIN `jsonb_path_ops`; sem extensões) | Claude | ver PR |
| B3 | ✅ `ReverseSearch.RunAsync` em dois estágios; sem `Snapshot` estático (só o glossário em cache, pela marca do índice) | Claude | ver PR |
| B4 | ✅ os 8 usos de `EntriesAsync` migrados (`StartSession`, `Publish`, `Search` sem termo, `GetItems`, `GetAnswerContext`, `Impact`, `ForCard`, `RenderRelated`); `EntriesAsync`/`StreamIndexEntriesAsync` removidos | Claude | ver PR |
| T1 | ✅ 201 testes (6 novos); comparação com a master e estresse no harness local (abaixo) | Claude | ver PR |
| T2 | ⏳ depois do deploy: logs do Cloud Run por 3 dias úteis; decidir se a memória volta a 1 GiB | — | — |

## Contexto / handoff

- 2026-10-27: OOM em `ReverseEngineering` (1 GiB, revisão 00136). Paliativo: 2 GiB (PR #133, revisão 00137, aplicado com
  terraform; state local com backup `terraform.tfstate.pre-2gi`). Spec escrita a partir dos logs do Cloud Run e da leitura de
  `ReverseSearch.cs`; a causa exata do pico (EF/JSON × concorrência) ainda não foi medida — é o B0.
- Harness local da 0070 em `.t0070/` (Postgres isolado porta 55470, Base real semeada) serve de ponto de partida.

## B0 — medições locais (fora do git em `.t0073/`)

Postgres 18 isolado (docker `cime-pg-0073`, porta 55473) + API da master (`feature/0073` = master + docs), `DOTNET_PROCESSOR_COUNT=1`,
`DOTNET_GCHeapHardLimit` simulando o container. Índice semeado a partir do espelho `~/.claude/solvace-kb` (`seed_index.py`: corpo =
bloco do item na seção `re-*`, tags/tabelas = TSV): **59.677 itens, 52,5 M caracteres, 31 módulos** (maior corpo: 30 mil caracteres).
Tabela em `knowledge."ReverseIndexEntries"` (schema `knowledge`, `KnowledgeContext`) — não `prform`.

| medida | master |
|---|---|
| heap vivo (depois de GC completo), instância recém-subida | 17 MB |
| heap vivo depois da **primeira** busca da ER (cache montado) | **505 MB** (RSS 531 MB) |
| primeira busca (instância fria) | 4,8–5,9 s (6 em paralelo: todas ~5,4 s, `HeavyReads` serializa) |
| busca seguinte (cache quente) | 0,14–0,18 s |
| estresse (3 × 9 em paralelo), heap limitado a 768 MB (≈ 1 GiB) | 0 OOM, pico RSS 749 MB |
| idem, heap 512 MB | 0 OOM, pico RSS 657 MB (vivo 515 MB — no limite) |
| idem, heap 384 MB | **10 OutOfMemory**, 500s |

Leitura: o cache sozinho custa ~0,5 GB de heap vivo e cresce com a Base (≈ 9,6 bytes de heap por caractere de corpo). Com 1 GiB de
container sobra pouco para o resto (cache da Base, documentos grandes das `sessions`, buffers do Npgsql, até 80 requests
concorrentes) — por isso o OOM em produção, que o harness só reproduz abaixo de 512 MB de heap porque aqui o índice é menor e
a Base (`Architecture`) não está semeada (várias rotas do estresse deram 404). 2 GiB deixa folga, mas o custo continua
proporcional ao tamanho da Base.

**Viabilidade do full-text (local):** coluna `tsvector` gerada (`simple` + `unaccent` imutável, pesos A=id/título, B=tags/sinônimos,
C=corpo) + índice GIN: **8,8 s** para preencher e **1,5 s** para o índice, **29 MB** de índice (tabela inteira 189 MB), consulta de
candidatos em **~1,3 ms**. Atenção para o B1: o filtro precisa ser OU entre os termos (e prefixo `:*`) — `regra & desconto` (E)
devolve 0 linhas; o ranking final continua no app.

**Atualização:** a busca por substring do ranking atual não cabe no full-text (tokens), e `pg_trgm` perdeu para a varredura (T1) — a solução final não usa extensão nenhuma, então o `unaccent` não é mais pendência.

## T1 — resultados finais (local, `.t0073/`)

Mesmo banco (60 mil itens) para a master (`fix/api-2gi`, porta 5174) e a feature (5173); `DOTNET_PROCESSOR_COUNT=1`.

| | master | feature |
|---|---|---|
| heap vivo depois de buscar | **506 MB** | **32 MB** |
| primeira busca numa instância fria | 4,8–5,9 s (todas as 6 em paralelo ~5,4 s) | 0,5–1,2 s (6 em paralelo), RSS 190 MB |
| busca com 1 termo, quente | 0,05–0,2 s | 0,2 s |
| busca com 2 termos ("regra de desconto"), quente | 0,1 s | 0,45 s |
| busca com 8 termos, quente | 0,2 s | 0,5 s |
| estresse (3 × 9 em paralelo), heap 384 MB | **10 OutOfMemory**, 500s | **0 OOM**, pico RSS 400 MB |
| idem, heap 256 MB | — | 0 OOM, pico RSS 350 MB |
| 252 comparações (busca com 4 variações, `items`, `impact`, `for-card`, listagens) | — | 0–2 diferenças por rodada |

- **Equivalência:** o conjunto de consultas é sorteado a cada rodada (`order by random()`); em 5 rodadas deu 1, 0, 0, 2 e 0 diferenças
  de 252. As diferenças são sempre na **cauda** (posição ~30–100) de consultas longas/amplas: um item de score quase igual entra no
  lugar de outro porque a pontuação aproximada do banco (corpo conta 1,4 por termo, o ranking conta até 3,8) não é idêntica ao
  ranking e o corte é em 1000 candidatos. Topo, `items`, `impact` e `for-card` ficaram idênticos.
- **Custo:** a busca quente é mais lenta que a da master com cache (0,2–0,5 s contra 0,05–0,2 s; consulta com termo muito comum, como
  "regra", varre ~25 mil linhas), mas a primeira consulta de cada instância deixou de custar 5 s e 0,5 GB. O banco responde por ~80% do tempo
  (`Server-Timing`).
- **Índice `pg_trgm` testado e descartado:** com ele a busca de 8 termos levou 1,6 s (bitmap + recheck lendo 25 mil linhas da tabela
  larga); sem ele, 0,5 s. Tabela estreita lateral (+1,5× de ganho) também descartada pela complexidade (trigger).
- **Migração:** ~15 s para 60 mil itens no Postgres local; a tabela vai de 73 MB para 143 MB (colunas geradas). Roda sozinha na
  subida da revisão nova (trava de migração; falha é fatal) — o Postgres de produção é remoto, o tempo pode ser maior.
- **Flaky já existente:** `SectionBlocksTests.Excerpt_de_secao_grande…` falha às vezes (master: 4 de 25 rodadas; feature: 2 de 25) —
  corrida do cache estático de `ArchitectureSearch` entre testes em paralelo; não é da 0073.

## Log

- 2026-10-27 — feature aberta (spec/plan/status).
- 2026-10-27 — B0: baseline local (cache 505 MB vivos, OOM a 384 MB de heap) e prova do full-text (GIN 29 MB, ~1,3 ms).
- 2026-10-27 — B1–B4 e T1: busca da ER no banco; heap vivo 506 → 32 MB; estresse com 384 MB sem OOM (master: 10). Falta o deploy (T2).
