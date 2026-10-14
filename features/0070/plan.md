# Plano — Feature 0070 (performance)

Branch `feature/0070` nos dois repos (worktrees `prform.api-0070` e `prform-app-0070`). Merge autorizado pelo usuário
em 2026-10-07 ("pode implementar e fazer o merge, eu não estarei aqui") — back e front mesclados juntos.

Princípio: **retrocompatível**. Rotas e respostas atuais continuam iguais para skills, MCP, executor e espelho; o que é
novo entra como parâmetro opcional ou rota nova.

| Fase | O quê | Depende |
|---|---|---|
| B1 | Transversal: cache do "usuário ativo" no handler x-api-key; compressão Brotli/Gzip (fora `/mcp` e tempo real); `Access-Control-Max-Age` no CORS; `Server-Timing` + log de request lenta com nº de comandos SQL | — |
| B2 | Base Solvace sem carregar o texto da Base inteira: seção/versões por chave; manifest/índice/catálogo pelas cabeças; export por projeto; busca com cache incremental (só baixa seção nova/alterada, descarta versão velha, trecho do banco) | — |
| B3 | Engenharia reversa: `modules` e `modules/{key}` com contagens no banco (sem índice inteiro em memória, sugestões/armadilhas contadas no banco); `docs/{doc}` lendo só a seção pedida e os itens do índice; `?content=false` + `outline` + `parts`; `doc-types?template=false`; ETag/304 em seção e documento; `Architecture/projects` com a configuração lida uma vez | — |
| B4 | Índices: `ReverseIndexEntries(Kind)`, `ReverseIndexEntries(UpdatedAt)`, `ExecutionPlans(CreatedByUserId, Status)`; `pending` sem carregar plano encerrado | — |
| F1 | Tela da ER: evento de andamento atualiza a lista em memória (sem `GET modules` a cada 4 s); lista só recarrega quando o status muda | B3 |
| F2 | Documento sob demanda e virtualizado: `app-lazy-markdown` desmonta pedaços longe da tela; documento publicado vem por `outline` + `parts` | B3 |
| F3 | Seção da Base Solvace com o mesmo componente; `doc-types?template=false` | B3 |
| F4 | Listas: andamento do plano com virtual scroll; timeline do card desenhando só as últimas entradas (carregar anteriores) | — |
| T1 | Testes unitários novos + local (Postgres isolado): rotas antigas iguais, rotas novas, medição antes/depois | B*, F* |
| T2 | Depois do deploy: logs do Cloud Run (p50/p95 por rota, OOM, cold start) | merge |

Ondas: B1, B2, B3, B4 e F4 independentes; F1–F3 depois de B3.
