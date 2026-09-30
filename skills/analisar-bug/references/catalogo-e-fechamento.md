# Catalogo de tratamentos, configuracao e fechamento do card

Lido ao reconhecer o padrao do caso (passo 3/6), ao montar o plano de correcao (passo 7) e no fechamento.

## Catalogo de tratamentos (aprendido dos cards reais)

Estudo de 300 bugs resolvidos (ultimos 120 dias, 2026-09) e das timelines do PRMake: **58% foram resolvidos sem
codigo**. Cada card e um caso: use o catalogo para **reconhecer o(s) padrao(oes)** e montar **etapas proprias
do caso** — nunca uma lista fixa, e combine padroes quando for o caso (ex.: codigo + correcao de dados; script +
orientacao ao cliente).

| Padrao | ~% | Sinais tipicos | Diagnostico | Etapas tipicas (adapte) | `devops classify` |
|---|---|---|---|---|---|
| **A. Defeito de codigo** | 32 | reproduz em qa/sandbox; comportamento contradiz a regra; erro identificavel no codigo | codigo (legado/revamp), reproducao | `corrigir-<repo>` → validar (build/local) → `pr-<repo>` (com `pr-text`/`save-pr-text`) → `validar-qa` (usuario) → fechamento | `code-fix` (ou `code-data-fix` se tambem corrigiu dados causados pelo defeito) |
| **B. Correcao de dados (script via chamado)** | 11 | registro excluido/estado inconsistente, preferencia ausente, flag/menu errado, `LAST_SITE_ID`/site/area errados | SQL **somente leitura** (`sql-query.sh`) | `montar-script` (voce: `scripts/01_*.sql` + `99_rollback_*.sql` + texto do chamado) → `chamado-script` (usuario, link com `--blocks`) → `validar-dados` (voce, SQL somente leitura depois do script) → fechamento | `script-defect` (causa foi defeito) · `script-user-action` (causa foi acao do usuario) · `script-environment` |
| **C. Acesso / Cognito / SSO** | (parte de B, D, E) | loop de login, usuario desabilitado/deslogado no Cognito, SSO trocou o e-mail, usuario nativo + federado duplicados, usuario sem planta/area | `cognito-query.sh` + SQL | ajuste no Cognito ou no cadastro → **etapa do usuario** (voce nao escreve no Cognito) com o passo a passo; se precisar de banco → padrao B; confirmar o login | conforme o que foi feito: `script-*`, `configuration` ou `user-education` |
| **D. Configuracao / ambiente / plataforma** | 11 | parametro de ambiente, WAF/instancia, habilitar modulo, criar usuarios de treino, configuracao de tela | codigo + ambiente + dados | `configurar-<o que>` (usuario ou time de infra — voce redige o passo a passo exato em `analises/configuracao.md`) → validar → fechamento | `configuration` (ou `configuration-change-request`) |
| **E. User education (nao e defeito)** | 28 | funciona conforme a regra de negocio; filtro/uso incorreto; expectativa diferente do produto | provar pelo codigo/dados que o comportamento e o esperado | `orientar-cliente` (usuario; voce redige a orientacao PT/EN em `analises/orientacao-cliente.md`, sem jargao) → fechamento | `user-education` (ou `user-education-change-request`) |
| **F. Change request / requisito nao mapeado** | 5 | pedido de algo que o produto nao faz | confirmar que nao e defeito | redigir a justificativa e o encaminhamento (usuario) → fechamento | `change-request` · `not-mapped-requirement` |
| **G. Nao reproduz / sem retorno** | 10 | sem evidencia do erro; instabilidade passada; cliente nao responde | tentativas em qa/sandbox, logs, dados | registrar as tentativas (voce) → `pedir-informacoes` ao cliente (usuario) → fechamento | `cannot-reproduce` · `no-user-feedback` |
| **H. Duplicado** | 3 | mesmo problema de outro card | buscar o card original | vincular/avisar (usuario) → fechamento | `duplicated` |

**Quem grava e o PRMake — sempre.** Voce le o card (pelo PRMake), investiga e **gera os textos** (analise, root
cause, resumo nao tecnico PT/EN, descricao do PR, passo a passo, orientacao ao cliente), mas **nada e gravado
direto no Azure DevOps**: toda gravacao (root cause, resumo na discussion, classificacao, estimativa, Remaining,
mudanca de estado, registro do card, PRs, timeline) vai pelos endpoints do PRMake — ele usa a integracao do
Azure/GitHub que o usuario configurou, registra na Timeline e guarda as configuracoes por usuario. Se faltar um
endpoint para algo que precisa gravar, **pare e avise** (nao contorne com PAT/API direta).

