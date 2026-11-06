---
name: analisar-us
description: Faz a analise de uma User Story (US) a partir do card no PRMake/Azure DevOps e do codigo — entende a US (descricao + criterios de aceite), consulta a Base Solvace (engenharia reversa + Knowledge Center) antes do codigo, mapeia onde a US entra, propoe a solucao perguntando ao usuario (responde no PRMake ou no Claude) e no fim gera os itens de trabalho (implementacao = tipo `implementation`; entregas com tipos proprios — nunca item `user-story`) com as sub-tarefas (plano de implementacao → item automatico; entregas separadas → itens proprios). Mesmo ciclo da analisar-bug (plano de execucao em tempo real, executor, retomada), sem causa raiz nem RCA. So abre PRs, nunca faz merge. Use quando o card for User Story ou o usuario pedir "analisar US", "analisar a historia", "analise da user story", "levantar a solucao da US".
model: opus
---

# analisar-us

Leva uma **User Story** da analise aos itens de trabalho, em duas fases, cada uma com o seu plano no PRMake (Kind
`analisar-us` — o `prmake-plan.sh` escolhe sozinho quando o card e User Story):
1. **Analise** — descricao + criterios de aceite + discussion/anexos, Base Solvace, codigo, **solucao proposta como
   perguntas ao usuario**. Sem causa raiz, sem RCA, sem classificacao de bug.
2. **Implementacao** — com as respostas, o **plano de implementacao** (fase `correction`) que o PRMake transforma em
   **item de trabalho `implementation`** com uma sub-tarefa por etapa; entregas independentes viram itens proprios.
   A US e a historia do DevOps: **nunca crie item como `user-story`** (nem `bug`) — itens tem tipos proprios
   (`WorkItemTypes`: `implementation`, `pull-request`, `review`, `adjustment`, `closing`, `data`, `ticket`, `text`, `task`).

O servidor ja cria o item **"Analise inicial da US #<card>"** ao nascer o plano de analise (sub-tarefas = etapas,
sincronizadas sozinhas) — nao crie outro item manualmente para a analise; ao fim, o plano de implementacao gera os
itens da entrega.

Instalada e atualizada pelo PRMake (fonte: `skills/analisar-us`). Usa os scripts e as referencias da `analisar-bug`
(instalada junto) — mesmo plano, mesmos comandos. Token: env `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh    # o mesmo da analisar-bug (contexto detecta a US)
REF=~/.claude/skills/analisar-bug/scripts/ref.sh              # referencias da analisar-bug por SECAO
KB=~/.claude/skills/base-solvace/scripts/kb.sh
KC=~/.claude/skills/base-solvace/scripts/kc.sh
```

## Vale tudo o que vale na analisar-bug
Economia de tokens (contexto num comando, referencias so por secao, saidas curtas), **MCP primeiro** (mesma tabela:
`$REF plano comandos` se precisar), modo executor (`PRMAKE_EXECUTOR=1`: nada de chat/`watch`; o que depende de alguem
vai para o plano e voce encerra a vez), **Base Solvace antes do codigo** (engenharia reversa do `contexto` →
`prmake_base_search`/`prmake_base_get` com o card, termos em PT e EN → codigo so para confirmar o `Onde:`), **lacunas
obrigatorias** (codigo lido sem item que o cite → `arch.sh suggest ... --kind gap --card`), evidencias do card abertas
antes de concluir a analise, arquivos de cada fase no plano certo, **codigo citado = fotografia** (`coderef`,
`[caminho:linhas](ref:<id>)`), Knowledge Center antes de perguntar regra de negocio (cite ART-n), repositorios pelo mapa
da maquina, codigo da analise na copia da master (`revamp-repos.sh master <repo>`), **toda gravacao pelo PRMake**,
**nunca merge**, configuracao (estados, branches, titulos) sempre do PRMake. Na duvida sobre uma dessas regras:
`$REF plano <assunto>` ou `$REF consultas <assunto>`.

**O que muda na US**: nada de `causa-raiz`, RCA, root cause no DevOps, classificacao (Resolution Type), catalogo de
padroes de bug (A–H) nem resumo PT/EN de fechamento de bug. O "porque" e o **objetivo da US**; a evidencia de pronto sao
os **criterios de aceite** (`CA-1`, `CA-2`...).

