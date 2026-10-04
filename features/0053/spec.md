# Feature 0053 — Engenharia reversa do banco (DEMO) e glossário por módulo

> **Status: implementada em 2026-10-04** (ver `status.md`). Pendente: leitura real da DEMO (a VPN estava desligada
> durante a execução — o catálogo foi testado com um `sql-query.sh` falso).

## Contexto
A 0052 (engenharia reversa por módulo) cobre bem o código (endpoints, validações, telas, tabelas citadas), mas no
legado boa parte da regra de negócio vive **no banco**: views, procedures, functions, triggers e jobs do SQL Agent.
Hoje:
- o inventário só registra o **nome** das procedures que o código chama — o corpo (onde está a regra) não entra;
- views, functions, triggers e jobs não entram de jeito nenhum;
- não há tipo de item próprio para esses objetos (ficaria espremido em `DB`/`RN`);
- o glossário só existe na Especificação de visão (`GLO-…`) e não ajuda a busca: "RCA" não acha um item que só diz
  "A3", e os termos da tela (traduções, menus) não são extraídos.

## Pedido (usuário, 2026-10-04)
1. A engenharia reversa deve levantar o que acontece **no banco**: views, procedures, functions, triggers e jobs — e
   as regras que estão só neles.
2. **Fonte da verdade = o banco da DEMO** (global e local), lido direto. **Não** usar os scripts/migrações versionados
   no legado (`solvace-asp/#database/…` — parados desde 03/2023 e incompletos: sem triggers, sem jobs).
   A DEMO é produção como os outros clientes, mas é o ambiente interno de apresentação para novos clientes — sempre
   reflete o que está em produção hoje.
3. Glossário por módulo com os textos e termos mais usados (ex.: RCA = SA3 = A3 = "RCA 1-pager"), útil para pessoas
   e para a busca das análises.

## Solução

### 1. Catálogo do banco da DEMO (somente leitura)
- **Onde**: alias `prod` do `~/.claude/sqlserver-credentials.json` (confirmado em 2026-10-04): global
  `DB_DEMO_PRD_GLOBAL` e os locais `DB_DEMO_PRD_LOCAL_CTB`, `DB_DEMO_PRD_LOCAL_GLB` e `DB_DEMO_PRD_LOCAL_PAR`. Vem da
  configuração do PRMake (plugin "Skills Configurations", chave nova `ReverseEngineeringReferenceDatabase`:
  `{"environment": "DEMO", "host": "prod", "global": "DB_DEMO_PRD_GLOBAL", "locals": ["DB_DEMO_PRD_LOCAL_CTB",
  "DB_DEMO_PRD_LOCAL_GLB", "DB_DEMO_PRD_LOCAL_PAR"]}`) — nada fixo na skill.
- **Global primeiro, locais junto**: legado **e revamp** usam os mesmos bancos; o foco é o **global** (o local está
  sendo desativado aos poucos, mas ainda há módulos/casos que usam). O catálogo lê o global e **todos os locais** da
  DEMO: objeto igual nos locais vira um item só (banco "local"); definição diferente entre locais → item com a
  diferença registrada e `GAP` (planta divergente). Cada item diz se está no global, no local ou nos dois — e a
  análise de um card sabe em qual banco olhar.
- Primeira leitura (2026-10-04): `DB_DEMO_PRD_GLOBAL` tem 442 tabelas, 18 views, 4 triggers e **nenhuma procedure/
  function** — as procedures estão nos locais (o catálogo não pode ignorar o local).
- **Como**: pelo `sql-query.sh` existente (validação read-only + transação com ROLLBACK), da máquina de quem roda a
  skill. Sem credencial/VPN → a etapa fica bloqueada no andamento da sessão com o que fazer (nunca segue "sem banco"
  calado) e o documento registra `GAP`.
- **Escopo do módulo** (o que extrair):
  - tabelas do módulo: prefixo `TB_<SIGLA>_*` (sigla pelo glossário de módulos / fontes) + tabelas citadas pelo código
    do módulo (inventário da 0052);
  - objetos que **dependem** dessas tabelas (`sys.sql_expression_dependencies` + `sys.objects`): views, procedures,
    functions (escalar/tabela) e **triggers** (`sys.triggers`, com a tabela-alvo e os eventos INSERT/UPDATE/DELETE);
  - objetos com a sigla no nome (`VW_SA3_*`, `STP_*SA3*`…) e as procedures que o código do módulo chama;
  - **definição** de cada um (`sys.sql_modules.definition` / `OBJECT_DEFINITION`) — o corpo, onde está a regra;
  - tabelas: colunas (tipo, nulo, default), PK/FK, índices únicos, **check constraints**, colunas computadas;
  - **jobs do SQL Agent** (`msdb.dbo.sysjobs`, `sysjobsteps`, `sysjobschedules`, `sysschedules`) cujos passos citam
    os objetos/tabelas do módulo — agenda, passos e comando. A credencial do `prod` lê o `msdb` (confirmado pelo
    usuário). Jobs do servidor atendem vários clientes: só entram os passos que rodam nos bancos da DEMO ou citam
    objetos do módulo.
  - objetos de **outros módulos** que leem/gravam as tabelas deste (impacto entre módulos — vira `INT`/`**Módulos:**`).
