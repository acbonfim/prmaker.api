# 0075 — Análise legível (código com fotografia, diff anotado e mocks) + resumo do estado intermediário

## Pedido (do usuário, 2026-11-04)

**1. A análise e a proposta de solução ainda não são claras quando falam de código.**
1. Formatar melhor os textos e as explicações de código — ex.: *O que vai ser feito*, *Onde parou*.
2. Toda citação de código vira **hiperlink** que abre um **popover** com a **fotografia do código atual** (o trecho real
   que vai ser alterado, removido ou criado), **parecido com um diff do git**, com **textos explicando** o motivo de cada
   trecho — por que usar aquele código, por que faz sentido.
3. A análise deixa claro para o usuário **tudo** o que será feito, de forma clara e objetiva.
4. **Todos** os lugares que mostram código permitem abrir o popover pelo hiperlink, com o arquivo e a parte
   problemática/afetada **destacada**.
5. Mudança **visual** (principalmente front): gerar **imagens** do resultado esperado e anexar na análise — o Claude tem
   o código do front e consegue simular a tela de verdade.
6. Exemplo de trecho que deveria ter os hiperlinks:
   ```
   Pontos suspeitos / de alteração
   solvace-asp/systems/sa3/sa3_busca_a3.asp:239-289 — ordem da inicialização.
   solvace-core/view_shared/wwwroot/js/loadSelectOptions.class.js:336-353 — restauração de data sem formato (compartilhado).
   solvace-core/view_shared/wwwroot/templates/limitlessfull/js/plugins/pickers/daterangepicker.js:325 — construtor sem isValid() (biblioteca, 36 páginas a carregam).
   ```

**2. Resumo para o tratamento intermediário.** Sempre que o card estiver **esperando algo**, mostrar um resumo desse estado:
1. **Chamado** — existe chamado aberto e estamos aguardando o retorno.
2. **Aguardando aprovação de PR e merge** — resumo do estado atual (PRs e DevOps).
3. **PRs mesclados, pendente validar em QA** — resumo do estado atual.
4. Funciona como um **handover**, com as informações necessárias para outra pessoa assumir.
5. Pendente validar em QA (merge na branch de QA feito) → **mover o card para Dev Test in QA**; quando o usuário concluir
   essa fase, aí sim move para **Ready for QA**.

---

## O que existe hoje (master 94a08a4 / front b08550d)

- **Onde a análise aparece**: a skill `analisar-bug` monta `analises/analise-inicial.md` pelo modelo
  `skills/analisar-bug/references/analise-template.md` e publica na **Timeline** (`prmake-timeline`); as soluções vão em
  `analises/solucoes.md` (arquivo do plano) + perguntas; o resumo para a correção vai no checkpoint de `propor-solucoes`.
  O código é citado como texto `caminho:linha` — sem fotografia, sem diff, sem explicação por trecho.
- **Front**: tudo é `marked.parse` puro (`plan-markdown.pipe.ts`, `timeline-markdown.pipe.ts`), sem renderer/extensão,
  sem highlight de sintaxe, sem esquema de link interno; só a Timeline intercepta clique em link (abre nova aba). Existem
  `cc-popover` (CDK, exige `CdkOverlayOrigin` no template), `p-popover` (PrimeNG, aceita alvo por evento),
  `git-diff-viewer` (diff estilo GitHub a partir de `patch`, sem anotações) e a prévia de arquivos do plano
  (`plan-files-dialog`: imagem por blob autenticado, código numerado, highlight SQL caseiro). Imagem dentro do markdown não
  funciona com URL da API (a autenticação vai em header).
- **Back**: arquivos do plano em `ExecutionArtifacts` (+ conteúdo `bytea`, 10 MB/arquivo, 50 MB/plano, tipo `image` já
  existe); `prmake_file` (MCP) só grava texto; `prmake_attachment` só acha anexos de **comentários**.
