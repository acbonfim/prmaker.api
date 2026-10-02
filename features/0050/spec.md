# 0050 — Atividade ao vivo do executor e arquivos de cada fase no plano certo

## Contexto
Três coisas apareceram no card 75067 (02/10/2026):

1. **Parecia travado, mas estava trabalhando.** O pedido "pode continuar, já tem acesso" foi pego às 12:33:49 (sessão
   nova, tentativa 1/3). A etapa `causa-raiz` passou minutos consultando o banco e fazendo `grep` no `edv-solvace`, e a
   tela não mudou nada nesse tempo — o plano só se mexe quando a skill chama `prmake_advance`/`block`/etc. Antes disso o
   pedido ainda ficou ~20 min parado porque o executor se atualizou para a 1.0.7 às 12:10 e devolveu os pedidos para a
   fila — também invisível na tela. O executor já tem tudo o que precisa: lê o `stream-json` do Claude linha a linha
   (`tools/Cime.ExecutionAgent/JobRunner.cs` ~321, cada ferramenta usada chega como `tool_use`) e manda o sinal de vida
   a cada 30 s (`JobRunner.cs` ~149, `HeartbeatAsync`).
2. **O texto do chamado passou despercebido.** A análise concluiu que o caso pedia correção de código + chamado
   (script de dados) e perguntou ao usuário. O `.sql` **e** o texto do chamado foram anexados aos arquivos do plano,
   mas nada disse isso: a pergunta e a mensagem final citavam só o script, e o texto do chamado ficou misturado com os
   outros arquivos no rodapé, como mais um anexo. O usuário só viu o `.sql` e achou que faltava o texto. A regra de
   gerar existe (`skills/analisar-bug/SKILL.md` "Arquivos do card sempre no plano" e a tabela de etapas em
   `references/correcao.md`); o que falta é **mostrar** o que foi anexado, no lugar onde o usuário vai usar.
3. **O script de correção ficou no plano errado.** O script que altera dados (e o texto do chamado) foi gravado no
   plano de **análise**, porque foi gerado na `propor-solucoes`, antes de existir o plano de correção (cada arquivo
   pertence a um plano — `ExecutionArtifact.PlanId`). Mas rodar o script é execução: o lugar dele é o plano de
   **correção**, junto da etapa do chamado. No plano de análise deveriam ficar só as consultas somente leitura que
   levaram ao problema (os `00_consulta*.sql` de `references/consultas.md`).

## Objetivo

### Parte A — o que o Claude está fazendo agora, na tela do card

1. **Executor 1.0.8** (subir `<Version>` para autoatualizar): de cada `tool_use` do `stream-json`, gerar um **rótulo
   curto em pt-BR**, sem nunca repassar o comando cru:

   | Ferramenta / comando | Rótulo |
   |---|---|
   | script de consulta ao banco (`consultas`, `cognito-query.sh`, cliente SQL) | "Consultando o banco" (+ alias do host, ex. `prod1`, se a pergunta em aberto abaixo confirmar) |
   | `Grep`, `grep`, `rg` | "Procurando “<termo>” no <repo>" — termo cortado em 40 caracteres, repo pelo mapa da 0048 |
   | `Read` | "Lendo <arquivo>" (nome do arquivo + repo, sem caminho absoluto) |
   | `Glob`, `find`, `ls` | "Listando arquivos em <repo>" |
   | `Edit`, `Write` | "Editando <arquivo>" |
   | `git` / `gh` | "Git: <subcomando>" / "GitHub: <subcomando>" |
   | Base Solvace / Knowledge Center (`kc`, `solvace-kb`) | "Consultando a Base Solvace" / "Consultando o Knowledge Center (ART-n)" |
   | `mcp__prmake__*` | pelo nome: "Lendo o card no DevOps", "Gravando arquivo no plano", "Atualizando o plano"… |
   | `Agent`/`Task` | "Subagente: <descrição>" |
   | qualquer outro | "Executando comando" |

   Envio: a atividade atual e as **últimas 10** vão junto no heartbeat (30 s) e também **na hora em que mudam**, com
   intervalo mínimo de 5 s entre envios. Falha de envio não atrapalha a execução.
2. **API**: o heartbeat (`ExecutionQueue`) aceita `activity { label, tool, at }` e `recent[]`. Guardar no
   `ExecutionRequest` (`CurrentActivity`, `CurrentActivityAt`, `RecentActivities` em JSON, máx. 10 — sem tabela de
   histórico). **Não** tocar no `LastActivityAt` (mesma armadilha da 0049: atrapalha a retomada). Defesa em
   profundidade: cortar o rótulo em 120 caracteres e descartar o que parecer segredo (`password=`, `pwd`, `-p<algo>`,
   connection string, token). Publicar pelo realtime no grupo do card (`execution.activity`). Limpar no `finish`.
   O `prmake_queue` (MCP) passa a devolver a atividade atual.
3. **Tela do card**:
   - Embaixo da etapa em andamento (ou no topo do plano, quando nenhuma etapa está *running*): ícone girando + rótulo +
     "há 12 s" (contado no navegador). Clique → popover com as últimas 10 atividades e o horário de cada uma.
   - Sem atividade nova há mais de 3 min, com o processo vivo: "Sem novidade há X min — o Claude pode estar pensando
     ou esperando um comando terminar".
   - **Estado da fila visível**, antes de o Claude começar: "Na fila desde 12:10 — aguardando o executor de
     `<máquina>`", "Executor atualizando para 1.0.7", "Tentativa 2/3 às HH:mm". (No 75067 foi esse intervalo que mais
     pareceu travamento.)

### Parte B — cada arquivo no plano da sua fase

