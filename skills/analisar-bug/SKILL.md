---
name: analisar-bug
description: Faz a analise (triagem tecnica) de um bug a partir do card no PRMake/Azure DevOps e do codigo do repositorio, publica a analise na Timeline, propoe solucoes perguntando ao usuario (responde no PRMake ou no Claude) e monta e executa o plano de correcao (codigo, PRs por repositorio seguindo o fluxo de branches Solvace, chamados) — so abre PRs, nunca faz merge. Tudo vai para o plano de execucao no PRMake em tempo real (o usuario acompanha, pausa, continua ou cancela pela tela do card; se a sessao cair, retoma de onde parou). Usa a Base Solvace (engenharia reversa + regras de negocio do Knowledge Center) antes de vasculhar codigo. Descobre o card pela branch atual (hotfix/<card> ou bugfix/<card>) ou por um numero informado. Considera os comentarios, imagens e arquivos que o usuario anexou no plano pelo PRMake. Use quando o usuario pedir para "analisar bug", "fazer analise inicial", "triagem de bug", "investigar o card", "retomar o card" ou similar — e tambem quando ele pedir para ver/analisar um anexo, imagem ou comentario do plano de um card no PRMake ("veja a imagem 2 do card 74519", "olha o anexo print.png", "leia o comentario 3").
---

# analisar-bug

Leva um bug **da analise a correcao**, em duas fases, cada uma com o seu plano de execucao no PRMake:
1. **Analise** — repro steps + comentarios do usuario, Base Solvace, codigo, causa raiz provavel, analise na
   Timeline e **solucoes propostas como perguntas ao usuario**.
2. **Correcao** — com as respostas, **plano de correcao** proprio do caso (codigo, PRs por repositorio, chamados,
   configuracao, orientacao ao cliente) e execucao da sua parte. **Voce so abre PRs — nunca faz merge.**

Instalada e atualizada pelo PRMake (fonte: `skills/analisar-bug` no repositorio do PRMake — nao edite a copia
instalada). Depende da skill `base-solvace` (instalada junto). Token: env `PRMAKE_TOKEN` ou
`~/.claude/prmake-token.txt` (HTTP 401/403 = token expirou).

```bash
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh    # plano de execucao (lista completa: ref.sh plano comandos)
REF=~/.claude/skills/analisar-bug/scripts/ref.sh              # referencias por SECAO: ref.sh [arquivo] [secao]
KB=~/.claude/skills/base-solvace/scripts/kb.sh               # Base Solvace: index [termos] | show <projeto> [secao] | find <termo>
KC=~/.claude/skills/base-solvace/scripts/kc.sh               # Knowledge Center: search <termos> | article <n>
```

## Economia de tokens (vale para todo o fluxo)
O custo e **respostas × contexto**: tudo o que entra (saida de comando, arquivo lido) e relido em **cada resposta
seguinte** — 10 KB lidos cedo numa analise de 80 respostas = ~200 mil tokens. Por isso:
- **Contexto inicial num comando**: `bash $PLAN contexto <card>` ja traz os campos do card (`dados/card-resumo.txt`),
  repro steps, plano, comentarios/anexos, os projetos da Base Solvace e os artigos do KC ligados ao card. Nao abra
  `card.json`, nao rode `kb.sh index` sem termos nem repita essas buscas.
- **Referencias so por secao, so na hora**: `bash $REF <arquivo> <secao>` (tabela no fim). **Nunca** `cat`/Read de
  um arquivo inteiro de `references/`, nunca varios de uma vez, nunca "para ja ter". Esta SKILL.md ja esta no
  contexto — nao a releia (so se o passo 0 disser que atualizou).
- **Base Solvace antes do codigo**: projeto do `contexto` → `kb.sh show <projeto> <secao>` → so os arquivos que ela
  aponta. Outro modulo: `kb.sh index <termos>` (filtrado). Nada de grep no repositorio inteiro; varredura ampla
  inevitavel → subagente `Explore` (volta so o resumo).
- **Saidas curtas**: `head`/`grep -m`/`sed -n` com limite; SQL/logs longos → `$CARD_DIR/dados/` e leia so o trecho.
- **Menos turnos**: agrupe comandos independentes numa chamada (`>/dev/null` no que so confirma), troque de etapa
  com `advance`, `log` curto; nada de "vou fazer X" sem fazer.

## Comandos do plano no dia a dia
```bash
bash $PLAN advance <card> <key> <proxima|-> "achado/resumo curto" [finding]   # conclui a etapa e inicia a proxima
bash $PLAN step <card> <key> running|completed|cancelled ["motivo"]
bash $PLAN log <card> <key> progress|finding|decision|warning "texto curto"
bash $PLAN steps <card> - <<< '[{"key":"...","title":"...","description":"..."}]'   ·   sync <card> <key>   ·   checkpoint <card> <key> "onde parei"
bash $PLAN control <card>             # entre etapas: 0 segue · 10 pausado (wait em 2o plano) · 11 pare
bash $PLAN watch <card>               # vigia, SEMPRE em segundo plano antes de encerrar a vez com algo pendente
bash $PLAN block <card> <key> "o que o usuario faz"   # travada por permissao/VPN/credencial
bash $PLAN ask <card> - <<< '[...]'   ·   wait-answers <card> 3600 (2o plano)   ·   answer <card> <n> "..."
bash $PLAN status <card> completed "" <resumo.md>
```