- **Espera**: não há "resumo do estado". Os sinais estão espalhados — etapa `waiting` com `waitingOn`
  (`user|answer|external`), links de chamado (`open|resolved|closed`, marcado à mão), links de PR
  (`open|merged|closed` + CI), estado/coluna do card no DevOps (cartão do topo) e marcos na Timeline ("⏳ Etapa aguardando",
  "**Falta:** …"). O handover (`Handovers`, gerado por IA pelo `TemplatePassagemConhecimento`) não conversa com o plano.
- **Dev Test in QA**: a ação existe (`POST Azure/card/{id}/actions/dev-test-in-qa`, `prmake_devops`, estado/coluna
  semeados por migração) e o `ready-for-qa` exige `validar-qa` concluída. **Mas quem move é a skill**, e só se a sessão
  estiver viva quando o PR for mesclado; o back não reconhece "PR para a branch de QA mesclado". A sincronização dos PRs
  (`SyncPullRequestsAsync`) roda quando alguém abre o plano ou a skill chama `control`. Revisões/aprovações dos PRs não são
  lidas do GitHub.

---

## Parte A — Análise legível

### A1. Formato do texto (1.1, 1.3)
Novo modelo da análise e das soluções, **com a mesma ordem sempre** — quem lê acha cada coisa no mesmo lugar:

```markdown
**Análise — Card 75812: Filtro de data some ao voltar da busca (SA3)**

> [!SUMMARY]
> **Problema:** ao voltar da busca, o filtro de data aparece vazio.
> **Causa (confirmada):** a data salva é restaurada antes de o seletor carregar o formato.
> **O que vamos fazer:** inicializar o seletor antes de restaurar o filtro (1 arquivo, legado).

### O que vai ser feito
| # | Onde | Mudança | Por quê |
|---|---|---|---|
| 1 | [`sa3_busca_a3.asp:239-289`](ref:sa3-init) | ✏️ alterar | inicializa o daterangepicker antes do `restoreFilters()` |
| 2 | [`loadSelectOptions.class.js:336-353`](ref:restore-date) | 👁️ só leitura | compartilhado por 36 telas — não mexemos; explica o sintoma |

### Onde parou
✅ Análise publicada · ⏳ aguardando sua escolha da solução (pergunta 1) · próximo: plano de correção.

### Evidências
…consultas (arquivos do plano), prints, itens da engenharia reversa (RN/UC)…

### Causa raiz
- ✅ **Confirmado:** …  · ❓ **Hipótese:** …

### Pontos suspeitos / de alteração
- [`solvace-asp/systems/sa3/sa3_busca_a3.asp:239-289`](ref:sa3-init) — ordem da inicialização.
- …

### Impacto e riscos · Antes × Depois (mock) · Próximos passos
```

- **Callouts** no estilo GitHub (`> [!SUMMARY]`, `[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]`)
  viram caixas coloridas no PRMake.
- **Confirmado × hipótese** sempre marcados (✅/❓). Frases curtas, sem jargão desnecessário.
- **Seções fixas** com títulos `###` — o PRMake usa os títulos para o sumário do documento e a skill usa os mesmos
  títulos no `solucoes.md` (por opção: *O que muda*, *Onde* (com refs), *Impacto na base*, *Riscos*, *Esforço*,
  *Antes × Depois*).
- **"Onde parou"** sai do estado real do plano (o que já foi feito, o que está aguardando e o próximo passo) — a mesma
  informação do resumo da Parte B.

### A2. Fotografia do código (1.2, 1.4) — *code ref*
Cada trecho de código citado vira uma **referência de código** gravada no plano:

