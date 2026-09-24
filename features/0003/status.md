# Feature 0003 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0003` em `prform.api` (a partir de `master` com 0001 e 0002). Front: `feature/0003` criada, sem mudanças previstas.
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | `ClaudeService` com o SDK oficial | back | — | 1 | ⬜ | | | | |
| B2 | IA resolvida por requisição + chave pessoal (0002) | back | — | 1 | ⬜ | | | | |
| Q1 | Configuração do plugin, teste real e publicação | ambos | B1, B2 | 2 | ⬜ | | | | |

## Decisões

| # | Tema | Situação | Observação |
|---|---|---|---|
| D1 | Modelo | a confirmar | Recomendação: `claude-haiku-4-5` (≈ US$ 0,04 por geração). Configurável no plugin. |
| D2 | SDK oficial `Anthropic` x HTTP cru | a confirmar | Recomendação: SDK (retries/erros tipados). Os demais provedores seguem em HTTP cru. |
| D3 | Sugestões R3 (saída estruturada) e R4 (prompt no backend) | a confirmar | Fora desta feature; viram features próprias se aprovadas. |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0002 mergeada (#7 back, #3 front) e em produção; `feature/0003` criada a partir de `master` nos dois repos. Plano criado com análise e sugestões (spec 5 e 6). |