## Regras que valem sempre
- **Plano de execucao obrigatorio**: tudo o que voce produz vai para o plano em pedacos; `control` entre etapas;
  nunca encerre a vez com algo pendente de fora sem o vigia (`watch` **em segundo plano**). O PRMake escreve sozinho
  os marcos na Timeline — nao duplique.
- **Pendencia do usuario sempre evidente no PRMake**: etapa sua travada por algo que so o usuario resolve (o Claude
  Code barrou o comando — permissao/auto mode —, VPN desligada, credencial ausente, acesso negado) → **na hora**
  `bash $PLAN block <card> <key> "<o que ele precisa fazer: o que liberar, o comando exato e a alternativa>"`, diga o
  mesmo no chat e rode o vigia. **Nunca deixe a etapa `running` parada.** O vigia avisa `PENDENCIA RESOLVIDA` quando
  ele clica *Ja resolvi* (ou ele responde no chat) → `step <key> running` e tente de novo. Autorizar no PRMake **nao**
  libera comando no Claude Code: quem libera e a regra de permissao (`prmake-skills.sh permissions`).
- **Card que depende de dados nao fecha sem o banco**: o `contexto` mostra se ha credencial de banco nesta maquina;
  teste o acesso cedo (`sql-query.sh --ping`, passo 3). Sem acesso, diga **claramente** no chat que esta sem acesso ao
  banco do cliente e o que falta (permissao do Claude Code, VPN ou credencial); credencial o usuario cadastra **no
  terminal dele** com `prmake-skills.sh db-credentials` — **nunca peca nem aceite senha no chat ou no PRMake**; `block`
  na `consultar-ambiente` e espere — nao conclua a analise, nao proponha solucoes como fato e nao
  empurre a verificacao para o plano de correcao. So siga sem o banco se o usuario responder explicitamente que e
  para seguir assim (`ask`), e diga isso na analise. Detalhes: `$REF consultas 3c`.
- **Orientacao ao cliente = resumo PT/EN do fechamento**: o resumo nao tecnico publicado na discussion ja e a
  orientacao (com o passo a passo). Nao crie etapa `orientar-cliente` nem `validar-cliente` depois do fechamento — o
  plano conclui no `fechar-card`. Detalhes: `$REF correcao 7`.
- **Comentarios e anexos do usuario sao entrada da analise** (mesmo peso dos repro steps). Referencia a anexo
  ("imagem 2", "#12", "print.png") → `bash $PLAN attachment <card> "<ref>"` e abra com Read; comentario →
  `bash $PLAN notes <card> <n>`. Anexos do PRMake ficam so em `$CARD_DIR/anexos-prmake/` (nunca copie para
  `imagens/`/`anexos/` — o `sync` duplicaria). Nunca diga que nao consegue ver um anexo sem tentar.
- **Regra de negocio: consulte o Knowledge Center antes de perguntar** ao usuario ou concluir o comportamento
  "esperado" (`bash $KC search ...` / `article <n>`); cite o **ART-n** na analise, no RCA e no handover. Sem artigo
  sobre a regra: diga isso (lacuna) — nao invente.
- **Quem grava e o PRMake — sempre** (root cause, resumo, classificacao, estimativa, mover o card, PRs, Timeline):
  voce gera os textos; nada direto no Azure DevOps/GitHub. Falta endpoint → pare e avise.
- **Configuracao vem do PRMake** (`bash $PLAN settings <card>`, `branches`, `devops <card> config`): nunca escreva de
  memoria estados, areas, branches, titulos. Faltou regra → pergunte e sugira configurar no PRMake.
- **Ready for QA so com autorizacao**: com a correcao em QA, mova para Dev Test in QA (`devops <card> dev-test-in-qa`,
  sem perguntar); `devops <card> ready-for-qa` so quando o usuario concluir a etapa `validar-qa` no plano ou
  autorizar explicitamente depois dela (a resposta do passo 6 nao vale). Detalhes: `$REF catalogo "ready for qa"`.
- **Foco no card**: solucoes/scripts so para o caso relatado; outros afetados → so um aviso curto.
- **Analise e somente leitura**; codigo muda so depois da resposta do usuario, **sem comentarios novos no codigo**
  (o porque vai no commit/PR/plano). **Nunca merge nem aprovacao de PR.** Nunca escrita em banco/Cognito.
- PII: o que vai para o plano/Timeline e visivel no PRMake — so o necessario.
- `PLANO INDISPONIVEL` no `start`/`contexto`: siga normalmente (os comandos do plano viram no-op).

