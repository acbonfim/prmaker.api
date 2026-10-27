# Plano — Feature 0073 (busca da ER fora da memória)

Branch `feature/0073` (worktree `prform.api-0073`). Só backend. Retrocompatível: rotas e respostas iguais.

| Fase | O quê | Depende |
|---|---|---|
| B0 | Baseline: reproduzir o OOM no harness da 0070 (Postgres isolado + Base real + `GCHeapHardLimit`), medir pico/heap/tempo por rota (`index/search`, `items`, `impact`, `for-card`, `docs/*/sessions`); verificar `unaccent`/`pg_trgm` no Postgres do MonsterASP (pedir ao usuário: leitura de prod é bloqueada) | — |
| B1 | Repositório: `GetIndexHeadsAsync(filtros)` (sem `Body`/`Evidence`), `GetIndexEntriesByRefsAsync`, `SearchIndexCandidatesAsync(termos, módulos, tipos, docType, limite)`, glossário `GLO` por módulo | B0 |
| B2 | Migração `KnowledgeContext` (schema `knowledge`): coluna de busca + índice GIN (ou coluna normalizada + trigram, conforme B0) em `ReverseIndexEntries` | B0 |
| B3 | `ReverseSearch.RunAsync` em dois estágios (candidatos no banco → ranking atual só neles); sem `Snapshot` estático | B1, B2 |
| B4 | Migrar os 8 usos de `ReverseSearch.EntriesAsync` (`StartSession`, `Publish`, `Search`, `GetItems`, `GetAnswerContext`, `Impact`, `ForCard` x2) para consultas direcionadas; remover `EntriesAsync`/`Invalidate` | B1 |
| T1 | Testes unitários (`solvace.knowledge.tests`) + local: equivalência antiga × nova, estresse com 384 MB, medição antes/depois | B3, B4 |
| T2 | Depois do deploy: logs do Cloud Run (OOM, p50/p95 por rota, cold start) por 3 dias úteis; propor voltar a 1 GiB | merge |

Ondas: B0 → (B1 ∥ B2) → (B3 ∥ B4) → T1 → T2.

Riscos: (1) ranking diferente do atual — mitigado por manter o ranking em app e trocar só a seleção de candidatos;
(2) extensão `unaccent` indisponível no banco gerenciado — plano B de normalizar na gravação; (3) migração em tabela com
~24 mil linhas e textos grandes — rodar fora de horário e medir o tempo do `CREATE INDEX` no harness.