- **Saída local**: `~/.prmake/reverse/<módulo>/banco/` — um arquivo por objeto (`views/VW_SA3_A3.sql`,
  `procedures/STP_…sql`, `triggers/…sql`, `jobs/<job>.md`, `tabelas/TB_SA3_A3.md`) + `catalogo.json` (lista com tipo,
  banco global/local, datas `create_date`/`modify_date`, dependências). Nada disso vai para o git do módulo.
- **Inventário**: o `catalogo.json` entra no inventário da 0052 com categorias novas — `view`, `procedure`,
  `function`, `trigger`, `job`, `constraint` — e a cobertura passa a exigir cada objeto no documento.
- **Nunca** dados de cliente: só metadados e definições de objetos (o catálogo não roda `SELECT` em tabelas de dados).
  Definição com valor sensível embutido (senha/chave em procedure) → mascarado antes de gravar.

### 2. Itens e documentos
- Tipos novos de item (contrato do índice, `ReverseItemKinds`): **`SQL`** (view/procedure/function — o título diz o
  tipo) e **`TRG`** (trigger). Agendamentos continuam em **`JOB`** (agora também os do SQL Agent).
- Levantamento de arquitetura: seção obrigatória nova **"Banco de dados: views, procedures, functions, triggers e
  jobs"** — um item por objeto com: banco (global/local), o que faz passo a passo, tabelas lidas/gravadas, quem chama
  (tela/API/job/trigger), regras que aplica (→ `RN-…`), efeitos colaterais (o que um trigger faz "escondido").
- Levantamento funcional: **regra que só existe no banco** vira `RN` normal com `**Onde:** banco DEMO <global|local>
  · dbo.STP_X (linha N)` — e a evidência aceita esse formato (parser/lint: `banco:` conta como evidência).
- Especificação de arquitetura: o diagrama de dados e as sequências incluem triggers e jobs.
- Lint: `SQL`/`TRG`/`JOB` exigem evidência; objeto do catálogo sem item → cobertura; ID de objeto que sumiu do banco →
  aviso (o item vira "(removido)").

### 3. Divergência e atualização
- Cada objeto guarda o `modify_date` da DEMO no item (`**Banco:** DEMO local · alterado em 2026-08-12`). No modo
  **melhorar**, a skill compara o catálogo novo com o anterior e lista o que mudou no banco desde a última publicação
  (objetos novos, alterados, removidos) — é a lista de trabalho da sessão.
- A tela da revisão mostra a seção "Banco" da cobertura (objetos cobertos × faltando) como as outras categorias.

### 4. Glossário por módulo (e para a busca)
- **Gerado já no levantamento funcional** (seção obrigatória "Glossário", itens `GLO-…`) — não só na visão; a visão
  referencia os `GLO` do funcional.
- **Fonte dos termos (inventário)**:
  - textos da tela: legado `GetLanguageByName("…")` e a tabela de multilíngua da DEMO (traduções PT/EN/ES dos termos do
    módulo); revamp, os `i18n/*.json` do front do módulo;
  - menus do módulo na DEMO (`TB_WCM_MENU`) — o nome que o usuário vê;
  - siglas e nomes de tabelas/objetos (`TB_SA3_*`, `VW_SA3_*`), nome do módulo no catálogo de módulos;
  - **todos os termos necessários, sem número fixo** (decisão do usuário): o inventário monta a lista de termos do
    módulo — rótulos/títulos/mensagens da tela que nomeiam conceitos do domínio, menus, siglas, nomes de status/tipos
    e de entidades — e a cobertura do glossário exige **cada** um (nome ou sinônimo de algum `GLO`). Palavras genéricas
    de interface ("Salvar", "Cancelar", "Filtrar", "Data") ficam fora por uma lista de exclusão configurável; o que o
    Claude deixar de fora de propósito vai justificado em `GAP`, como na cobertura do código.
- Item `GLO` com linha **`- **Sinônimos:** SA3, A3, RCA, RCA 1-pager, root cause analysis`** (lida pelo índice).
- **Busca com sinônimos**: ao publicar, os sinônimos dos `GLO` viram expansão de consulta do módulo — `prmake_base_search
  ("RCA")` acha itens que só dizem "A3"; também na tela Índice e no "Pergunte" da Base Solvace.
- **Apelidos e palavras-chave sugeridos**: ao publicar, os termos do glossário que não estão nos apelidos do módulo
  (campo Module do card) nem nas palavras-chave do projeto aparecem como sugestão na tela (aceitar/ignorar) — nunca
  gravados sozinhos.
- **Aba "Glossário"** na tela Engenharia reversa: termos de todos os módulos, com sinônimos, módulo (legado × revamp)
  e onde aparecem; busca por termo.

