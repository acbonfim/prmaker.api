# Propor solucoes, plano de correcao e execucao (passos 6 a 8b)

Leia so a secao do passo em que esta (`bash ~/.claude/skills/analisar-bug/scripts/ref.sh correcao <6|7|8|8b>`).

### 6. Propor solucoes e perguntar (etapa `propor-solucoes`)
Com a analise publicada, a analise ainda nao terminou: **proponha as solucoes** e **decida com o usuario**.
1. `step propor-solucoes running`. Escreva `$CARD_DIR/analises/solucoes.md`: 1 a 3 opcoes, cada uma com o que
   muda (repositorios/arquivos), riscos, se precisa de script de dados (chamado) e o esforco; marque a
   recomendada. **Opcao com chamado (0050): nao grave o script nem o texto do chamado aqui** — descreva o que o
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
   - Se ha script de dados: vai por **chamado** (o usuario abre no sistema de chamados configurado — `TicketSystem`
     do `settings`) ou nao e necessario.
   - Se pode seguir com a correcao agora (ou so deixar o plano pronto).
   Use opcoes objetivas + texto livre. Mostre as mesmas perguntas no terminal. **O `label` de cada opcao e o
   texto que o usuario le no botao e o que vai para a Timeline** — escreva a opcao em si ("Corrigir no backend
   (recomendada)", "Script de dados via chamado"), nunca "Opcao 1"/"A"; detalhes vao em `description`.
   **Resumo para a correcao (obrigatorio, junto com as perguntas)** — `prmake_checkpoint(card, "propor-solucoes",
   texto)` (sem MCP: `checkpoint <card> propor-solucoes "..."`), ate ~3.000 caracteres, **autossuficiente**: no
   executor a correcao roda numa sessao nova que le **so isto** (+ respostas, comentarios e arquivos), sem esta
   conversa. Inclua: causa raiz e a evidencia (ids, consultas, host/banco usados); onde mexer em cada opcao — repo
   (legado `edv-solvace` ou `revamp-<modulo>`), `caminho:linha`, funcao/SP; consultas usadas (nomes nos arquivos do
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
   (fim do plano de **analise**).

### 7. Montar o plano de correcao
Monte as etapas **a partir do(s) padrao(oes) confirmados e das especificidades deste card** — o catalogo da
as etapas tipicas, voce adapta (nomes, descricoes com o que exatamente sera feito neste card, quem executa,
dependencias) e **sempre termina com o fechamento** (`fechar-card`, executor claude, com os comandos `devops`) —
**ele e a ultima etapa** (nada do usuario depois dele, salvo `validar-qa` no fluxo com codigo, que vem antes do
Ready for QA).
So tem etapas de codigo/PR se houver codigo. Crie com `correction` — ele vira o
plano ativo e a tela mostra as abas *Analise* e *Correcao*. Cada etapa tem `executor` (`claude` ou `user`),
`kind` e, quando for o caso, `repository` e `dependsOn`:

| Etapa | kind | executor | Observacao |
|---|---|---|---|
| Corrigir o codigo — **uma por repositorio** (`corrigir-<repo>`) | `code` | claude | branches e commits do fluxo abaixo |
| Validar (build/testes/reproducao) | `validation` | claude ou user | depende da correcao |
| **PRs — uma por repositorio** (`pr-<repo>`) | `pr` | claude | todos os PRs daquele repositorio; conclui sozinha quando **todos** forem mesclados |
| Chamado de script de dados (`chamado-<nome>`) | `ticket` | **user** | crie com `dependsOn` numa etapa sua que prepara o script (ex.: `preparar-script-<nome>`, `claude`); **antes** de concluir essa etapa (antes de a do chamado ficar com o usuario), anexe no **plano de correcao**, com `key` = a etapa do chamado: o `.sql` de alteracao com rollback (`prmake_file(card, "01_<nome>.sql", ..., "script", "<etapa>", phase: "correction")`) e o texto do chamado (`prmake_file(card, "chamado-<nome>.md", ..., "ticket", "<etapa>", phase: "correction")` — 1a linha = titulo; corpo com o link do card, cliente, ambiente, banco, o que o script faz, rollback e a consulta de validacao). A API recusa deixar a etapa com o usuario sem os dois (`ticketStepsMissingFiles` no `prmake_control`). Na descricao da etapa cite os arquivos pelo nome ("Abra o chamado no `TicketSystem` com o texto de `chamado-x.md` e anexe `01_x.sql`"); o usuario abre o chamado, cola o link na tela (etapa fica *aguardando* ate o chamado ser marcado resolvido) |
| Configuracao na tela do sistema (`configurar-<o que>`) | `task` | **user** | voce nao acessa o sistema do cliente: escreva o passo a passo exato (ambiente, tela, campo, valor antes/depois) em `analises/configuracao.md`; o usuario executa e conclui a etapa na tela |
| ~~`orientar-cliente`~~ — **nao e etapa** | — | — | a orientacao ao cliente (o que aconteceu, o que fazer, passo a passo, por que nao e bug) **e o resumo nao tecnico PT/EN** que o `fechar-card` publica na discussion: redija em `analises/orientacao-cliente.md` e use esse texto no resumo (ver abaixo) |
| Validar em QA (`validar-qa`) — **com codigo** | `validation` | **user** | depois dos PRs (correcao publicada em QA); quando ela ficar pronta, mova o card para Dev Test in QA (`devops <card> dev-test-in-qa`); o usuario conclui a etapa na tela quando validar |
| Validar o ambiente (`validar-dados`, `validar-configuracao`) | `validation` | claude ou user | **antes** do fechamento, quando o caso pede conferir o resultado (ex.: SQL somente leitura depois do script). A confirmacao **com o cliente** nao e etapa: ela acontece depois do *Test in Production*, fora do plano — so crie uma etapa assim se o usuario pedir |
| Gerar PRMake (RCA no DevOps, resumo, campos) — **com codigo** | `task` | claude | skill `gerar-prmake` **sem** abrir PR (`OPEN_GITHUB_PR` desligado), **reaproveitando** a descricao/RCA ja gerados e salvos na etapa de PR (nao gere de novo) |

**Tratamento sem codigo** (o plano e dinamico — monte so o que o caso pede): quando a solucao e user education,
configuracao direto na tela do sistema ou algo do tipo, **nao crie nem sugira** etapas de correcao de codigo,
branches, PRs, "Gerar PRMake", chamados ou scripts — a menos que o caso realmente precise (ex.: so um script de
dados → so a etapa de chamado). O plano pode ter apenas, por exemplo:

```json
[{"key":"configurar-permissao","title":"Ajustar a permissao do perfil no sistema","kind":"task","executor":"user",
  "description":"Passo a passo em analises/configuracao.md"},
 {"key":"fechar-card","title":"Fechamento: root cause, classificacao, resumo PT/EN (orientacao ao cliente) e Test in Production",
  "kind":"task","executor":"claude","dependsOn":["configurar-permissao"],
  "description":"O resumo PT/EN publicado na discussion e a orientacao ao cliente (texto de analises/orientacao-cliente.md)"}]
```

User education puro (nada a configurar) = **so o `fechar-card`**.

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
titulo) e os **comandos prontos** (`git checkout`/`worktree` ja com a pasta, `cherry-pick`, `push` e as linhas de
`open-pr`). Exit 4 = a pasta do repositorio e desconhecida (fora do mapa ou mais de um clone): pergunte ao usuario,
fixe com `bash ~/.claude/skills/.prmake/prmake-skills.sh repos set <repo> <pasta>` e rode o `branches` de novo. Siga exatamente o que ele imprimir; o nome da branch nunca leva o prefixo do
commit/titulo. Antes de criar branches, confira com `git fetch origin && git ls-remote --heads origin <base>` que
as bases existem; se nao existirem ou o caso nao estiver claro, **pergunte**. Commits so com a mudanca
necessaria: **sem comentarios novos no codigo** (ver passo 8).

Depois do `push`: `pr-text` na branch de correcao → gere `desc.md`/`rca.md` (passo 8) → `save-pr-text` → as
linhas de `open-pr` que o `branches` imprimiu (com o `desc.md` do repositorio).
Conflito no `cherry-pick`: resolva mantendo a intencao da correcao, registre um `log warning` e mencione no PR.

### 8. Executar o plano de correcao
Repita: `control` → pegue a proxima etapa **pronta** do `executor: claude` → faca (com `log`/`sync`/`checkpoint`)
→ `step completed`. Regras:
- **Mudar codigo so depois da resposta do usuario** (passo 6) e so no escopo do card.
- **Nao adicione comentarios no codigo.** A correcao deve ser so a mudanca de codigo — sem comentarios
  explicando o bug, a correcao, o card (`// AB#...`, `// fix: ...`, `// antes era...`), sem blocos comentados
  e sem remover/alterar comentarios existentes que nao tenham relacao com a mudanca. O porque da correcao vai
  na mensagem do commit (`CommitMessagePattern`), na descricao do PR e no plano/Timeline — nunca no codigo.
  Excecao rara: so se o proprio arquivo exigir (ex.: doc obrigatoria de API publica) e, mesmo assim, o minimo.
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
  `chamado-x.md` e anexe `01_x.sql` — os dois estao na etapa do chamado, no plano de correcao — e cole o link na
  etapa no PRMake") e que o botao *Concluir* esta na propria linha da etapa no PRMake (ela aparece em
  "Aguardando voce" no topo do plano e no sino), e siga com as outras etapas prontas.
- Etapa sua travada por algo do usuario (permissao, VPN, credencial, acesso): `block` na hora — ver
  `ref.sh plano block`.
- Sem nada pronto do seu lado (so aguardando merge/chamado/usuario): resuma o que falta, **deixe o vigia
  (`watch`) rodando em segundo plano** e encerre a vez — ele te acorda quando algo mudar e voce segue sozinho.
  O PRMake acompanha os PRs no GitHub (e mostra na tela sem cliques); o plano de correcao **conclui sozinho**
  quando todas as etapas terminam (PRs mesclados, chamados resolvidos) — ai o vigia termina com exit 11 e voce
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