**Terminologia (obrigatoria em tudo o que a pessoa le** — titulos do plano e das etapas, Timeline, perguntas, checkpoint,
handoff, itens, PR, respostas): US = **plano de implementacao**, **implementar**, **implementacao**, **entrega**. Nunca
"correcao", "plano de correcao", "corrigir", "causa raiz", "RCA" nem "bug". A fase no servidor continua `correction`
(nome tecnico: `prmake_correction`, `contexto-correcao`, `phase: "correction"`) — so o nome da ferramenta. Onde as
referencias da `analisar-bug` (`$REF correcao ...`) dizem "correcao", leia "implementacao". Titulo do plano:
**"Implementacao <card>: <o que entrega>"** (o PRMake ajusta sozinho se vier "Correcao ...").

## Fluxo
**0. Atualizar** — `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet analisar-us analisar-bug 2>/dev/null || true`;
se atualizar, releia esta SKILL.md.

**1. Card** — numero informado ou da branch (`feature/<n>`, `us/<n>`). Card que **nao** e User Story →
use a `analisar-bug`. **Branch de User Story e `feature/<n>` (`BranchNamePatternUserStory`), nunca `hotfix/`** — o
`bash $PLAN branches <card> <repo>` e o `prmake_rules` ja devolvem o nome certo pelo tipo do card; nao monte nome na mao.

**2. Contexto (1 comando)** — `bash $PLAN contexto <card>`: campos, **descricao** e **criterios de aceite**
(`dados/criterios-aceite.txt`), plano `analisar-us` criado/retomado com as etapas da US, comentarios/anexos, evidencias,
engenharia reversa e Base Solvace. Conclua `identificar-card`/`coletar-dados` num `advance`. **Implementacao numa sessao
nova** (o executor pede): `bash $PLAN contexto-correcao <card>` e siga do passo 7 (ele ja lista os itens de trabalho do
card e grava as regras em `dados/regras.md`).

**3. Entender a US (`entender-us`)** — objetivo (o que muda para quem usa), atores, regras de negocio citadas, e os
criterios de aceite **numerados** (`CA-1`...; card sem criterios: derive da descricao, marque "derivado" e pergunte no
passo 6). Liste o que e duvida — duvida que decide a solucao vira pergunta no passo 6, nao suposicao.

**4. Base e codigo (`consultar-base`, `mapear-codigo`)** — como na analisar-bug (`$REF consultas 3a` para localizar
codigo): `consultar-base` conclui citando os itens (`modulo#RN-012`) ou `lacuna: ...` (o PRMake recusa sem isso).
`mapear-codigo`: para cada CA, onde a mudanca entra (tela, endpoint, servico, tabela, repositorio — legado `edv-solvace`
ou `revamp-<modulo>`), com fotografia de cada trecho citado. Dados so leitura (`sql-query.sh`, `$REF consultas 3c`).

**5. Desenhar a solucao (`desenhar-solucao`)** — `prmake_base_impact` em **cada ponto de alteracao** e 1 a 3 opcoes,
cada uma com: o que muda (tabela `# · Onde · Mudanca · CA atendido`, trechos como `[caminho:linhas](ref:<id>)` com a
proposta no `coderef`), impacto na base, riscos, esforco, **entregas** (o que vai em PR separado, em que repositorio e
para quais destinos do fluxo — nomes so do `bash $PLAN branches <card> <repo>`) e Antes x Depois quando visivel
(`$REF analise 4c`).

