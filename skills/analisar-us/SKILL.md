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

**1. Card** — numero informado ou da branch (`feature/<n>`, `us/<n>`, `hotfix/<n>`). Card que **nao** e User Story →
use a `analisar-bug`.

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
se ha script de dados (vai sempre por **chamado** — `TicketSystem` das regras), se pode seguir com a implementacao agora.
**Checkpoint obrigatorio** de `propor-solucoes` (ate ~3.000 caracteres,
autossuficiente: objetivo, CAs, solucao por opcao com refs, entregas, branches levantadas, riscos) — a implementacao
roda numa sessao nova que le so ele. Com as respostas, `status <card> completed "" .../analise-us.md`.

**7. Plano de implementacao → item de trabalho** — `prmake_correction(card, "Implementacao <card>: <resumo>", steps)`
(script: `bash $PLAN correction <card> "Implementacao <card>: <resumo>" -`): o PRMake cria **um item `implementation`** ligado ao plano, com **uma sub-tarefa por
etapa** (`stepKey`), e sincroniza as sub-tarefas conforme as etapas andam (voce **nao** chama `prmake_work_task` para
esse item). Etapas tipicas (adapte ao caso; nomes e descricoes concretos):

| Etapa | kind | executor | Observacao |
|---|---|---|---|
| `impacto-na-base` | `task` | claude | `prmake_base_impact` em cada ponto; vira o roteiro de regressao |
| `implementar-<repo>` (uma por repositorio) | `code` | claude | `dependsOn: impacto-na-base`; worktree e commits pelo `branches`; sem comentarios no codigo; testes so do que mudou (`$REF correcao 8`) |
| `validar` | `validation` | claude | build/testes; CA verificaveis localmente |
| `pr-<repo>` | `pr` | claude | descricao pelo `pr-text` + prompt de **User Story** (sem `<RCA>`), `save-pr-text` sem rca, `open-pr` nas linhas do `branches`; conclui quando os PRs forem mesclados — nunca mescle |
| `preparar-script-<nome>` (so com script de dados) | `task` | claude | escreve o `.sql` com rollback e a consulta de validacao e o **chamado completo**; **antes** de concluir, grava no plano com `key` = a etapa do chamado: `prmake_file(card, "01_<nome>.sql", ..., "script", "chamado-<nome>", phase: "correction")` e `prmake_file(card, "chamado-<nome>.md", ..., "ticket", "chamado-<nome>", phase: "correction")` (1a linha = titulo; corpo: link do card, cliente, ambiente, banco, o que o script faz e por que — pelo objetivo da US —, rollback, consulta de validacao). O PRMake recusa a etapa do chamado com o usuario sem os dois arquivos |
| `chamado-<nome>` | `ticket` | **user** | `dependsOn: preparar-script-<nome>`; descricao cita os arquivos ("Abra o chamado no `TicketSystem` com o texto de `chamado-<nome>.md` e anexe `01_<nome>.sql`; cole o link aqui"); fica aguardando ate o chamado ser resolvido |
| `validar-qa` | `validation` | **user** | a pessoa valida os CAs em QA (roteiro na descricao: CA → passo → esperado) |
| `fechar-card` | `task` | claude | ultima etapa: confere PRs e CAs; mover o card **so** com a acao configurada (`devops <card> config`) e autorizada nas respostas; sem acao aplicavel a US → nao mova e diga |

Script de dados **sempre** vai com o par `preparar-script-*` + `chamado-*` (nunca script sem chamado, nem chamado sem o
arquivo do chamado completo) — igual ao Bug (`$REF correcao 7`, linha do chamado).

**Entregas independentes** (ex.: um PR para producao e outro para `release-version`; uma parte do back que pode ir antes
do front; um script de dados que vai depois): alem do plano, crie **itens proprios** (item com sub-tarefa `sql-script`
ganha sozinho as sub-tarefas do chamado — gerar o chamado completo + a pessoa abre no sistema de chamados; nao duplique):
1. `prmake_work_items(card)` (script: `bash $PLAN work-items <card>`) — ja existe item com o mesmo objetivo
   (titulo/sub-tarefas/fingerprint, status nao cancelado)? Nao crie outro; cite o existente.
2. `prmake_rules(card)` (ou `dados/regras.md`) para os nomes: branch por destino (`release-version` → `<branch>-rv`),
   titulo, commit — nunca invente.
3. Ferramenta pronta que serve (`prmake_tools(card)`: PR para uma branch, promocao, ajuste pos-QA...) →
   `prmake_tool_run(card, key, answers)`. Senao `prmake_work_item_create(card, title, description, tasks, type,
   planPhase: "correction")` com `type` do item (`implementation` para codigo da US, `pull-request` para so levar PRs,
   `data` para script com chamado (ferramenta `script-com-chamado`),
   `data`, `ticket`... — nunca `user-story`), cada sub-tarefa com `kind` do catalogo (`branch, cherry-pick, code, pr,
   devops-state, devops-action, pr-text, sql-query, sql-script, validation, comment, manual, ticket, notify`), `executor` e `params` reais (`repository`, `branch`, `from`, `target`...). Resposta `duplicate` → nao
   insista; `allowDuplicate` + motivo so se for de fato outro trabalho. Esses itens rodam pelo
   `executar-item-de-trabalho` quando a pessoa clicar "Executar".

**8. Executar** — como a analisar-bug (`$REF correcao 8`): `control` entre etapas, worktree do card, PRs com
`open-pr`, espera com `prmake_handoff`, nova rodada no mesmo plano se a validacao falhar (`$REF correcao 8b`). Plano
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
