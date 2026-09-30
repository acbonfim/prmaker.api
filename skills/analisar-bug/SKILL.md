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
PLAN=~/.claude/skills/analisar-bug/scripts/prmake-plan.sh    # plano de execucao (todos os comandos: references/plano-execucao.md)
KB=~/.claude/skills/base-solvace/scripts/kb.sh               # Base Solvace: index | show <projeto> [secao] | find <termo>
KC=~/.claude/skills/base-solvace/scripts/kc.sh               # Knowledge Center: search <termos> | article <n>
```

## Economia de tokens (vale para todo o fluxo)
- **Um comando traz o contexto inicial**: `bash $PLAN contexto <card>` (pasta, plano criado/retomado, card, repro
  steps, comentarios/anexos novos, trechos da Base Solvace e artigos do KC relacionados). Nao repita essas buscas.
- **Base Solvace antes do codigo**: indice → secao do modulo → so os arquivos que ela aponta. Nada de grep no
  repositorio inteiro; varredura ampla inevitavel → subagente `Explore` (volta so o resumo).
- **Leia as referencias so quando chegar na fase** (tabela no fim). Nao releia arquivos ja lidos; saidas longas
  (SQL, logs) → salve em `$CARD_DIR/dados/` e leia so o trecho que importa.
- Menos turnos: agrupe comandos independentes numa chamada; `log` curto; nada de "vou fazer X" sem fazer.

## Regras que valem sempre
- **Plano de execucao obrigatorio**: tudo o que voce produz vai para o plano em pedacos (`step`, `log`, `sync`,
  `checkpoint`); `control` entre etapas (exit 10 pausado → `wait` em segundo plano e encerre a vez; 11 → pare).
  Nunca encerre a vez com algo pendente de fora sem o vigia: `bash $PLAN watch <card>` **em segundo plano**. O
  PRMake escreve sozinho os marcos na Timeline — nao duplique. Detalhes: `references/plano-execucao.md`.
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
refazer), grava `description.txt`/`card.json` em `$CARD_DIR/dados/`, lista comentarios/anexos novos (abra cada
anexo novo com Read), sincroniza o KC e a Base Solvace e mostra os trechos relacionados. Conclua
`identificar-card`/`coletar-dados` e **refine as etapas** (`steps`) para este caso concreto.

**3. Investigar** — comece pela secao da Base Solvace do modulo (`bash $KB show <projeto> <secao>`), depois o codigo
apontado; regra de negocio → KC. Reconstrua o fluxo, levante hipoteses priorizadas com `caminho:linha`, marque o
que e hipotese. Diga em qual mundo/repo esta o codigo (legado `edv-solvace` ou `revamp-<modulo>`). Dados, Cognito,
localizar codigo fora da base: `references/consultas.md` (`consultar-ambiente`, cancele com motivo se nao
precisar). Reconheca o padrao do caso no catalogo: `references/catalogo-e-fechamento.md`. Se o codigo divergir da
Base Solvace, registre um `log warning` e proponha a correcao da secao:
`bash ~/.claude/skills/base-solvace/scripts/arch.sh suggest <projeto> <secao> divergencia.md --kind divergence --card <card>`.

**4–5. Analise e Timeline** — monte `$CARD_DIR/analises/analise-inicial.md` pelo modelo de
`references/analise-template.md` (inclua "Regras de negocio (Knowledge Center)" com os ART-n usados), `sync` e
publique com `prmake-timeline`. O plano de analise segue para `propor-solucoes`.

**6–8b. Solucoes, plano de correcao e execucao** — `references/correcao.md` (perguntas com opcoes cujo `label` e
a propria opcao, espera nas duas pontas, `correction`, fluxo de branches pelo `branches`, PRs com `pr-text`/
`save-pr-text`/`open-pr`, fechamento pelo `devops`, nova rodada no mesmo plano).

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

## Referencias (leia so na fase)
| Arquivo | Quando |
|---|---|
| `references/plano-execucao.md` | detalhe de qualquer comando/regra do plano (vigia, pausa, anexos, fila offline) |
| `references/consultas.md` | localizar codigo legado × revamp, Cognito, SQL Server somente leitura |
| `references/catalogo-e-fechamento.md` | padrao do caso (A–H), configuracao do PRMake, fechamento e mover o card |
| `references/analise-template.md` | passos 4–5 |
| `references/correcao.md` | passos 6–8b |
