# Plano de execucao no PRMake — comandos e regras

Lido quando precisar do detalhe de um comando do plano (`$PLAN` = `~/.claude/skills/analisar-bug/scripts/prmake-plan.sh`). As regras essenciais estao resumidas na SKILL.md.

## Plano de execucao no PRMake (obrigatorio em todo o fluxo)

Quem acompanha o card no PRMake ve, ao vivo, os planos (etapas), o que voce esta fazendo, as perguntas e os
arquivos. Por isso: **nada fica so na memoria/na maquina** — tudo o que voce produz vai para o plano em pedacos.
O PRMake tambem escreve sozinho na Timeline os marcos (perguntas, respostas, plano de correcao, etapas, chamados,
PRs mesclados, conclusao) — **nao duplique** esses registros com a `prmake-timeline`.

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh
bash $PLAN contexto <card>                                           # 1 comando: card, repro, plano (cria/retoma), comentarios, Base Solvace, KC
bash $PLAN usage <card>                                              # custo desta sessao no plano (vai sozinho ao mudar o status)
bash ~/.claude/skills/analisar-bug/scripts/prmake-card.sh <card>     # (terminal) volta para a sessao do card ou abre uma nova
bash $PLAN start <card> "Analise do bug <card>: <titulo curto>"      # cria OU retoma o plano aberto do card
bash $PLAN steps <card> - <<< '[{"key":"...","title":"...","description":"..."}, ...]'  # refina as etapas
bash $PLAN step <card> <key> running                                  # comecou a etapa
bash $PLAN log <card> <key> progress "Lendo OrderService.cs (fluxo de aprovacao)"   # pedaco de andamento
bash $PLAN log <card> <key> finding  "UserStatus = FORCE_CHANGE_PASSWORD no pool demo"
bash $PLAN log <card> <key> decision "Nao vou consultar o SQL: o erro e de validacao no front"
bash $PLAN checkpoint <card> <key> "Onde parei / o que falta nesta etapa"   # para retomar
bash $PLAN sync <card> <key>                                          # envia arquivos novos/alterados da pasta do card
bash $PLAN step <card> <key> completed                                # terminou
bash $PLAN step <card> <key> cancelled "Motivo (ex.: nao necessario — bug so no front)"  # pulou
bash $PLAN control <card>                                             # entre etapas: 0 segue, 10 pausado, 11 parar
bash $PLAN wait <card>                                                # pausado: espera o "Continuar" da tela
bash $PLAN watch <card>                                               # VIGIA em segundo plano: acorda voce quando algo muda no PRMake
bash $PLAN ask <card> - <<< '[{"stepKey":"propor-solucoes","text":"...","options":[...]}]'  # pergunta
bash $PLAN wait-answers <card>                                        # espera as respostas (tela ou terminal)
bash $PLAN answer <card> <n> "resposta dada aqui no terminal"         # grava no PRMake (via claude)
bash $PLAN correction <card> "Correcao do bug <card>: <solucao>" - <<< '[...etapas...]'  # plano de correcao
bash $PLAN use <card> analysis|correction                             # em qual plano os comandos agem
bash $PLAN link <card> <key> <url> "titulo" [--blocks]                # anexa link (chamado/doc) a etapa
bash $PLAN notes <card> [n]                                           # comentarios do usuario no plano + anexos baixados
bash $PLAN attachment <card> "<ref>"                                  # baixa um arquivo citado ("imagem 2", "#12", nome)
bash $PLAN note <card> "texto" [arquivos...] [--step key]             # voce comenta no plano (resposta/observacao)
bash $PLAN settings <card>                                            # configuracao das skills no PRMake
bash $PLAN branches <card> <repo> [--flow f] [--base b]               # branches/PRs/titulos do repo (da configuracao)
bash $PLAN pr-text <card> <repo> <branch-correcao>                    # prompt configurado + diff (layout padrao)
bash $PLAN save-pr-text <card> desc.md rca.md <key>                   # salva descricao/RCA no card do PRMake
bash $PLAN open-pr <card> <repo> <branch> <destino> "<titulo>" desc.md   # abre PR (nunca merge) — linha pronta no `branches`
bash $PLAN status <card> completed "" "$CARD_DIR/analises/analise-inicial.md"   # fim do plano ativo
```

Regras:
- **Comece por ele.** Assim que souber o numero do card (passo 1), rode `start`. Se ele responder
  `PLANO INDISPONIVEL` (PRMake fora do ar ou sem o recurso), siga normalmente: os comandos do plano viram
  no-op. Se ja existir um plano aberto (analise **ou correcao**), o `start` **retoma** o mais recente e imprime
  as etapas com status, checkpoints, perguntas, links e arquivos: continue da **primeira etapa pronta**, usando
  o checkpoint; rode `pull` se a pasta do card nao tiver os arquivos. **Analise ja concluida sem plano de
  correcao** (inclusive a feita na versao anterior da skill, que parava em `publicar`): o `start` a reabre para
  o passo 6 (acrescenta `propor-solucoes` se faltar) — **nao refaca a analise**; use a analise publicada
  (`analises/analise-inicial.md`) para propor as solucoes. So use `start ... --new` se o usuario pedir uma
  analise nova do zero.
- **Refine o plano de analise** depois de ler o card (passo 2): ajuste titulos/descricoes ao caso concreto (a
  descricao diz *o que vai ser feito*), remova/cancele (com motivo) o que nao se aplica e acrescente o que
  surgir. Keys: minusculas, numeros, `-`/`_`, estaveis (nunca renomeie uma key).
- **Em cada etapa:** `step running` → `log` a cada acao relevante (arquivo lido, hipotese, achado, decisao,
  consulta — frases curtas, markdown ok; `finding` para achados, `decision` para escolhas, `warning`/`error`
  para problemas) → salve os arquivos na pasta do card e rode `sync <card> <key>` → `checkpoint` → `step completed`.
- **Entre etapas (e antes de qualquer coisa demorada):** `control`. Exit **10** = pausado pelo usuario: rode
  `wait <card> 3600` **em segundo plano** (Bash com `run_in_background: true`), avise que ficou pausado e encerre a
  sua vez — quando o usuario clicar *Continuar* no PRMake o comando termina e te acorda (exit 0 = continue;
  11 = cancelado: pare). Exit **11** = cancelado/concluido pela tela: pare e
  nao envie mais nada. O `control` tambem lista: etapas **canceladas** (pule), **prontas** (pode comecar),
  **aguardando** (resposta/chamado/merge — nao mexa) e **perguntas sem resposta**.
- **Etapas do usuario** (`executor: user`, ex.: abrir o chamado, validar em QA): diga ao usuario o que fazer
  (com o texto/arquivos prontos) e **siga com as outras etapas prontas** — o usuario conclui a dele na tela.
- **Nunca fique parado esperando o PRMake sem o vigia.** O PRMake nao consegue chamar esta sessao; quem te
  acorda e um comando rodando em segundo plano. Sempre que a sua vez for terminar com algo pendente de fora
  (etapa do usuario, chamado, PR aguardando merge, perguntas, plano pausado), rode
  `bash $PLAN watch <card>` **em segundo plano** (Bash com `run_in_background: true`) e so entao encerre a vez
  dizendo o que esta aguardando. Quando o vigia terminar, a saida diz o que mudou (etapa concluida pelo
  usuario, chamado resolvido, PR mesclado → etapa de PR concluida, respostas, pausa/continuar, plano
  concluido/cancelado): rode `control` e **continue sozinho** com a proxima etapa pronta — sem pedir ao usuario
  para escrever nada. Se ainda restar espera, rode o vigia de novo. Exit 11 = plano concluido/cancelado: faca o
  relatorio final (passo 9) e pare. O usuario nao precisa clicar em nada nem avisar no chat.
  (Se a sessao for fechada, o vigia morre junto — `/analisar-bug <card>` retoma depois.)
- **Comentarios e anexos do usuario (PRMake)** — o usuario pode escrever comentarios no plano e anexar imagens
  (arrastando, colando com Ctrl+V ou pelo icone de anexo) e arquivos (logs, planilhas, PDFs). Cada comentario e
  cada arquivo tem um **numero por card** (`comentario #3`, `anexo #12`), o mesmo nas abas Analise e Correcao.
  **Isso e entrada da analise, com o mesmo peso dos repro steps**:
  - Rode `notes <card>` logo depois do `start` (antes de investigar), sempre que o vigia (`watch`) ou o
    `wait-answers`/`control` avisar que os comentarios mudaram, e antes de propor solucoes ou montar o plano de
    correcao. Ele imprime os comentarios (marca os **novos**) e **baixa os anexos** para
    `$CARD_DIR/anexos-prmake/`, mostrando o caminho local de cada um.
  - **Abra cada anexo novo com a ferramenta Read** (imagens aparecem para voce; PDF com `pages`; logs/CSV/JSON
    como texto) e considere o que ele mostra: tela com erro, dados, passo a passo, configuracao. Registre com
    `log <card> <key> finding "Considerado o anexo #12 (print da tela X): ..."` e cite comentarios/anexos na
    analise e nas decisoes.
  - **Referencias**: quando o usuario — aqui no terminal, numa resposta dada no PRMake ou num comentario — disser
    "veja a imagem 2", "olha o anexo #12", "o print.png", "comentario 3", resolva antes de responder:
    `attachment <card> "imagem 2"` / `attachment <card> "#12"` / `attachment <card> print.png` (baixa e mostra o
    caminho) ou `notes <card> 3`, e **abra o arquivo com Read**. Nunca diga que nao consegue ver um anexo do
    PRMake sem tentar esses comandos. Ambiguo (o comando lista candidatos) → pergunte qual.
  - Se o comentario pedir algo (ex.: "considere tambem o ambiente X"), trate como instrucao do usuario; se mudar
    o plano, ajuste as etapas. Pode responder no proprio plano com `note <card> "..."` (aparece na tela e na
    Timeline) — util quando o usuario comentou pela tela e nao esta no terminal.
- **Falha de rede** nao interrompe a skill: o envio vai para uma fila local e e reenviado na proxima chamada
  (`flush` forca). Erro 400 (ex.: plano cancelado) interrompe — leia a mensagem.
- PII: o que vai para o plano fica visivel no PRMake — mesmo cuidado da timeline.

Etapas padrao da analise (o `start` cria se voce nao passar outras): `identificar-card`, `coletar-dados`,
`investigar-codigo`, `consultar-ambiente` (opcional — cancele com motivo se nao precisar), `causa-raiz`,
`montar-analise`, `publicar`, `propor-solucoes`.
