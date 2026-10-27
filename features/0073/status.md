# Status — Feature 0073 (busca da ER fora da memória)

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B0 | 🟡 baseline medido e viabilidade do full-text provada; falta checar `unaccent` no Postgres do MonsterASP (usuário) | Claude | — |
| B1 | ⏳ pendente | — | — |
| B2 | ⏳ pendente | — | — |
| B3 | ⏳ pendente | — | — |
| B4 | ⏳ pendente | — | — |
| T1 | ⏳ pendente | — | — |
| T2 | ⏳ pendente | — | — |

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

**Pendente do usuário (B0):** no Postgres de produção (db70152), rodar:

```sql
select name, default_version, installed_version from pg_available_extensions where name in ('unaccent','pg_trgm');
select rolname, rolsuper, rolcreatedb from pg_roles where rolname = current_user;
```

`unaccent` e `pg_trgm` são extensões "trusted" no PG 13+: o dono do banco consegue `CREATE EXTENSION`. Se `unaccent` não estiver
disponível, o plano B é normalizar o texto na gravação (coluna já sem acento).

## Log

- 2026-10-27 — feature aberta (spec/plan/status).
- 2026-10-27 — B0: baseline local (cache 505 MB vivos, OOM a 384 MB de heap) e prova do full-text (GIN 29 MB, ~1,3 ms).
