# Plano — Feature 0050

Branch `feature/0050` em `prform.api-0050` (API, executor, skill) e `prform-app-0050` (front).

| Fase | O quê | Depende |
|---|---|---|
| B1 | Atividade: `ExecutionRequest.CurrentActivity`/`CurrentActivityAt`/`RecentActivities` (JSON, máx. 10, migração `ExecutionContext`); `ExecutionRequestHeartbeatRequest` aceita `activity`/`recent`; `ExecutionQueueApplication.HeartbeatAsync` grava sem tocar `LastActivityAt`, sanitiza (120 caracteres, descarta o que parece segredo) e publica `ExecutionPlanRealTimeEvents` novo (`activity`); limpa no `finish`; plano/fila devolvem a atividade e o estado da fila (na fila desde, executor, tentativa); `prmake_queue` (MCP) com a atividade | — |
| B2 | Arquivos por fase: `ExecutionArtifactKind.Ticket` (`chamado-*.md` inferido pelo nome); `UploadArtifactAsync` recusa no plano de análise `script` que altera dados (SQL fora de comentários: `UPDATE`/`INSERT`/`DELETE`/`MERGE`/`TRUNCATE`/`ALTER`/`DROP`/`CREATE`/`EXEC`/`CALL`) e `ticket` — mensagem de regra (`Safe(...)` no MCP, 400 no REST); `prmake_file` documenta `ticket` e `phase` | — |
| B3 | Etapa do chamado: arquivos vinculados por `key` na resposta do plano/contexto (por etapa); etapa `kind=ticket` só fica aguardando o usuário com `script` + `ticket` da `key` dela (advance/step/MCP) — mensagem de regra | B2 |
| E1 | Executor 1.0.8: no `JobRunner` (leitura do `stream-json`), `tool_use` → rótulo pt-BR (classe `ActivityLabel`, tabela da spec; repo pelo `RepoMap`; nunca o comando cru), últimas 10, envio no heartbeat e na hora da mudança (mínimo 5 s); subir `<Version>` | B1 (contrato) |
| S1 | Skill `analisar-bug`: `SKILL.md` ("Arquivos do card sempre no plano"), `references/correcao.md` (passos 1–2, tabela da etapa `chamado-<nome>`), `references/consultas.md` — análise só `00_consulta-*.sql` comentadas + análise + dados; script de alteração, rollback, validação e `chamado-<nome>.md` (`ticket`) na correção, na etapa do chamado; `propor-solucoes` descreve o script e o checkpoint leva o necessário; citar os arquivos anexados pelo nome (etapa, mensagem final, Timeline) | B2, B3 |
| F1 | Front: linha de atividade sob a etapa em andamento (ou no topo), "há X s" local, popover com as 10 últimas, "sem novidade há X min" (> 3 min), estado da fila; evento realtime `activity` | B1 |
| F2 | Front: tipo `ticket` no rodapé ("Chamados") e no .zip; na etapa `ticket`, bloco "Texto do chamado" (Copiar título / Copiar corpo / Baixar script(s)) acima do link; selo "📎 N arquivos" nas etapas com arquivos; conferir que cada aba mostra só os arquivos do seu plano | B2, B3 |
| T1 | Harness local (Postgres isolado + API local + executor contra API local, `fake-claude` emitindo `tool_use` com SQL/credencial no comando): rótulos chegam e sem segredo; fila/atualização visíveis; `UPDATE` na análise recusado; etapa `ticket` sem arquivos recusada; com arquivos, tela copia/baixa; plano antigo continua abrindo | todas |

Ondas: **1** — B1, B2 (paralelas). **2** — B3, E1. **3** — S1, F1, F2. **4** — T1.

Notas:
- Executor: só o `<Version>` dispara a autoatualização; mandar campos novos no heartbeat é compatível com a API antiga
  (campos a mais são ignorados), então E1 pode ir antes ou depois do deploy de B1.
- Regras de B2/B3 valem só para gravações novas — nada de mover arquivos de planos antigos (75067 incluído).
- Perguntas em aberto da spec (modelo `TicketTemplate`, alias do host no rótulo, guardar atividades depois do
  `finish`) — decidir antes de B1/E1; sem resposta: sem `TicketTemplate`, só "Consultando o banco", limpar no `finish`.