| Campo | O que é |
|---|---|
| `ref` | id curto e estável no card (`sa3-init`), usado no link `ref:sa3-init` |
| `repository`, `path`, `commitSha`, `branch` | onde o código está e **de qual commit** a fotografia foi tirada (a master lida na análise, 0067) |
| `startLine`–`endLine` | o trecho em foco (o destacado) |
| `change` | `change` (alterar) · `create` (criar) · `remove` (remover) · `suspect` (suspeito) · `read` (só leitura/contexto) |
| `title`, `why` | o que é o trecho e **por que** ele importa (1–3 frases) |
| `snapshot` | o código real: o foco ± N linhas de contexto (`firstLine` = número da 1ª linha) |
| `proposal` | a mudança proposta em **diff unificado** (vazio em `read`/`suspect`); em `create`, o arquivo novo |
| `annotations` | notas por faixa de linhas, do lado `old` (atual) ou `new` (proposto): *por que este código*, *por que faz sentido*, *o que acontece aqui* |
| `implemented` | (correção) o diff **real** do commit, quando a mudança foi feita — compara proposta × feita |

**Quem tira a fotografia é a skill, localmente** (o servidor não tem os repositórios): `prmake-plan.sh coderef` lê o
arquivo na cópia da master, recorta o foco + contexto, calcula o diff da proposta (o Claude escreve só o **trecho novo**;
o script faz o `diff`), detecta a linguagem e envia. O Claude **não cola o código** na conversa (economia de tokens).
Anotações e proposta também podem ser ajustadas pelo MCP (`prmake_code_ref`).

**No texto**, o link explícito é `[texto](ref:<id>)`. Para pegar também textos antigos e citações escritas sem link
(o exemplo do item 1.6), o PRMake **reconhece automaticamente** `caminho:linha` / `caminho:ini-fim` (em código inline ou
texto) e liga à ref do card cujo arquivo termina com aquele caminho e cujas linhas se sobrepõem. Citação sem fotografia
continua clicável: o popover mostra "sem fotografia" com o caminho para copiar e, se der, o link do arquivo no GitHub.

**Popover** (abre no clique; Esc/fora fecha; no celular vira folha inferior; botão "expandir" abre em diálogo grande):
- **Cabeçalho**: selo da mudança (✏️ Alterar · ➕ Criar · 🗑️ Remover · ⚠️ Suspeito · 👁️ Leitura), repositório, caminho,
  linhas, commit curto (copiar), "Abrir no GitHub" (no commit da fotografia), copiar caminho:linha.
- **Por quê**: o `why` em destaque, 1–3 frases.
- **Abas**: **Proposta** (diff estilo git: removidas em vermelho, adicionadas em verde, contexto apagado) ·
  **Código atual** (a fotografia com o foco destacado, o resto esmaecido) · **Feito** (só com `implemented`).
- **Anotações**: marcadores numerados na calha das linhas (①②③) e a lista de notas ao lado (abaixo, no celular);
  passar o mouse numa nota destaca as linhas dela e vice-versa.