### 5. Correção: clique no sumário não rola até o item (relatado em 2026-10-04, piloto SA3)
Relato: na aba do levantamento funcional, clicar num item do sumário (PRF, FN, UC…) às vezes não levava ao ponto
exato; depois voltou a funcionar sozinho. Investigado com o documento real do `legado-rca` (85 mil caracteres, 175
itens), sem reproduzir a falha na tela Engenharia reversa — mas com duas causas concretas:
- **Engenharia reversa — âncoras aplicadas uma vez só**: os `id="item-RN-012"` são postos por um `setTimeout` depois de
  carregar o documento (`decorate()`); a app é *zoneless*, então em documento grande o clique pode chegar antes, e um
  redesenho do conteúdo sem recarga deixa o documento sem âncoras — o clique não acha o item e nada acontece.
  Correção: aplicar as âncoras **a cada renderização** do conteúdo (`afterRenderEffect`/`MutationObserver` no
  `#docContent`, ou âncoras geradas já no HTML pelo renderizador de markdown) e, no clique, se o item ainda não tiver
  âncora, decorar e tentar de novo; diagramas mermaid renderizados depois do clique não podem deslocar o destino (rolar
  de novo ao terminar a renderização).
- **Base Solvace — títulos com `_`, `*`, `` ` `` ou link nunca rolam**: o sumário compara o texto do título tirando
  essas marcas só de um lado (`cleanInline` × `textContent`) — no SA3, 13 de 186 títulos (ex.: `EST-001 — A3
  (TB_SA3_A3.ID_STATUS)`, `CFG-002 — … RCA_FISHBONE_DISABLED`). Correção: casar pela mesma normalização dos dois
  lados ou por âncora (id) gerada no título, e usar o ID do item quando o título começa com um.
- Nos dois: o título para colado na barra superior (topo 70 px = altura da barra) — `scroll-margin-top` maior e
  destaque rápido no item (como no `?i=` da engenharia reversa).
- Teste: no T1, clicar em todos os itens do sumário do `legado-rca` nas duas telas (inclusive logo após abrir e depois
  de voltar do Índice) e conferir o topo do título visível.

### 6. Modo "melhorar" que fecha o ciclo (pedido do usuário, 2026-10-04)
- **Sugestões resolvidas na publicação**: hoje as sugestões pendentes entram no pacote do `melhorar`, mas continuam
  pendentes depois de publicar (voltam na próxima sessão). Passa a ser:
  - no envio (`re.sh submit … --sugestoes <arquivo>`), a skill informa, para cada sugestão do pacote, `aplicada`
    (com os IDs dos itens que mudaram) ou `recusada` (com o motivo); sem decisão = continua pendente;
  - a revisão mostra essa lista ao revisor (sugestão → itens alterados / motivo da recusa);
  - ao **publicar**, as aplicadas viram `applied` e as recusadas `dismissed`, com a nota e o número da revisão (e o card
    de origem fica sabendo pela Timeline, se a sugestão veio de um card); descartar a revisão não resolve nada.
- **O que mudou desde a última publicação**: a revisão já guarda o commit de cada fonte (`session.sources[].commit`).
  No `melhorar`, a skill compara esse commit com o atual de cada fonte (`git diff --stat <commit>..HEAD -- <pasta>`) e,
  com a §1, o catálogo do banco da DEMO publicado × o atual, e monta a **lista de trabalho da sessão**:
  arquivos/objetos alterados, novos e removidos → itens da ER que os citam (pela evidência `Onde:`) → itens a revisar.
  O andamento ao vivo mostra essa lista ("12 arquivos e 3 procedures mudaram desde a v2 → 9 itens a revisar"); o
  resumo do envio diz o que foi revisto por causa dela. Commit gravado que não existe mais (rebase/clone raso) → avisa
  e cai no inventário completo.
- **Comando com o módulo**: o painel "Como gerar" e a SKILL.md mostram sempre o módulo explícito
  (`/engenharia-reversa <módulo> melhorar <doc>` / `refazer <doc>`) — no legado a pasta do `edv-solvace` serve a dezenas
  de módulos e não identifica o módulo sozinha; o painel lista também as chaves válidas de `<doc>`.

## Fora do escopo
- Usar os scripts `solvace-asp/#database/…` ou migrações versionadas como fonte (decisão do usuário).
- Banco de clientes que não a DEMO como referência (o catálogo é de referência; divergência de um cliente específico
  continua sendo assunto da análise do card).
- Rodar a engenharia reversa pelo executor (continua no Claude aberto no módulo).

## Respostas do usuário (2026-10-04)
1. DEMO: `DB_DEMO_PRD_GLOBAL` + 3 locais (`CTB`, `GLB`, `PAR`) no alias `prod` — o catálogo lê todos (acima).
2. A credencial lê o `msdb` — jobs entram.
3. Revamp usa os mesmos bancos do legado, com foco no global; ainda há casos no local → mesmo catálogo para
   `revamp-*` e `legado-*`, com global primeiro.
4. Glossário: todos os termos necessários, sem número fixo (cobertura de 100% da lista de termos do módulo, com
   exclusões genéricas configuráveis e `GAP` justificado).

## A conferir na execução
- Os três locais deram timeout na primeira leitura (VPN oscilou) — confirmar o acesso e a contagem de objetos de cada
  um antes de S1.