1. **Análise = consultas, correção = execução.**
   - Plano de **análise**: só as consultas somente leitura que levaram ao problema (`00_consulta-<assunto>.sql`, com
     um comentário no topo dizendo o que cada uma mostrou), a análise (`.md`) e os dados extraídos (`.csv`/`.json`).
   - Plano de **correção**: script que altera dados (`01_<nome>.sql` com rollback), consulta de validação pós-execução
     e texto do chamado. Gerados na etapa do chamado da correção (ou na que a prepara), com `key` = essa etapa.
   - Na `propor-solucoes` o script **não** é gravado: a opção com chamado descreve o que ele vai fazer (tabelas,
     filtros, linhas afetadas estimadas) no `solucoes.md`, e o checkpoint leva o que a correção precisa para escrevê-lo
     (ids, consultas usadas, host/banco) — a correção roda numa sessão nova (0049).
2. **Skill (`analisar-bug`)**: atualizar `SKILL.md` ("Arquivos do card sempre no plano"), `references/correcao.md`
   (passos 1–2 e a tabela da etapa `chamado-<nome>`) e `references/consultas.md` com a divisão acima; na análise,
   `prmake_file(..., phase: "analysis")` só para consultas/análise/dados; na correção, `phase: "correction"`.
3. **API (guarda)**: `prmake_file`/upload de arquivo `script` no plano de **análise** com comando que altera dados
   (`UPDATE`, `INSERT`, `DELETE`, `MERGE`, `TRUNCATE`, `ALTER`, `DROP`, `CREATE`, `EXEC`/`CALL` — fora de comentários)
   é recusado com mensagem de regra (`Safe(...)`): "script que altera dados vai para o plano de correção
   (`phase: \"correction\"`, na etapa do chamado); na análise ficam só as consultas somente leitura". Tipo `ticket`
   (item 4 abaixo) no plano de análise: mesma recusa.
4. **Arquivo do chamado reconhecível**: tipo novo `ticket` em `ExecutionArtifactKind` (hoje o texto entra como
   `script`/`analysis` e se mistura). Nome padrão `chamado-<nome>.md`, gravado com
   `prmake_file(card, "chamado-<nome>.md", texto, "ticket", "<etapa>", phase: "correction")`. Rodapé com botão próprio
   "Chamados" e pasta no .zip. Primeira linha = título do chamado; corpo com o link do card, cliente, ambiente, banco,
   o que o script faz, rollback e a consulta de validação.
5. **Skill diz o que anexou**:
   - Na descrição da etapa do chamado: "Abra o chamado no Freshservice com o texto de `chamado-x.md` e anexe
     `01_x.sql`".
   - Na mensagem final da vez (terminal) e na Timeline: "Anexei ao plano de correção: `01_x.sql` (script + rollback),
     `chamado-x.md` (texto do chamado)". Na análise, o mesmo para as consultas.
6. **API**: o `prmake_plan`/contexto devolve, por etapa, os arquivos vinculados pela `key`. Regra: uma etapa
   `kind=ticket` **não fica aguardando o usuário** sem um arquivo `ticket` e um `script` com a `key` dela — mensagem de
   regra: "anexe o script e o texto do chamado (`prmake_file(..., \"<etapa>\", phase: \"correction\")`) antes de
   liberar a etapa".
7. **Tela**: o arquivo aparece **onde ele é usado**, não só no rodapé:
   - Na etapa `ticket`: bloco "Texto do chamado" com **Copiar título**, **Copiar corpo** e **Baixar script(s)**, e o
     campo de link do chamado (que já existe) logo abaixo.
   - Em qualquer etapa que gravou arquivos: selo "📎 N arquivos" que abre a lista (na análise, as consultas).
   - Rodapé de cada plano mostra só os arquivos daquele plano (já é assim pelo `PlanId`; conferir que a aba
     Análise não mostra os da correção e vice-versa).

## Fora do escopo
- Abrir o chamado automaticamente no Freshservice (continua manual; o usuário cola o link na etapa).
- Mostrar o texto ou o raciocínio do Claude na tela — só os rótulos da Parte A.
- Mudar as skills para reportar atividade: a Parte A é só executor + API + tela.
- Mover arquivos já gravados em planos antigos (o 75067 incluído) — a regra vale daqui para frente.

## Perguntas em aberto
- O texto do chamado que a skill gera hoje está bom, ou vale um modelo configurável no PRMake ("Skills
  Configurations" → `TicketTemplate`) com os campos do Freshservice (categoria, prioridade, grupo)?
- No rótulo do banco, mostrar o alias do host (`prod1`) ou só "Consultando o banco"?
- Guardar as atividades depois do `finish` (para ver depois o que a sessão fez) ou só enquanto roda?

## Critérios de aceite
- Com o executor rodando um card, a tela mostra um rótulo novo em até ~5 s depois de cada ferramenta usada, e o
  "há X s" continua contando entre elas.
- Nenhum rótulo traz host, usuário, senha, SQL ou caminho absoluto (testar com uma consulta que leva credencial no
  comando).
- Pedido na fila ou executor atualizando aparece como tal, não como "rodando" parado.
- Card com solução que inclui chamado: o plano de análise fica só com consultas somente leitura, análise e dados;
  o `.sql` de alteração, o rollback e o `chamado-<nome>.md` (tipo `ticket`, título na primeira linha, link do card)
  aparecem no plano de correção, na etapa do chamado, e a mensagem final e a Timeline citam os arquivos pelo nome.
- Gravar um `UPDATE`/`DELETE` (ou um `ticket`) no plano de análise devolve a mensagem de regra.
- Na etapa `ticket`, dá para copiar título e corpo e baixar o script sem abrir o rodapé.
- Tentar deixar uma etapa `ticket` aguardando o usuário sem o texto do chamado devolve a mensagem de regra.