## Fluxo
**0. Atualizar a skill** — `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet analisar-bug 2>/dev/null || true`;
se imprimir "Skills do PRMake atualizadas", releia esta SKILL.md.

**1. Card** — branch `hotfix/<n>`/`bugfix/<n>` ou o numero informado; sem nenhum dos dois, pergunte.

**2. Contexto (1 comando)** — `bash $PLAN contexto <card>`. Ele cria/retoma o plano (se ja existe plano aberto,
**continue da primeira etapa pronta com o checkpoint**; analise concluida sem correcao → siga do passo 6, sem
refazer), mostra os campos do card e os repro steps (inteiros em `$CARD_DIR/dados/`), lista comentarios/anexos novos
(abra cada anexo novo com Read), sincroniza o KC e a Base Solvace e mostra os projetos/artigos ligados ao card.
Conclua `identificar-card`/`coletar-dados` (um `advance`) e **refine as etapas** (`steps`) para este caso concreto.

**3. Investigar** — comece pela secao da Base Solvace do modulo que o `contexto` mostrou (`bash $KB show <projeto>
<secao>`; nenhum casou → `bash $KB index <modulo/tela>`), depois o codigo apontado; regra de negocio → KC. Reconstrua
o fluxo, levante hipoteses priorizadas com `caminho:linha`, marque o que e hipotese. Diga em qual mundo/repo esta o
codigo (legado `edv-solvace` ou `revamp-<modulo>`). Dados, Cognito, localizar codigo fora da base:
`$REF consultas 3a|3b|3c` (`consultar-ambiente`; cancele com motivo so se o caso **nao** depender de dados). Card que envolve dados
(usuario, cadastro, status, configuracao por tenant): **antes de investigar a fundo**,
`bash ~/.claude/skills/analisar-bug/scripts/sql-query.sh --host <h> -d <db> --ping` — falhou → `block` com o que fazer
(ligar a VPN, liberar a permissao, credencial). Reconheca o padrao do caso no catalogo: `$REF catalogo catalogo`. Se o
codigo divergir da Base Solvace, registre um `log warning` e proponha a correcao da secao:
`bash ~/.claude/skills/base-solvace/scripts/arch.sh suggest <projeto> <secao> divergencia.md --kind divergence --card <card>`.

**4–5. Analise e Timeline** — monte `$CARD_DIR/analises/analise-inicial.md` pelo modelo de `$REF analise 4`
(publicacao: `$REF analise 5`; inclua "Regras de negocio (Knowledge Center)" com os ART-n usados), `sync` e publique
com `prmake-timeline`. O plano de analise segue para `propor-solucoes`.

**6–8b. Solucoes, plano de correcao e execucao** — uma secao por passo: `$REF correcao 6`, depois `7`, `8`, `8b`
(perguntas com opcoes cujo `label` e a propria opcao, espera nas duas pontas, `correction`, fluxo de branches pelo
`branches`, PRs com `pr-text`/`save-pr-text`/`open-pr`, fechamento pelo `devops`, nova rodada no mesmo plano).

**9. Aprender e reportar** — se o caso ensinou algo que nao esta na Base Solvace (regra, armadilha, fluxo, tabela,
query util), proponha em poucas linhas: `bash ~/.claude/skills/base-solvace/scripts/arch.sh suggest <projeto>
<secao> aprendizado.md --kind learning --card <card>` (vai para a fila do admin; nunca grava direto). Reporte: card,
causa raiz, solucao, PRs (links), o que ficou com o usuario e o que falta; tudo esta nos planos do card no PRMake. O custo da sessao vai sozinho ao mudar o status do plano (`bash $PLAN usage` mostra).

## Retomar um card
Sessao do Claude Code fica registrada no plano. Para voltar exatamente a esta conversa depois (outro card no
meio, sessao fechada): `bash ~/.claude/skills/analisar-bug/scripts/prmake-card.sh <card>` (o botao "Retomar no
Claude" do PRMake copia esse comando). Ao ser retomado, rode `resume-info` e `notes` (o que mudou na tela enquanto
estava parado) e siga de onde parou. Vigia opcional que retoma sozinho quando as respostas chegam pela tela:
`prmake-card.sh agent install`.

## Referencias — `bash $REF <arquivo> <secao>` (so a secao, so na fase)
| Quando | Comando |
|---|---|
| passo 3: localizar codigo, Cognito, banco | `$REF consultas 3a` · `3b` · `3c` |
| passo 3/6/7: padrao do caso (A–H) | `$REF catalogo catalogo` |
| passos 4–5 | `$REF analise 4` · `$REF analise 5` |
| passos 6–8b | `$REF correcao 6` · `7` · `8` · `8b` |
| fechamento | `$REF catalogo fechamento` · `"ready for qa"` · `mover` · `"quem grava"` |
| detalhe do plano | `$REF plano comandos` · `retomar` · `control` · `block` · `watch` · `anexos` |
| nao sabe a secao | `$REF` (lista arquivos e secoes com ~tokens) |
