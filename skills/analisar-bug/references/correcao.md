# Propor solucoes, plano de correcao e execucao (passos 6 a 8b)

Leia so a secao do passo em que esta (`bash ~/.claude/skills/analisar-bug/scripts/ref.sh correcao <6|7|8|8b>`).

### 6. Propor solucoes e perguntar (etapa `propor-solucoes`)
Com a analise publicada, a analise ainda nao terminou: **proponha as solucoes** e **decida com o usuario**.
1. `step propor-solucoes running`. Antes de escrever, rode `prmake_base_impact` em cada ponto de alteracao das opcoes
   (arquivo, funcao, tabela ou item da engenharia reversa) — 0063. Escreva `$CARD_DIR/analises/solucoes.md`: 1 a 3
   opcoes, cada uma com o que muda (repositorios/arquivos), **o impacto na base** (quem mais usa aquele ponto, pelo
   `prmake_base_impact`: telas/modulos/itens), riscos, se precisa de script de dados (chamado) e o esforco; marque a
   recomendada. **Formato (0075)** — o mesmo da analise (`ref.sh analise 4`): abra com `> [!SUMMARY]` (a recomendada e por
   que), depois uma secao `### <nome da opcao>` por opcao (nome da solucao, nunca "Opcao 1"), cada uma com as mesmas
   subsecoes em negrito: **O que muda** (tabela `# · Onde · Mudanca · Por que`, com cada trecho como
   `[caminho:linhas](ref:<id>)`), **Impacto na base**, **Riscos**, **Esforco**, **Antes x Depois** (mudanca visivel,
   `ref.sh analise 4c`). Cada trecho que a opcao muda tem fotografia com proposta, `--why` e notas (`ref.sh analise 4b`)
   — refs diferentes por opcao quando o codigo novo difere (`sa3-init-a`, `sa3-init-b`). **Opcao com chamado (0050): nao grave o script nem o texto do chamado aqui** — descreva o que o
   script vai fazer (tabelas, filtro, linhas afetadas estimadas, rollback) e diga que ele e o texto do chamado serao
   anexados no plano de correcao, na etapa do chamado. O resumo do checkpoint leva o que a correcao precisa para
   escreve-lo (ids, consultas usadas, host/banco). `sync <card> propor-solucoes` (no executor: `prmake_file(card, "solucoes.md", conteudo, "analysis",
   "propor-solucoes")`) e `log ... decision` com o resumo.