**6. Analise, Timeline e perguntas (`montar-analise`, `publicar`, `propor-solucoes`)** — `$CARD_DIR/analises/analise-us.md`:
`> [!SUMMARY]` (o que a US pede e a solucao recomendada), **Entendimento**, **Criterios de aceite** (tabela `CA ·
criterio · como a solucao atende · como validar`), **Base Solvace** (itens usados, lacunas registradas), **Onde entra**,
**Solucoes** (secao por opcao, nome da solucao — nunca "Opcao 1"), **Entregas e itens de trabalho previstos**,
**Duvidas**. Arquivo no plano (`prmake_file(..., "analysis", "montar-analise")` no executor; `sync` no terminal),
publique na Timeline (`prmake_timeline`) e pergunte (`prmake_ask`, como em `$REF correcao 6`): qual solucao, as
duvidas que decidem o escopo, criterios derivados (confirma?), fluxo/base de branches quando o `branches` pedir (exit 3),
se ha script de dados (vai sempre por **chamado** — `TicketSystem` das regras) e, **por ultimo, os itens de trabalho**
(card 77988) — "Como seguir com os itens de trabalho da implementacao?" com exatamente estas opcoes (na `description`, o que
acontece neste card: repositorios, PRs e destinos do `branches`, chamado): `Criar os itens e iniciar agora` · `Criar os
itens sem iniciar` (plano e itens prontos, plano pausado; a pessoa inicia pelo Executar do item na Bancada) · `Nao criar
itens agora` (os itens saem depois pelo botao **Criar itens da analise** da Bancada ou pedindo ao Especialista do caso).
Nunca o sim/nao "posso seguir com a implementacao?" nem "so a analise" sem dizer como criar os itens depois
(`$REF correcao 6` tem o que fazer com cada resposta e com o pedido "Criar itens da analise", que vale por cima do "nao").
**Checkpoint obrigatorio** de `propor-solucoes` (ate ~3.000 caracteres,
autossuficiente: objetivo, CAs, solucao por opcao com refs, entregas, branches levantadas, riscos) — a implementacao
roda numa sessao nova que le so ele. Com as respostas, `status <card> completed "" .../analise-us.md` e siga pela
resposta dos itens: iniciar → passos 7 e 8; sem iniciar → passo 7 e `status <card> paused "Criado sem iniciar — use
Executar no item da Bancada"` (fase `correction`; nenhuma etapa executada); nao criar → pare aqui.

**7. Plano de implementacao → item de trabalho** — `prmake_correction(card, "Implementacao <card>: <resumo>", steps)`
(script: `bash $PLAN correction <card> "Implementacao <card>: <resumo>" -`): o PRMake cria **um item `implementation`** ligado ao plano, com **uma sub-tarefa por
etapa** (`stepKey`), e sincroniza as sub-tarefas conforme as etapas andam (voce **nao** chama `prmake_work_task` para
esse item). Etapas tipicas (adapte ao caso; nomes e descricoes concretos):

| Etapa | kind | executor | Observacao |
|---|---|---|---|
| `impacto-na-base` | `task` | claude | `prmake_base_impact` em cada ponto; vira o roteiro de regressao |
| `implementar-<repo>` (uma por repositorio) | `code` | claude | `dependsOn: impacto-na-base`; worktree e commits pelo `branches`; sem comentarios no codigo; testes so do que mudou (`$REF correcao 8`); no fim, `bash $PLAN deliver-code <card> <repo> --step implementar-<repo>` (0091: branch + diff na sub-tarefa) |
| `validar` | `validation` | claude | build/testes; CA verificaveis localmente |
| `pr-<repo>` | `pr` | claude | descricao pelo `pr-text` + prompt de **User Story** (sem `<RCA>`), `save-pr-text` sem rca, `open-pr` nas linhas do `branches`; conclui quando os PRs forem mesclados — nunca mescle |
| `preparar-script-<nome>` (so com script de dados) | `task` | claude | escreve o `.sql` com rollback e a consulta de validacao e o **chamado completo**; **antes** de concluir, grava no plano com `key` = a etapa do chamado: `prmake_file(card, "01_<nome>.sql", ..., "script", "chamado-<nome>", phase: "correction")` e `prmake_file(card, "chamado-<nome>.md", ..., "ticket", "chamado-<nome>", phase: "correction")` (1a linha = titulo; corpo: link do card, cliente, ambiente, banco, o que o script faz e por que — pelo objetivo da US —, rollback, consulta de validacao). O PRMake recusa a etapa do chamado com o usuario sem os dois arquivos |
| `chamado-<nome>` | `ticket` | **user** | `dependsOn: preparar-script-<nome>`; descricao cita os arquivos ("Abra o chamado no `TicketSystem` com o texto de `chamado-<nome>.md` e anexe `01_<nome>.sql`; depois use **Anexar chamado** nesta etapa/sub-tarefa com o link"); a etapa fica aguardando o chamado e **conclui sozinha** quando a pessoa marca o chamado como resolvido — nunca peca para clicar em *Concluir* nem ponha a validacao na descricao desta etapa |
| `validar-chamado-<nome>` | `validation` (`taskKind: sql-query`) | claude | `dependsOn: chamado-<nome>`; com o chamado resolvido, roda a **consulta de validacao** do script (somente leitura, `sql-query.sh` no host/banco do cliente) e confirma que os dados mudaram como o script previa; grava o resultado (consulta + linhas) como evidencia: `prmake_file(card, "<nome>-validacao.md", ..., "analysis", "validar-chamado-<nome>", phase: "correction")` + resumo na Timeline. Divergencia → `fail` dizendo o que ficou diferente (o chamado volta para a pessoa). As etapas seguintes que dependem dos dados (ex.: `validar-qa` com CA de dados, `fechar-card`) dependem desta |
| `validar-qa` | `validation` | **user** | a pessoa valida os CAs em QA (roteiro na descricao: CA → passo → esperado). **So se algum PR do `branches` tem destino QA** — repositorio sem QA (ex.: revamp-backend, so `development`) nao tem essa etapa: nao crie `validar-qa`; a validacao fica na `validar`/no destino que o fluxo tem |
| `fechar-card` | `task` | claude | ultima etapa: confere PRs e CAs; mover o card **so** com a acao configurada (`devops <card> config`) e autorizada nas respostas; sem acao aplicavel a US → nao mova e diga |