- **Highlight de sintaxe** (C#, TS/JS, HTML/ASP, SQL, CSS/SCSS, JSON, Java, Python, shell), carregado sob demanda.
- Leve: lista de refs vem só com cabeçalhos; o conteúdo de uma ref é buscado no primeiro clique e fica em cache.

**Onde vale** (1.4 — todos os lugares que mostram código): Timeline do card, resumo/descrições/checkpoints/andamento/
perguntas do plano, comentários, prévia de `.md` nos arquivos do plano, resumo do estado (Parte B). Na visão **pública**
do handover não há popover (link vira código simples).

**Regra de qualidade** (o PRMake confere): ao concluir `montar-analise` e `propor-solucoes`, toda citação
`caminho:linha` do texto precisa ter fotografia; faltando, o `advance` é recusado com a lista (mesmo padrão da 0063).
Ref com `change` ≠ `read`/`suspect` precisa de `why`; `change`/`create`/`remove` precisam de `proposal`.

### A3. Mocks visuais — Antes × Depois (1.5)
Quando a correção muda algo **visível** (tela, layout, texto, relatório, e-mail), a análise traz imagens:
- **Antes** — o problema reproduzido; **Depois** — o resultado esperado com a solução recomendada (uma imagem por opção
  quando as opções mudam a tela de jeitos diferentes).
- A skill monta um **HTML de simulação a partir do código real da tela** (markup, CSS do tema — ex.: limitless no
  legado, `styles.scss`/componentes no revamp —, textos/traduções reais, dados fictícios sem PII) e tira o print com o
  **Chrome headless** (`mock-shot.sh`: acha Chrome/Chromium/Edge no macOS, Linux e Windows/Git Bash; largura de desktop e,
  se for o caso, de celular). Destaque da área alterada (contorno/seta) no "Depois".
- Os PNGs vão para os **arquivos do plano** (tipo `image`, fase certa) junto do HTML-fonte, e entram no texto como
  `![Depois — filtro de data mantido](anexo:mock-depois.png)`. No PRMake a imagem carrega autenticada, abre com zoom no
  clique, e duas imagens na mesma linha ficam **lado a lado** (Antes × Depois).
- Sempre com a legenda **"Simulação a partir do código — não é print do sistema"**. Sem fidelidade possível (ex.: tela
  depende de componente que não roda isolado), um wireframe anotado — dito explicitamente.
- `prmake_attachment` passa a achar também os arquivos do plano (não só anexos de comentário).

---

## Parte B — Resumo do estado intermediário (Situação do card)

### B1. O que é
Um bloco **"Situação atual"** que diz, em segundos, **em que pé o card está, o que está sendo aguardado, desde quando, de
quem, e o que acontece em seguida** — completo o bastante para outra pessoa assumir (handover). Tipos:

| Tipo | Quando | O que mostra |
|---|---|---|
| 🎫 **Aguardando chamado** | etapa `ticket` com chamado aberto | chamado (link, sistema, aberto em/por, há quanto tempo), o que o script faz, arquivos anexados (`01_x.sql`, `chamado-x.md`), quem acompanha, "quando resolver: marque *Resolvido* → segue para <próxima etapa>" |
| 🔀 **Aguardando aprovação e merge** | etapa `pr` com PR aberto | por PR: repositório, `#n`, `branch → destino`, rascunho, **aprovações / alterações pedidas / revisores pendentes**, CI (✅/⏳/❌, "já falhava na base"), há quanto tempo aberto; **estado e coluna do card no DevOps**; o que falta para seguir |
| 🧪 **Validar em QA** | PRs da branch de QA mesclados e `validar-qa` pendente | o que foi publicado em QA (PRs/commits), **card em Dev Test in QA** (desde quando), **roteiro de validação** (do `impacto-na-base`: telas/fluxos a testar), ambiente QA, "ao concluir *Validar em QA* → movemos para Ready for QA" |
| ❓ **Aguardando resposta** / ⚠️ **Aguardando você** | perguntas abertas / etapa travada pelo usuário | as perguntas/o que fazer (já existe no plano — entra no resumo para ficar completo) |

Cada resumo tem duas partes:
1. **Fatos** — montados pelo PRMake a partir do plano, do GitHub e do DevOps, **sem IA** (custo zero, sempre atuais).
2. **Contexto da passagem** (handoff) — texto curto que a skill grava ao entrar em espera: o que foi feito, o que se
   espera, riscos, como retomar ("se o chamado voltar recusado, ver `01_x.sql` linha …").

### B2. Onde aparece
- **Plano do card**: cartão "Situação atual" no topo (acima das etapas), com o tipo, "há 2 d", os fatos, o handoff,
  o próximo passo e **"Copiar resumo"** (markdown/texto) — some quando nada está sendo aguardado.
- **Cartão do card (topo da tela)**: fato "Situação: 🔀 aguardando merge há 2 d".
- **Home**: chip no card ("🎫 chamado há 3 d", "🧪 validar em QA").
- **Timeline**: uma entrada quando a situação **muda de tipo** (entrou em espera / mudou de espera / saiu), no lugar do
  "⏳ Etapa aguardando" de hoje — sem repetir a cada sincronização.
- **Handover**: a passagem de conhecimento (skill `gerar-handover` e o diálogo) inclui a seção "Situação atual" com o
  resumo do momento.
- **DevOps**: o resumo **mostra** o estado/coluna do card no DevOps. Publicar o resumo na discussion do DevOps é
  **opcional e manual** (botão "Publicar no DevOps", pelo PRMake) — ver decisão D1.

### B3. Dev Test in QA → Ready for QA (2.5) — sem depender da sessão do Claude
- O PRMake reconhece o **PR para a branch de QA** pelo `BranchStrategy` (o PR `-qa → qa` do fluxo; padrão configurável
  `QaBranchPattern`, semeado como `qa`).
- Quando esse PR é **mesclado** e o plano de correção tem `validar-qa` pendente, o **PRMake move o card para Dev Test in
  QA** (ação existente, com a integração do Azure do dono do plano), uma vez só (idempotente), registra na Timeline, deixa
  `validar-qa` com o usuário e a situação vira 🧪. Config `AutoDevTestInQa` (padrão ligado). Falha ao mover (409/permissão)
  → aviso no plano e na situação, sem travar.
- **Gatilhos** da sincronização: abrir o plano, `control`/`watch` da skill, o resumo da Home e o *heartbeat* do executor
  (varredura leve dos planos aguardando PR, no máximo a cada 5 min) — o Cloud Run não garante CPU fora de requisição.
- **Ready for QA**: ao concluir `validar-qa` na tela, o diálogo de conclusão traz **"Mover para Ready for QA agora"**
  (marcado) — o PRMake move na hora; a skill não precisa estar aberta. Desmarcado → fica como hoje (o `fechar-card` move
  depois, com a regra existente).

---

## Fora de escopo
- Ler status de chamado direto do Freshservice (continua marcado à mão no PRMake).
- Rodar o front de verdade (com back/auth) para tirar print real — o mock é simulação a partir do código.
- Editar a fotografia/anotações pela tela (só leitura no PRMake; quem grava é a skill).
- IA do servidor para montar o resumo (regra: IA do servidor cobra a chave do usuário).

## Decisões a confirmar
- **D1** — Item 2.2 "adicionar resumo do estado atual no devops": a spec assume **mostrar** o estado do DevOps no resumo
  e oferecer "Publicar no DevOps" **manual** (a discussion é lida pelo cliente). Se a intenção for publicar
  automaticamente a cada mudança de situação, vira uma opção de config (`SituationPostToDevOps`).
- **D2** — Tamanho da fotografia: foco ± 30 linhas (máx. 600 linhas/64 KB por ref, 80 refs por plano), configurável em
  Skills Configurations (`CodeRefContextLines`, `CodeRefMaxLines`).

## Critérios de aceite
1. Uma análise nova de card com código: todas as citações `caminho:linha` (texto da Timeline, `solucoes.md`, checkpoint,
   perguntas) abrem o popover com a fotografia do commit lido, o foco destacado, o diff da proposta e as anotações.
2. O exemplo do item 1.6, colado como está num texto do card que tem as refs, fica com os três links funcionando.
3. Card com mudança visual: análise com Antes × Depois (PNG nos arquivos do plano, lado a lado no texto, legenda de
   simulação).
4. Card com chamado aberto / PR aguardando merge / PR de QA mesclado: o cartão "Situação atual" aparece com os fatos
   certos, o handoff da skill, "há X", próximo passo e copiar; a Timeline recebe uma entrada por mudança de situação.
5. PR para `qa` mesclado sem sessão do Claude aberta: ao abrir o card (ou no heartbeat do executor) o card vai para Dev
   Test in QA uma única vez; concluir `validar-qa` com "Mover para Ready for QA" move o card.
6. Sem regressão: textos antigos renderizam igual (só ganham links onde há ref), visão pública do handover sem popover,
   memória/tempo das telas dentro do medido na 0070 (refs carregadas sob demanda).
