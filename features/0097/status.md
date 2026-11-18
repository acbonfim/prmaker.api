# Status 0097

- Branch: `feature/0097` (prform.api-0097 e prform-app-0097), a partir de origin/master.
- Merge: autorizado pelo usuário em 2026-11-18 (back e front juntos).

| Fase | Estado | Responsável | Notas |
|---|---|---|---|
| B1 — leitura aberta | ✅ | agente back | e87e668 |
| B2 — exigidos / descontinuados / prática sob demanda | ✅ | agente back | 3015037 + 42dff3e (visao-geral substituída por funcional+arquitetura) |
| B3 — GAP do gerador | ✅ | agente back | c2d4dc4 — migração apaga os GAP do gerador já indexados (digitalobeya: 171/848 no funcional) |
| B4 — índice técnico (API) | ✅ | agente back | fc8d29a — prefixo de objeto do banco é DBO (SQL colidia); IDs com 3 dígitos no mínimo |
| B5 — histórico do card | ✅ | agente back | a3a1394 — backfill do Query pelo título do plano de análise/PromotionItems |
| B6 — itens desatualizados | ✅ | agente back | fa196d6 — publicar `reanchor` mantém as marcas |
| S1 — master obrigatória | ✅ | agente skill | 9904781 |
| S2 — reancorar | ✅ | agente skill | e6e4f6c — legado-rca: 7%/4% das linhas remapeadas, 11 itens desatualizados |
| S3 — índice técnico (skill) | ✅ | agente skill | 5e8a5c3 — legado-rca 87 itens, revamp-users 229, edv-solvace-apps 240; IDs estáveis |
| S4 — documentos + GAP | ✅ | agente skill | 9be2e84 |
| S5 — custo/tempo | ✅ | agente skill | ed84923 — lote/consolidar/fechar/spec/custo; linha de base medida ~102 M / ~87 min |
| S6 — analisar-bug/us | ✅ | agente skill | d2aafdb |
| F1 — rota/menu abertos | ✅ | agente front | a9abeb8 |
| F2 — documentos na tela | ✅ | agente front | e79acc3 |
| F3 — aba Índice técnico | ✅ | agente front | f4a01c6 + 731f0bd (DBO) |
| F4 — itens desatualizados | ✅ | agente front | c084ab6 + 731f0bd (filtro stale=true) |
| Q1 — testes | ✅ | | knowledge 244 · prform 416 · executionplans 258 · SkillTests 72 · ng build ok |
| Q2 — índice/reancoragem em produção | ⏳ | | precisa de ok do usuário |
| Q3 — piloto medido | ⏳ | | precisa de ok do usuário |

## Log

- 2026-11-18 — spec/plano a partir da avaliação da ER (uso real, precisão, consumo e método de geração).
- 2026-11-18 — B, S e F implementados em paralelo; alinhados DBO, `stale=true` e `visao-geral`. Q1 verde.

## Pendente

- Q2/Q3 precisam de ok do usuário (gravam em produção / consomem a assinatura).
- O `lote` nunca rodou com LLM: a Q3 confirma custo, tempo, qualidade e se as áreas cabem com dois documentos por
  subagente (talvez reduzir `areaBudgetKb` no lote).
- Migrações conferidas num Postgres 18 descartável (2026-11-18): config up/down, GAP do gerador apagado só sem evidência (GAP real e RN ficam), backfill do Query pelo título do plano.
- `re-tecnico` entra no espelho/busca de seções das análises (público llm) — só saiu do Pergunte.
- `kb.sh` (base-solvace) não mostra a marca de desatualizado; o `re.sh find/get` mostra.
- Rotas do índice técnico saem só com o segmento (sem o caminho do pai).
- Reancoragem do revamp-users: 29–77 citações ambíguas e 3–26 não achadas (nomes truncados) ficam para o `melhorar`.