**Categoria na Bancada (`taskKind`)** — cada etapa vira uma sub-tarefa do item de trabalho e a categoria dela aparece
na Bancada. Informe `taskKind` (catalogo `WorkItemTaskKinds`) em toda etapa que nao for codigo: `research` (analise/consulta
sem alterar nada — `impacto-na-base`, levantamentos), `sql-query` (consulta SQL so leitura), `sql-script` (`preparar-script-*`),
`ticket` (`chamado-*`), `sql-query` (`validar-chamado-*`), `pr` (`pr-*`), `devops-action` (`preparar-fechamento`, `fechar-card`, mover o card, root cause,
classificacao), `notify` (Timeline/Teams), `comment`, `pr-text`, `decision` (decidir com a pessoa), `manual` (tarefa
da pessoa sem tipo proprio) ou `task` (o que nao cabe em nenhuma); `code` **so** em etapa que altera codigo. Sem `taskKind`
o PRMake deduz pela chave e pelo titulo (nunca mais "Alterar codigo" para etapa `task`), entao use as chaves desta tabela.

Script de dados **sempre** vai com o trio `preparar-script-*` + `chamado-*` + `validar-chamado-*` (nunca script sem
chamado, chamado sem o arquivo do chamado completo, nem chamado sem a conferencia depois) — igual ao Bug (`$REF correcao
7`, linha do chamado).

**Entregas independentes** (ex.: um PR para producao e outro para `release-version`; uma parte do back que pode ir antes
do front; um script de dados que vai depois): alem do plano, crie **itens proprios** (item com sub-tarefa `sql-script`
ganha sozinho as sub-tarefas do chamado — gerar o chamado completo + a pessoa abre no sistema de chamados e anexa o
link + conferir os dados depois do chamado; nao duplique):
1. `prmake_work_items(card)` (script: `bash $PLAN work-items <card>`) — ja existe item com o mesmo objetivo
   (titulo/sub-tarefas/fingerprint, status nao cancelado)? Nao crie outro; cite o existente.
2. `prmake_rules(card)` (ou `dados/regras.md`) para os nomes: branch por destino (`release-version` → `<branch>-rv`),
   titulo, commit — nunca invente.
3. Ferramenta pronta que serve (`prmake_tools(card)`: PR para uma branch, promocao, ajuste pos-QA...) →
   `prmake_tool_run(card, key, answers)`. Senao `prmake_work_item_create(card, title, description, tasks, type,
   planPhase: "correction")` com `type` do item (`implementation` para codigo da US, `pull-request` para so levar PRs,
   `data` para script com chamado (ferramenta `script-com-chamado`),
   `data`, `ticket`... — nunca `user-story`), cada sub-tarefa com `kind` do catalogo (`branch, cherry-pick, code, pr,
   devops-state, devops-action, pr-text, sql-query, sql-script, validation, comment, manual, ticket, notify, research,
   decision, task` — `code` so quando altera codigo), `executor` e `params` reais (`repository`, `branch`, `from`, `target`...). Resposta `duplicate` → nao
   insista; `allowDuplicate` + motivo so se for de fato outro trabalho. Esses itens rodam pelo
   `executar-item-de-trabalho` quando a pessoa clicar "Executar".