2. **Pergunte** (`ask`) tudo o que decide o plano — a etapa fica *aguardando* e o PRMake mostra as perguntas
   em destaque. Sempre que se aplicar:
   - Qual solucao seguir (opcoes das solucoes, com a recomendada).
   - **Padrao(oes) do caso** (catalogo acima) — proponha o que a analise indica, com a evidencia, e deixe o
     usuario confirmar/ajustar: codigo (A), dados via script/chamado (B), acesso/Cognito (C), configuracao/
     ambiente (D), user education (E), change request (F), nao reproduz/sem retorno (G), duplicado (H). Pode ser
     mais de um. Nem todo card tem codigo — nao presuma PR.
   - **Fechamento**: com o `devops <card> config` em maos, pode mover o card ao final (cite o estado e a area
     exatamente como configurados, so as acoes configuradas e aplicaveis a area atual do card, mais "Nao mover")?
     A resposta autoriza voce a mover pelo PRMake no `fechar-card` (ver **Mover o card e sempre pelo PRMake**) —
     **exceto o Ready for QA**, que so vale depois da validacao em QA (ver **Dev Test in QA → Ready for QA**). E a
     classificacao sugerida (uma das opcoes de `devops <card> classifications`, com o motivo) esta certa?
   - **Fluxo de branches**: rode `branches <card> <repo>` para cada repositorio; se ele sair com exit 3, pergunte
     o que ele pedir (o fluxo, quando a area nao tem regra, ou a branch base, quando a regra manda perguntar —
     com as opcoes que ele listar).
   - Se ha script de dados: vai **sempre** por **chamado** (o usuario abre no sistema de chamados configurado —
     `TicketSystem` do `settings`) — pergunte so o que decide o script (ambiente, janela, quem aprova), nao "se vai por
     chamado".
   - **Itens de trabalho — sempre a ultima pergunta, com estas tres opcoes** (card 77988: o sim/nao "posso seguir com a
     correcao?" respondido com "so a analise" deixou o card sem plano, sem item e sem caminho para cria-los depois). Texto:
     "Como seguir com os itens de trabalho da correcao?" — opcoes (`label` exatamente assim; na `description` o que vai
     acontecer neste card: repositorios, PRs e destinos do `branches`, chamado):
     · `Criar os itens e iniciar agora` — monto o plano de correcao (vira o item de trabalho na Bancada) e executo;
     · `Criar os itens sem iniciar` — monto o plano e os itens e deixo o plano pausado; a pessoa inicia pelo Executar do item;
     · `Nao criar itens agora` — a analise termina aqui; os itens saem depois pelo botao **Criar itens da analise** da
       Bancada (ou pedindo ao Especialista do caso).
     Nunca pergunte "posso seguir?" como sim/nao nem ofereca "so a analise" sem dizer como os itens sao criados depois.
   **Nova rodada de perguntas** (comentario/anexo/resposta mudou a analise e voce refez as solucoes — card 75648):
   antes do `ask`, veja as perguntas do plano (`prmake_answers`) e trate **cada uma ainda aberta**:
   - continua valendo como esta → deixe aberta (nao pergunte de novo);
   - mudou (outras opcoes, outro texto) → a nova vai com `replaces: [n]` no `prmake_ask` (script: `"replaces": [n]`
     no JSON) — o PRMake cancela a antiga e mostra "substituida pela pergunta N". Vale tambem para pergunta ja
     **respondida** cuja resposta deixou de valer (ela fica marcada como superada; vale a resposta da nova);
   - perdeu o sentido (o print respondeu, a hipotese caiu, juntou em outra) → `prmake_cancel_questions(card,
     [n], "motivo curto")` (script: `cancel-questions <card> <n,...> "motivo"`).
   Nunca escreva "substitui a pergunta N" so no texto: sem `replaces`/cancelamento a antiga fica aberta e o usuario
   tem que cancela-la a mao. O `prmake_ask` devolve `stillOpen` (anteriores ainda abertas) — se sobrar alguma que nao
   decide mais o plano, cancele na hora. No fim, so ficam abertas as perguntas que ainda decidem o plano.
   Use opcoes objetivas + texto livre. Mostre as mesmas perguntas no terminal. **O `label` de cada opcao e o
   texto que o usuario le no botao e o que vai para a Timeline** — escreva a opcao em si ("Corrigir no backend
   (recomendada)", "Script de dados via chamado"), nunca "Opcao 1"/"A"; detalhes vao em `description`.
   **Resumo para a correcao (obrigatorio, junto com as perguntas)** — `prmake_checkpoint(card, "propor-solucoes",
   texto)` (sem MCP: `checkpoint <card> propor-solucoes "..."`), ate ~3.000 caracteres, **autossuficiente**: no
   executor a correcao roda numa sessao nova que le **so isto** (+ respostas, comentarios e arquivos), sem esta
   conversa. Inclua: causa raiz e a evidencia (ids, consultas, host/banco usados); onde mexer em cada opcao — repo
   (legado `edv-solvace` ou `revamp-<modulo>`), `caminho:linha` com a ref (`[...](ref:<id>)` — a correcao le a proposta
   e as notas pela fotografia, sem reler a analise), funcao/SP; consultas usadas (nomes nos arquivos do
   plano) e, com chamado, o que o script de alteracao deve fazer (a correcao o escreve); branches levantadas pelo `branches`; riscos; o que o usuario pediu nos comentarios. Sem repetir a analise
   inteira — ela continua na Timeline e em `analises/`.
3. **Espere as duas pontas ao mesmo tempo** — o usuario pode responder pela tela (PRMake) ou aqui:
   - Rode `bash $PLAN wait-answers <card> 3600` **em segundo plano** (ferramenta Bash com
     `run_in_background: true`) e so entao mostre as perguntas no terminal e encerre a sua vez esperando o chat.
     Voce **nao ve** respostas dadas no PRMake enquanto esta parado esperando o chat — e o comando em segundo
     plano que as detecta: quando todas forem respondidas pela tela ele termina e voce e acordado com as
     respostas na saida.
   - Se o usuario responder aqui no chat: grave cada resposta com `answer <card> <n> "..."` (o PRMake mostra
     "pelo Claude") e siga — o comando em segundo plano termina sozinho.
   - Se o usuario disser que respondeu no PRMake (ou voce voltar a sessao depois): rode `answers <card>`.
   - Resposta que cita anexo/comentario ("ver imagem 2", "como no print") → `attachment`/`notes` e abra antes de
     seguir. Rode `prmake_notes` (sem MCP: `notes <card>`) ao receber as respostas: o usuario pode ter anexado algo junto.
   - Exit 10 (1 h sem todas as respostas): avise e pare — o `start` retoma depois.
   Com as respostas: `step propor-solucoes completed` e `status <card> completed "" .../solucoes.md`
   (fim do plano de **analise**). Depois, pela resposta dos itens de trabalho:
   - **Criar e iniciar** → passo 7 e passo 8.
   - **Criar sem iniciar** → passo 7 (plano + itens separados) e, sem executar nenhuma etapa,
     `prmake_plan_status(card, "paused", reason: "Criado sem iniciar — use Executar no item da Bancada", phase: "correction")`
     (script: `status <card> paused "Criado sem iniciar — use Executar no item da Bancada"`); encerre. O Executar do item
     retoma o plano.
   - **Nao criar** → sem plano de correcao; no resumo final diga que os itens saem pelo botao **Criar itens da analise**.
   - **Pedido "Criar itens da analise"** (observacao do pedido do executor — botao da Bancada ou `prmake_analysis_items` do
     especialista —, ou a pessoa pede no chat depois): vale por cima de "Nao criar itens agora"/"so a analise" — passo 7 e
     inicie ou deixe pausado conforme o pedido.

### 7. Montar o plano de correcao
Monte as etapas **a partir do(s) padrao(oes) confirmados e das especificidades deste card** — o catalogo da
as etapas tipicas, voce adapta (nomes, descricoes com o que exatamente sera feito neste card, quem executa,
dependencias) e **sempre termina com o fechamento** (`fechar-card`, executor claude, com os comandos `devops`) —
**ele e a ultima etapa** (com codigo, ele so confere e move para Ready for QA — root cause, classificacao, resumo e
Remaining ja foram no `preparar-fechamento`, antes da validacao; nada do usuario depois dele, salvo `validar-qa` no fluxo com codigo, que vem antes do
Ready for QA).
So tem etapas de codigo/PR se houver codigo. Crie com `correction` — ele vira o
plano ativo e a tela mostra as abas *Analise* e *Correcao*. Cada etapa tem `executor` (`claude` ou `user`),
`kind` e, quando for o caso, `repository` e `dependsOn`:

| Etapa | kind | executor | Observacao |
|---|---|---|---|
| **Impacto na base (`impacto-na-base`) — com codigo, primeira etapa** | `task` | claude | 0063: `prmake_base_impact` em cada arquivo/funcao/tabela/item que a correcao vai tocar; lista no checkpoint o que mais usa cada ponto (telas, modulos, itens) e vira o roteiro de regressao do `validar`/`validar-qa`. Conclui citando os itens (ou "sem outros usos na base") |
| Corrigir o codigo — **uma por repositorio** (`corrigir-<repo>`) | `code` | claude | `dependsOn: impacto-na-base`; branches e commits do fluxo abaixo |
| Validar (build/testes/reproducao) | `validation` | claude ou user | depende da correcao; testes com `$PLAN test` (passo 8) |
| **PRs — uma por repositorio** (`pr-<repo>`) | `pr` | claude | todos os PRs daquele repositorio; ao anexar o PR (o `open-pr` anexa) a etapa passa sozinha a *aguardando o merge* e conclui sozinha quando **todos** forem mesclados — nao conclua nem deixe "em andamento" |
| Chamado de script de dados (`chamado-<nome>`) — **todo script tem** | `ticket` | **user** | sempre em par com a etapa sua que prepara o script e o chamado completo (`preparar-script-<nome>`, `task`, `claude`) — `dependsOn` nela; nunca script sem chamado nem chamado sem o arquivo do chamado completo (vale igual na US: `analisar-us`); **antes** de concluir essa etapa (antes de a do chamado ficar com o usuario), anexe no **plano de correcao**, com `key` = a etapa do chamado: o `.sql` de alteracao com rollback (`prmake_file(card, "01_<card>_<nome>.sql", ..., "script", "<etapa>", phase: "correction")`) e o texto do chamado (`prmake_file(card, "chamado-<card>-<nome>.md", ..., "ticket", "<etapa>", phase: "correction")` — 1a linha = titulo; corpo com o link do card, cliente, ambiente, banco, o que o script faz, rollback e a consulta de validacao). A API recusa deixar a etapa com o usuario sem os dois (`ticketStepsMissingFiles` no `prmake_control`). Na descricao da etapa cite os arquivos pelo nome ("Abra o chamado no `TicketSystem` com o texto de `chamado-<card>-x.md` e anexe `01_<card>_x.sql`; depois use **Anexar chamado** nesta etapa com o link"); o usuario anexa o chamado (no plano ou na sub-tarefa da Bancada), a etapa fica *aguardando o chamado* e **conclui sozinha** quando ele e marcado resolvido — nunca peca para clicar em *Concluir* nem ponha a conferencia na descricao desta etapa: ela e a etapa `validar-chamado-<nome>` |
| Conferir os dados depois do chamado (`validar-chamado-<nome>`) — **todo chamado tem** | `validation` (`taskKind: sql-query`) | claude | `dependsOn: chamado-<nome>`; com o chamado resolvido, rode a **consulta de validacao** do script (somente leitura, `sql-query.sh` no host/banco do cliente) e confirme que os dados mudaram como o script previa; grave o resultado (consulta + linhas) como evidencia: `prmake_file(card, "validacao-<card>-<nome>.md", ..., "analysis", "validar-chamado-<nome>", phase: "correction")` + resumo na Timeline. Divergencia → `fail` dizendo o que ficou diferente (o chamado volta para a pessoa). `validar-qa`/`fechar-card` que dependem dos dados dependem desta |
| Configuracao na tela do sistema (`configurar-<o que>`) | `task` | **user** | voce nao acessa o sistema do cliente: escreva o passo a passo exato (ambiente, tela, campo, valor antes/depois) em `analises/configuracao.md`; o usuario executa e conclui a etapa na tela |
| ~~`orientar-cliente`~~ — **nao e etapa** | — | — | a orientacao ao cliente (o que aconteceu, o que fazer, passo a passo, por que nao e bug) **e o resumo nao tecnico PT/EN** que o `fechar-card` publica na discussion: redija em `analises/orientacao-cliente.md` e use esse texto no resumo (ver abaixo) |
| Validar em QA (`validar-qa`) — **com codigo** | `validation` | **user** | **so quando o fluxo do repositorio tem PR para QA** (o `branches` lista um PR com destino `qa`; legado e revamp-frontend tem, revamp-backend nao — so `development`): sem destino QA **nao crie** `validar-qa` (nem prometa QA); a validacao e a `validar`. `dependsOn` as etapas de PR **e** `preparar-fechamento` (correcao publicada em QA e card sem pendencias); key exatamente `validar-qa` (0075: o PRMake move o card para Dev Test in QA sozinho quando o PR para a branch de QA e mesclado — confira no `prmake_plan`/`situation`; so se ele avisar erro, mova com `devops <card> dev-test-in-qa`); o usuario conclui a etapa na tela quando validar — o diálogo de conclusao ja oferece mover para Ready for QA |
| Validar o ambiente (`validar-configuracao`) | `validation` | claude ou user | **antes** do fechamento, quando o caso pede conferir o resultado (ex.: configuracao feita na tela; dados depois de script = `validar-chamado-<nome>`, acima). A confirmacao **com o cliente** nao e etapa: ela acontece depois do *Test in Production*, fora do plano — so crie uma etapa assim se o usuario pedir |
| **Preparar o fechamento (`preparar-fechamento`) — com codigo** | `task` | claude | **antes da validacao em QA** (0075: o Ready for QA exige root cause, Resolution Type, classificacoes, Bug Type e Remaining zerado — o usuario move ao concluir `validar-qa` e o card nao pode ter pendencia). `dependsOn` a etapa de validacao/correcao; rode **logo depois de abrir os PRs**: skill `gerar-prmake` **sem** abrir PR (`OPEN_GITHUB_PR` desligado), **reaproveitando** a descricao/RCA salvos na etapa de PR (root cause no DevOps e resumo PT/EN), `devops <card> classify <opcao> "<bug type>"` (Bug Type de `devops <card> bug-types`) e `devops <card> zero-remaining` (`$REF catalogo fechamento` itens 1–4, **sem** mover o card). `validar-qa` depende dela |

**Categoria na Bancada (`taskKind`)** — cada etapa vira uma sub-tarefa do item de trabalho e a categoria dela aparece
na Bancada. Informe `taskKind` (catalogo `WorkItemTaskKinds`) em toda etapa que nao for codigo: `research` (analise/consulta
sem alterar nada — `impacto-na-base`, levantamentos), `sql-query` (consulta SQL so leitura), `sql-script` (`preparar-script-*`),
`ticket` (`chamado-*`), `sql-query` (`validar-chamado-*`), `pr` (`pr-*`), `devops-action` (`preparar-fechamento`, `fechar-card`, mover o card, root cause,
classificacao), `notify` (Timeline/Teams), `comment`, `pr-text`, `decision` (decidir com a pessoa), `manual` (tarefa
da pessoa sem tipo proprio) ou `task` (o que nao cabe em nenhuma); `code` **so** em etapa que altera codigo. Sem `taskKind`
o PRMake deduz pela chave e pelo titulo (nunca mais "Alterar codigo" para etapa `task`), entao use as chaves desta tabela.

**Tratamento sem codigo** (o plano e dinamico — monte so o que o caso pede): quando a solucao e user education,
configuracao direto na tela do sistema ou algo do tipo, **nao crie nem sugira** etapas de correcao de codigo,
branches, PRs, "Gerar PRMake", chamados ou scripts — a menos que o caso realmente precise (ex.: so um script de
dados → so as etapas do script, do chamado e da conferencia). O plano pode ter apenas, por exemplo:

```json
[{"key":"configurar-permissao","title":"Ajustar a permissao do perfil no sistema","kind":"task","taskKind":"manual","executor":"user",
  "description":"Passo a passo em analises/configuracao.md"},
 {"key":"fechar-card","title":"Fechamento: root cause, classificacao, resumo PT/EN (orientacao ao cliente) e Test in Production",
  "kind":"task","taskKind":"devops-action","executor":"claude","dependsOn":["configurar-permissao"],
  "description":"O resumo PT/EN publicado na discussion e a orientacao ao cliente (texto de analises/orientacao-cliente.md)"}]
```

User education puro (nada a configurar) = **so o `fechar-card`**.

**Plano de correcao → item(s) de trabalho (0085)** — o plano de correcao agora gera **itens de trabalho** no PRMake
(tela *Bancada do card*):
- **Automatico**: ao criar o plano (`prmake_correction`/`correction`), o PRMake cria **um item** (`origin=correction`,
  tipo `fix` no card Bug / `implementation` no card US — o tipo do item nao e o tipo do card) ligado ao plano, com **uma sub-tarefa por etapa** (`stepKey` = key da etapa). Ao
  executar as etapas (`advance`/`step`/`block`), o servidor **sincroniza** as sub-tarefas (inicio, conclusao,
  espera, cancelamento) — **nao** chame `prmake_work_task` para esse item. Etapas novas (nova rodada, 8b) entram como
  sub-tarefas novas. Plano concluido (tudo terminado) → item **concluido**. Item do plano roda pelo plano, nunca pelo
  `executar-item-de-trabalho`.
- **A analise tambem tem item (ajustes 0085)**: o plano de analise ja gerou o item "Analise inicial do bug/da US
  #<card>" (`origin=analysis`, sincronizado pelo servidor) — nao crie outro para a analise; os itens da entrega saem
  deste plano de correcao.
- **Entregas independentes = itens separados**: quando a solucao tem partes que andam sozinhas — ex.: o PR para
  producao (master) e outro para `release-version` levado depois, um script de dados que vai por chamado em outro
  momento, um ajuste pos-QA —, deixe no plano so o que e desta rodada e crie as outras entregas com
  de preferencia por uma **ferramenta** pronta (`prmake_tools(card)` → `prmake_tool_run(card, key, answers)`: PR para uma
  branch, promocao revamp/hotfix-version, script com chamado, ajuste pos-QA...) ou com
  `prmake_work_item_create(card, title, description, tasks, type, planPhase: "correction")` — `type` do item
  (`WorkItemTypes`: `fix`, `pull-request`, `data`, `ticket`, `adjustment`, `closing`, `text`, `task`; nunca `bug`/`user-story`):
  1. **Antes**, `prmake_work_items(card)` (script: `bash $PLAN work-items <card>`): ja existe item com o mesmo objetivo
     (mesmo destino/repositorio/sub-tarefas, status nao cancelado)? Nao crie — cite o existente. A API recusa duplicata
     (`duplicate` com o item existente); `allowDuplicate` + motivo so quando for outro trabalho de fato.
  2. Sub-tarefas na ordem, com `kind` do catalogo (`branch, cherry-pick, code, pr, devops-state, devops-action, sql-script,
     sql-query, validation, comment, manual, ticket, notify, pr-text, research, decision, task` — `code` so quando altera codigo), `executor` (`claude`/`user`) e `params` **reais** (item com `sql-script` e sem
     `ticket` ganha sozinho, logo depois do script, "Gerar o chamado completo" (claude) + "Abrir o chamado no sistema de
     chamados e anexar o link" (user, aguarda ate o chamado ser resolvido) + "Conferir os dados depois do chamado" (claude,
     se nao houver consulta/validacao depois do script) — ou use a ferramenta `script-com-chamado`): `repository`, `branch` (nome do
     `branches`/`prmake_rules` — nunca inventado), `from`, `target`, `commits`. Ex.: "PR master → release-version" =
     `branch {branch: hotfix/<card>-rv, from: release-version}` → `cherry-pick {source: hotfix/<card>}` →
     `pr {branch: hotfix/<card>-rv, target: release-version}`.
  3. Diga no checkpoint/resumo quais itens criou (`#numero`). A pessoa executa cada item pela tela ("Executar", roda
     em paralelo pelo `executar-item-de-trabalho`); cancelar com "desfazer" usa o `desfazer-item-de-trabalho`.
- **Item de validacao em ambiente (0092) — toda correcao que altera algo e precisa ser validada**: alem do plano, crie
  (sem duplicar) o item de validacao com uma sub-tarefa por ambiente pelo `prmake_env_validation(card, environments,
  plant?, scenario?, expected?)` — padrao: os ambientes por onde a correcao passa no fluxo do card (`dev` e `qa` quando
  ha PR para eles; `sandboxefeso` = homologacao de release, `sandboxrt` = release/hotfix, quando o fluxo leva para la) +
  a **producao do cliente do bug** (chave da configuracao, ex.: `mondelez`, `gerdau`, ou a URL; `bash
  ~/.claude/skills/validar-ambiente/scripts/env.sh resolve <cliente>`) com a **planta** do cliente que reportou;
  ambiente que a pessoa pediu entra tambem. `scenario` = o roteiro curto (passo → esperado) do *Como validar*. O item
  fica pendente (nao execute): a pessoa (ou o especialista, a pedido) executa quando a correcao estiver no ambiente; o
  Claude entra, grava os prints e anexa na sub-tarefa (skill `validar-ambiente`). Cite o `#numero` no resumo.

**Orientacao ao cliente = resumo PT/EN do fechamento** (card 74669): o resumo nao tecnico que o `fechar-card`
publica na discussion e o que o cliente le — entao ele **e** a orientacao. Redija a orientacao (o que aconteceu, o
que foi feito ou o que o cliente faz, o passo a passo, por que nao e bug) em `analises/orientacao-cliente.md`, use
esse conteudo no resumo (seguindo o `summary-prompt` do `config`, no formato `**PT**`/`**EN**`) e nao crie etapa
para "enviar a orientacao". Depois do fechamento nao ha etapa do usuario: a validacao do cliente e acompanhada pelo
estado *Test in Production*, fora do plano.

Nesses casos o seu trabalho e redigir o passo a passo / a orientacao (e salvar com `sync`), deixar o vigia rodando
e acompanhar: quando o `fechar-card` termina (root cause, classificacao, resumo, Remaining e o card movido, se
autorizado), **todas as etapas estao terminadas e o plano conclui sozinho** — sem PR. Se ainda sobrar alguma etapa
do usuario que perdeu o sentido (plano antigo), cancele com o motivo ("a orientacao foi no resumo PT/EN") e conclua.

**Fluxo de branches — sempre pelo `branches`** (regras no PRMake, `Skills Configurations`):
```bash
bash $PLAN branches <card> <repo>                       # usa o fluxo pela area do card
bash $PLAN branches <card> <repo> --flow <f> --base <b> # quando ele pedir (exit 3) ou o usuario escolher
```
Ele imprime o tipo do repositorio, o fluxo, a base, **a pasta do repositorio nesta maquina** (`pasta=`, pelo mapa
da 0048), a branch de correcao, a mensagem de commit, os PRs (branch derivada, de onde sai + cherry-pick, destino,
titulo), **o nome por destino** (0085: linha `por-destino:` nos PRs sem sufixo cujo destino tem sufixo proprio e
`branches-por-destino:` com todos — `BranchSuffixByTarget` no PRMake, ex.:
`{"development":"-dev","qa":"-qa","release-version":"-rv","hotfix-version":"-hv","edge":"-edge"}`; release-version →
`hotfix/<card>-rv`, **nunca** `-release`; **User Story = `feature/<card>`**: o prefixo vem do tipo do card — `BranchNamePattern` no Bug, `BranchNamePatternUserStory` na US) e os **comandos prontos** (`git checkout`/`worktree` ja com a pasta, `cherry-pick`, `push` e as linhas de
`open-pr`). Exit 4 = a pasta do repositorio e desconhecida (fora do mapa ou mais de um clone): pergunte ao usuario,
fixe com `bash ~/.claude/skills/.prmake/prmake-skills.sh repos set <repo> <pasta>` e rode o `branches` de novo. Os PRs sao **os do repositorio em tratamento** (revamp-backend: so `development`; legado e revamp-frontend: `development` e `qa`) — nunca sugira, abra nem planeje PR/branch para um destino que o `branches` desse repositorio nao listou. Siga exatamente o que ele imprimir; o nome da branch nunca leva o prefixo do
commit/titulo. Antes de criar branches, confira com `git fetch origin && git ls-remote --heads origin <base>` que
as bases existem; se nao existirem ou o caso nao estiver claro, **pergunte**. Commits so com a mudanca
necessaria: **sem comentarios novos no codigo** (ver passo 8).

Depois do `push`: `pr-text` na branch de correcao → gere `desc.md`/`rca.md` (passo 8) → `save-pr-text` → as
linhas de `open-pr` que o `branches` imprimiu (com o `desc.md` do repositorio).
Conflito no `cherry-pick`: resolva mantendo a intencao da correcao, `git cherry-pick --continue`, registre um
`log warning` e mencione no PR. **Nunca encadeie o `push` no mesmo comando do `cherry-pick`** (card 75648: o
conflito passou e a branch derivada subiu sem a correcao) — rode a linha de `push` que o `branches` imprimiu, que so
empurra com o cherry-pick concluido.

**Edicao e testes novos** (card 75648) — testes da correcao: passo 8, *Validar com testes*.
- Edicao de arquivo com `python3` ou a ferramenta de edicao — nao `sed -i 's/...'` (o `sed` do macOS exige
  `sed -i ''` e quebra com `/` no texto).
- Teste novo so quando o repositorio ja testa aquela camada; nao escreva teste de cancelamento/infra que precise
  de mocks que o projeto nao tem.

### 8. Executar o plano de correcao
Repita: `control` → pegue a proxima etapa **pronta** do `executor: claude` → faca (com `log`/`sync`/`checkpoint`)
→ `step completed`. Cada etapa e uma sub-tarefa do item de trabalho do plano — o PRMake sincroniza sozinho (0085).
Regras:
- **Mudar codigo so depois da resposta do usuario** (passo 6) e so no escopo do card.
- **Nao adicione comentarios no codigo.** A correcao deve ser so a mudanca de codigo — sem comentarios
  explicando o bug, a correcao, o card (`// AB#...`, `// fix: ...`, `// antes era...`), sem blocos comentados
  e sem remover/alterar comentarios existentes que nao tenham relacao com a mudanca. O porque da correcao vai
  na mensagem do commit (`CommitMessagePattern`), na descricao do PR e no plano/Timeline — nunca no codigo.
  Excecao rara: so se o proprio arquivo exigir (ex.: doc obrigatoria de API publica) e, mesmo assim, o minimo.
- **Validar com testes (0069)** — so os testes do que mudou, **uma vez**:
  - **Front/Node (jest)**: `bash $PLAN test <card> "$WT"` (timeout do Bash: 600000). Ele acha os specs dos arquivos alterados (o `.spec.ts` ao
    lado de cada arquivo mudado + specs mudados), roda sem cobertura e encerra o runner no limite configurado no PRMake
    (`CorrectionTestMaxSeconds`, padrao 420 s). Resultado → `log` na etapa `validar`:
    - `OK` / `SEM-SPEC` / `DESLIGADO`: conclua a validacao (sem spec ou desligado: o CI do PR testa).
    - `FALHOU`: corrija (se a falha e da sua mudanca) e rode o script de novo; falha que ja existia na base: registre e siga.
    - `LENTO` (exit 124) ou `SEM-RUNNER` (exit 3): **nao repita** (nem com `--maxWorkers`, pastas, `npm test`, `npm
      install`): registre no plano e siga para o PR — o CI do PR roda o build e os testes. Sem jest (back .NET/Java): o
      build do projeto alterado basta.
    - **Nunca** rode o jest/`npm test` direto, em pastas inteiras ou na suite toda.
  - **Back .NET** (card 75648): `dotnet build <sln>` **uma vez** e `dotnet test <projeto-de-teste> --no-build
    --filter "FullyQualifiedName~<Classe>"` so nas classes de teste do que mudou — cada `dotnet test` sem
    `--no-build` recompila a solucao. Java: o build do modulo alterado basta.
  - Depois do cherry-pick nas branches do fluxo (dev/qa...) **nao compile nem teste de novo** — so se resolveu
    conflito na mao: ai so o que voce tocou (`$PLAN test` ou build + `--no-build` filtrado) nessa branch.
- **CI do PR (0069)**: o PRMake acompanha o CI de cada PR do plano e mostra na etapa (CI ok / rodando / falhou). Falha que a base ja
  tem (o mesmo check vermelho nos PRs recentes para a mesma branch — ex.: `users` em todo PR para development) aparece
  como "ja falhava na base" e **nao e sua**: nao corrija. So falha nova retoma o card ("o CI de um PR falhou"):
  `prmake_plan` mostra o PR, o `ci` e os checks (`alreadyFailsOnBase` = da base); `gh pr checks <url-do-pr>` e `gh run view <run-id> --log-failed | tail -120`
  (o id esta no link do check) dao o log. Falha da sua mudanca → corrija no worktree, na branch daquele PR
  (`$PLAN test` so dos arquivos da falha), commit no padrao e `push` — o CI roda de novo e o PRMake acompanha. Falha
  de infraestrutura/instavel ou de outra parte do codigo → `log` (kind `warning`) com o porque e encerre a vez, sem
  mexer no codigo.
- **Diff feito na fotografia (0075)**: depois do commit da correcao, para cada ref que a correcao mudou, `bash $PLAN coderef
  <card> <ref> --implemented <sha> --file <caminho> --dir "$WT"` — o popover mostra *Proposta x Feito*. Arquivo mudado
  sem ref na analise (a correcao precisou de mais): fotografe com `--dir "$WT"` antes do commit (`--change change --after`)
  e grave o `--implemented` depois.
- **Codigo alterado vira entrega do item (0091)**: ao terminar cada etapa `code` (commits feitos no worktree, antes do
  `advance`), `bash $PLAN deliver-code <card> <repo> --step <key-da-etapa>` (no worktree do card; `--dir "$WT"` se
  estiver em outro lugar; `--base <base>` se a branch nao nasceu de `origin/<base>`). Grava a branch, a base, os commits
  e o **diff completo** na sub-tarefa da etapa — a pessoa ve o codigo alterado ao abrir a sub-tarefa na Bancada. O diff
  vai do worktree direto para o PRMake (nao leia nem cole o diff no chat). Novos commits na mesma etapa (CI, nova
  rodada) → rode de novo (substitui). O `prmake_advance` avisa quando uma etapa de codigo da correcao conclui sem isso.
  PRs abertos com `open-pr`, acoes do DevOps e Timeline viram entrega da etapa em andamento sozinhos.
- Etapa de PR — **descricao no layout padrao do PRMake, salva no card**, antes de abrir os PRs de cada repositorio:
  1. `bash $PLAN pr-text <card> <repo> <branch-correcao>` (a do `branches`, ja no GitHub): baixa o **prompt
     configurado** (o mesmo da `gerar-prmake`), os repro steps e o diff em `$CARD_DIR/pr/<repo>/`.
  2. Gere `pr_generated.md` seguindo o prompt **exatamente como a `gerar-prmake`** (ingles, markdown, titulos em
     negrito; Bug com o RCA entre `<RCA>`/`</RCA>`; foco so no caso do card) e separe `desc.md`/`rca.md` com os
     comandos que o `pr-text` imprime.
  3. `bash $PLAN save-pr-text <card> $CARD_DIR/pr/<repo>/desc.md $CARD_DIR/pr/<repo>/rca.md pr-<repo>`: salva a
     descricao e o root cause **no card do PRMake** (ficam prontos no "Abrir PR" para quem quiser abrir um PR por
     fora) e guarda os arquivos no plano. Com mais de um repositorio, o card fica com a descricao do ultimo
     salvo — gere a do repositorio principal por ultimo (ou uma que cubra todos).
  4. Abra os PRs com essa descricao: as linhas de `open-pr` que o `branches` imprimiu (branch, destino e titulo
     conforme a configuracao) com `$CARD_DIR/pr/<repo>/desc.md`. Sem titulo, o `open-pr` usa o `PrTitlePattern`.
- Etapa de PR: os PRs abertos com `open-pr` ficam no card, na Timeline e anexados a etapa. **Nunca faca merge,
  nem aprove** — a etapa fica aguardando e conclui sozinha quando outra pessoa mesclar.
- Etapa do usuario: diga o que fazer, citando os arquivos pelo nome (ex.: "abra o chamado com o texto de
  `chamado-<card>-x.md` e anexe `01_<card>_x.sql` — os dois estao na etapa do chamado, no plano de correcao — e use *Anexar chamado*
  na etapa (ou na sub-tarefa da Bancada) com o link; a etapa conclui sozinha quando voce marcar o chamado como
  resolvido") — o *Concluir* da linha fica para etapas sem chamado (a etapa aparece em "Aguardando voce" no topo do
  plano e no sino) —, e siga com as outras etapas prontas.
- Etapa sua travada por algo do usuario (permissao, VPN, credencial, acesso): `block` na hora — ver
  `ref.sh plano block`.
- **Entrou em espera → contexto da passagem (0075)**: sempre que o plano passa a esperar algo de fora — chamado aberto,
  PRs aguardando aprovacao/merge, correcao em QA esperando a validacao — grave **na hora** `prmake_handoff(card, texto)`
  (script: `handoff <card> "..."`): ate ~1.500 caracteres, para alguem que nunca viu o card assumir — **o que foi feito**
  (correcao, PRs, script), **o que se espera e de quem** (revisor, time do chamado, QA), **riscos/pegadinhas** (o que
  checar se o chamado voltar recusado, o que o QA deve testar, refs `[...](ref:<id>)` do que mudou) e **como retomar**.
  Os fatos (PRs, aprovacoes, CI, chamado, estado no DevOps, desde quando) o PRMake ja junta sozinho no resumo
  *Situacao atual* do card — nao repita links/status, explique o contexto. Atualize quando a espera mudar (chamado
  resolvido → PR; PR mesclado → QA); o PRMake tira do resumo o contexto gravado antes da espera atual.
  **Escrita do contexto (vai para a discussion do DevOps, no nome de quem trata o card):** em primeira pessoa (0096:
  "Abri o chamado de limpeza...", "Vou conferir...") e no formato de status — a espera e
  "Aguardando abrir chamado", "Aguardando o atendimento do chamado", "Aguardando o merge do PR #489", "Aguardando
  validacao em QA"; **nunca** "voce", "quem trata o card", "falta voce abrir...", "conclua a etapa", "no PRMake" nem
  instrucoes de tela do PRMake — o comentario parece escrito pela pessoa.
- Sem nada pronto do seu lado (so aguardando merge/chamado/usuario): resuma o que falta, **deixe o vigia
  (`watch`) rodando em segundo plano** e encerre a vez — ele te acorda quando algo mudar e voce segue sozinho.
  O PRMake acompanha os PRs no GitHub (e mostra na tela sem cliques); o plano de correcao **conclui sozinho**
  quando todas as etapas terminam (PRs mesclados, chamados resolvidos) — e o item de trabalho do plano junto — ai o vigia termina com exit 11 e voce
  faz o relatorio final.

### 8b. A correcao nao resolveu — nova rodada no mesmo plano
Se o usuario disser que a correcao nao resolveu (ou a validacao falhar), **nao crie outro plano**: acrescente
etapas ao **mesmo plano de correcao** (`use <card> correction` e `steps` com as etapas atuais **mais** as novas —
keys novas, ex.: `ajustar-edv-solvace-2`, `pr-edv-solvace-2` com `dependsOn` na de ajuste, e a validacao de novo).
Ao marcar a primeira etapa nova como `running`, o plano volta a "em andamento". Corrija nas mesmas branches do
fluxo (as do `branches` — novos commits no padrao configurado e cherry-pick) e abra os PRs novos com
`open-pr`: eles entram na etapa de PR nova (os PRs antigos continuam na etapa anterior) e o plano so conclui de
novo quando os PRs novos forem mesclados. Todo PR aberto pelo PRMake tambem aparece no card (painel de PRs e
Timeline).

### Itens de trabalho ricos (0087) — a pessoa entende tudo sem abrir o codigo

Quem abre o item (ou a sub-tarefa) na Bancada deve entender, **de forma clara e objetiva, o que sera feito, cada passo e
cada alteracao**. Por isso, ao montar o plano de correcao e os itens, **antes** de chamar `prmake_correction` /
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