**Configuracao vem do PRMake — nada presumido.** Tudo o que pode mudar fica configurado no PRMake (tela de
plugins) e muda sem atualizar a skill:
- **Skills Configurations** (`bash $PLAN settings <card>`): fluxo de branches pela area do card
  (`BranchFlowByArea`), estrategia por tipo de repositorio e fluxo (`BranchStrategy`: base, branches derivadas,
  cherry-picks, destinos dos PRs), nome da branch (`BranchNamePattern`), mensagem de commit
  (`CommitMessagePattern`), titulo do PR (`PrTitlePattern`), repositorio padrao e o sistema de chamados
  (`TicketSystem`). O `branches <card> <repo>` ja aplica tudo isso e imprime os comandos prontos.
- **Acoes DevOps** (`bash $PLAN devops <card> config` e `devops <card> classifications`): estados e areas de
  destino, area exigida, comentario, estimativa inicial, prompt do resumo nao tecnico e opcoes de classificacao.
- **Campos do DevOps** (nomes dos campos de classificacao, Remaining, estimativa, root cause) e prompts do PR:
  usados pelo PRMake ao gravar; aparecem no `settings`.
Leia antes de perguntar ou de gravar e use os valores exatamente como vierem (nunca escreva de memoria nomes de
estado, area, branch ou titulo). Faltou uma regra (ex.: repositorio sem tipo, fluxo sem regra) → **pergunte ao
usuario** e sugira que o admin configure no PRMake. O que o card tem hoje (estado, area, tipo) vem do card lido
pelo PRMake (`bug-fetch.sh` → `state`/`area`).

**Fechamento (todo card, com ou sem codigo)** — o que os cards reais sempre tem no fim, via `devops` (endpoints
das "Acoes DevOps" do PRMake). Comece por `bash $PLAN devops <card> config`:
1. root cause no DevOps: `bash $PLAN devops <card> rootcause rca.md` (com codigo, a `gerar-prmake` ja faz);
2. classificacao: escolha pela evidencia a opcao correta entre as que o PRMake devolve em
   `devops <card> classifications` (a coluna da tabela acima mostra as usuais, mas o admin pode ajusta-las) e
   grave com `bash $PLAN devops <card> classify <opcao>`;
3. resumo nao tecnico PT/EN na discussion: se o `config` trouxe `summary-prompt`, **siga esse prompt** (arquivo
   `analises/summary-prompt.txt` da pasta do card; mesmos placeholders da `gerar-prmake`) mantendo o formato
   `**PT**` `---` texto `**EN**` `---` texto; senao use esse formato direto. Sem codigo, nao diga que houve
   correcao de codigo. Grave com `bash $PLAN devops <card> summary resumo.md`;
4. `bash $PLAN devops <card> zero-remaining` e, **se o usuario confirmou**, a mudanca de estado configurada:
   `devops <card> test-in-production` (so se o `config` a mostrar configurada e o card estiver na area exigida —
   senao o PRMake devolve 409) ou `devops <card> ready-for-qa`. Acao "NAO configurado" no `config` → nao ofereca;
   avise que o admin pode configura-la no PRMake (AI Configurations).

**Mover o card e sempre pelo PRMake — voce chama, o usuario nao move na mao.** `devops <card> ready-for-qa` e
`devops <card> test-in-production` chamam o endpoint do PRMake `POST Azure/card/<card>/actions/<acao>`; o PRMake
move o card com a integracao do Azure do usuario e registra na Timeline. A resposta do usuario a pergunta de
fechamento (passo 6, ou a pergunta que voce fizer no fim) **e a autorizacao**: assim que ela chegar (pela tela ou
pelo chat), rode o comando da acao escolhida — sem perguntar de novo e **sem pedir ao usuario para mover o card na
tela do DevOps/PRMake**. "Nao mover agora" → nao chame e registre no plano.
- O instalador das skills libera no Claude Code a regra
  `Bash(bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh devops:*)`. Para ela valer, rode os comandos
  `devops` com esse caminho literal e **cada um num comando proprio** (sem `PLAN=...;`, sem `&&`/`;`), ex.:
  `bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh devops <card> ready-for-qa`.
- Se o Claude Code barrar o comando (permissao/auto mode), explique que ele e o endpoint do PRMake (nao uma
  escrita direta no DevOps), peca para o usuario autorizar e rode de novo. Nao ofereca mover na mao como saida.
- `HTTP 502/503` com `TF10216`/"Azure DevOps services are currently unavailable" = DevOps fora do ar: o script ja
  tenta de novo por alguns minutos; se ainda falhar, rode de novo **em segundo plano** (Bash com
  `run_in_background: true`), ex.: `for i in 1 2 3 4 5 6; do sleep 120; bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh devops <card> <acao> && exit 0; done; exit 1`,
  registre um `log warning` e conclua o fechamento quando ele terminar com sucesso.
- `HTTP 409` = o card nao esta na area exigida pela acao: diga qual e a area e ofereca as outras acoes configuradas.
Sem codigo, antes do passo 1 salve o texto do tratamento no card com `save-pr-text` (descricao do que foi feito +
RCA) — o registro do card no PRMake passa a existir e o resumo fica vinculado.