**8. Executar** — como a analisar-bug (`$REF correcao 8`): `control` entre etapas, worktree do card, PRs com
`open-pr`, espera com `prmake_handoff` (terceira pessoa, espera como status: "Aguardando abrir chamado"), nova rodada no mesmo plano se a validacao falhar (`$REF correcao 8b`). Plano
concluido → o PRMake conclui o item.

**9. Aprender e reportar** — lacunas ja registradas; regra nova ou item da base errado → `arch.sh suggest ... --kind
learning|divergence --card <card>`. Reporte: card, solucao, itens de trabalho (numeros), PRs, o que ficou com quem.

## Referencias da analisar-bug — `bash $REF <arquivo> <secao>` (so a secao, so na hora)
| Quando | Comando |
|---|---|
| localizar codigo, Cognito, banco | `$REF consultas 3a` · `3b` · `3c` |
| fotografias do codigo · mock visual | `$REF analise 4b` · `4c` |
| perguntas, espera nas duas pontas | `$REF correcao 6` |
| fluxo de branches, PRs, execucao, nova rodada | `$REF correcao 7` (parte de branches) · `8` · `8b` |
| plano (retomar, control, block, watch) | `$REF plano <secao>` |

### Itens de trabalho ricos (0087) — a pessoa entende tudo sem abrir o codigo

Quem abre o item (ou a sub-tarefa) na Bancada deve entender, **de forma clara e objetiva, o que sera feito, cada passo e
cada alteracao**. Por isso, ao montar o plano de implementacao e os itens, **antes** de chamar `prmake_correction` /
`prmake_work_item_create` / `prmake_tool_run`:
1. **Fotografe todo codigo que muda ou que explica a mudanca** (`prmake_code_ref`/`$PLAN coderef <card> <ref> --repo <repo> --file <caminho> --lines A-B`,
   com `--change alterar|criar|remover`, `--why` e `--after` quando muda). O link `[caminho:linhas](ref:<ref>)` abre o popover com o codigo
   atual e o diff — o mesmo da analise. Reaproveite as fotografias da analise (mesmo id).
2. **Descricao do item** (markdown): *O que sera feito* (2–4 frases), *Por que* (causa/criterio), *Onde* (lista `[caminho:linhas](ref:id)` — **Alterar/Criar/Remover**),
   *Regras da Base Solvace* (`modulo#RN-012`, `UC-003`: viram link para o item da engenharia reversa), *Como validar*.
3. **Cada sub-tarefa/etapa de codigo (`kind=code`)**: descricao com o arquivo e a linha (`[caminho:linhas](ref:id)`), o que muda **nessa** linha, o porque e a regra da
   Base que ela cumpre. O PRMake **recusa** etapa/sub-tarefa `code` sem nenhuma citacao e citacao sem fotografia — corrija e crie de novo.
   Sub-tarefa nao-codigo (PR, chamado, script, validacao): diga exatamente o que entregar (branch/destino, titulo, arquivo, criterio).
4. **Arquivos, imagens e textos entram no item**: o que ajuda a entender ou executar — analise (`.md`), mock Antes x Depois, script `.sql`, chamado, dados.
   Texto: `prmake_work_item_file(card, number, name, content, kind, taskNumber?)` (vincule a sub-tarefa com `taskNumber`). Imagem/binario: grave no
   **plano** (`bash $PLAN upload <card> <arquivo> ...` ou `prmake_file`) — o item ligado ao plano mostra os arquivos do plano na aba Arquivos; item avulso,
   so texto. **Cite cada arquivo na descricao** (nome do arquivo) para a pessoa saber o que abrir.
5. Nada de "ver analise": a descricao se basta. Texto curto, em pt-BR, sem jargao desnecessario.
