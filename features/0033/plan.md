# Feature 0033 — Base de conhecimento Solvace, engenharia reversa e skills mais baratas

Objetivo (spec): análises mais assertivas, com regra de negócio e arquitetura à mão, **gastando menos tokens**, e
retomar um tratamento sem copiar/colar comando.

## Diagnóstico (levantado em 2026-09-30)

**Onde o token vai hoje** — sessão real do card 74775 (`7d4a89db`): 63 respostas, 6,68 M tokens lidos de cache
(~108 mil de contexto **por resposta**), 33 mil de saída. (Corrigido em 2026-09-30: a 1ª medição somava cada linha do
transcript, e ele grava a mesma resposta em várias linhas — ~2,4× — deduplicado por id de mensagem agora.)
O custo é *respostas × tamanho do contexto*:
- A `analisar-bug/SKILL.md` tem ~12,3 mil tokens e entra inteira em todo turno (≈11% do contexto) e de novo a cada
  retomada/`/analisar-bug`.
- Os 3 prints do usuário lidos em tamanho original somaram ~870 KB de resultado de ferramenta.
- 26 chamadas ao `prmake-plan.sh`, 14 ao SQL, buscas `grep/find` sem limite — muitos turnos pequenos.
- Retomar = `/analisar-bug <card>` numa sessão nova: relê SKILL.md, plano, notas e código já vistos.

**Knowledge Center** (`revamp_separado/revamp-KnowledgeCenter`): biblioteca de artigos (título, conteúdo em JSON/
texto puro, categorias, subcategorias, tags), schema `knowledgeCenter` no Aurora PostgreSQL; API em
`https://api-knowledge-center-dev.solvacelabs.com` exige o **JWT da plataforma Solvace** (claims `UserId`,
`EnvironmentId`, `SiteId`) e o módulo ativo. Busca pronta: `GET /api/v1/consumption/articles?searchTerm=` (título,
`ContentPlainText`, tags) e `GET /api/v1/consumption/articles/{id}`. Só existe em DEV hoje.

**Parque a mapear**: `edv-solvace` (legado .NET Core + ASP Classic, ~15 mil arquivos), `edv-solvace-apps` (Angular,
~7,8 mil), `edv-solvace-api` (integrações), 43 módulos no monorepo `revamp` + 18 repos revamp separados, AWS
(Cognito, RDS SQL Server por cliente, Aurora, S3, CodePipeline/CloudWatch). Já existe material reaproveitável no
`claude-global` (electradv): `revamp/architecture.md`, `revamp/context` (padrões/anti-padrões), `source/` (arquitetura
revamp, pipelines, branching) e skills `core-asp-expert`/`sustain-ticket-triage` — entra como semente, não do zero.

**Retomar sessão**: o Claude Code expõe `CLAUDE_CODE_SESSION_ID` para os scripts e tem `claude --resume <id>`,
`claude --bg --resume <id> "<prompt>"` (continua em segundo plano), `claude attach <id>` e `-n <nome>`. Dá para a
skill registrar a sessão no plano e voltar ao raciocínio exato — sem reler tudo.

## Decisões (confirmadas pelo usuário em 2026-09-30)

1. **Engenharia reversa: PRMake é a fonte da verdade + espelho local nas máquinas** (responde 2.3).
   - Banco do PRMake: projetos → seções versionadas (markdown + mermaid), com o commit de origem de cada projeto;
     tela para ver/editar/sugerir (2.4/2.5); histórico de versões.
   - Espelho local `~/.claude/solvace-kb/` sincronizado por hash, igual às skills (hook `SessionStart` já existe):
     ler arquivo local não custa rede nem chamadas; a skill abre **primeiro um índice compacto** (~3–5 mil tokens
     para o parque inteiro: projeto → responsabilidade, módulos, pastas-chave, tabelas, integrações, palavras-chave)
     e só depois a seção que precisa. Sem espelho a skill cai para a API (`GET /Architecture/...`).
   - Por que não só no banco: cada consulta viraria um turno + resposta HTTP no contexto. Por que não só local:
     sem tela, sem edição revisada, sem versão única para o time.
2. **Geração pela skill `base-solvace`** (nova; reúne mapear/atualizar a engenharia reversa, o Knowledge Center e o espelho local — as outras skills dependem dela), rodada por quem tem os repositórios: um projeto por vez, com
   template fixo de seções; publica no PRMake (toda escrita pelo PRMake). **O Knowledge Center entra já na primeira
   geração** (pedido do usuário): cada projeto/módulo ganha a seção *Regras de negócio (Knowledge Center)* com os
   artigos relacionados (`ART-n`, título, resumo de 2–3 linhas, categoria/tags) cruzados pelo nome do módulo,
   telas e termos de domínio; e a árvore tem um ramo *Regras de negócio* com o mapa de categorias/subcategorias
   do KC. Assim a análise acha a regra pelo índice, sem consultar o banco — o `kc-query.sh` fica para o detalhe
   de um artigo ou para o que ainda não foi mapeado. Ao atualizar (incremental), artigos novos/alterados desde a
   última geração (`UpdateDate`) são reprocessados. **Incremental**: guarda o commit de cada
   projeto e depois só reanalisa o `git diff` desde ele. Ordem: repositórios com mais bugs/PRs no PRMake primeiro.
3. **Knowledge Center — banco Aurora direto, somente leitura, pela máquina de quem roda a skill** (decidido em
   2026-09-30). Script `kc-query.sh search|article|categories` (mesmo modelo do `sql-query.sh`: valida leitura,
   transação com ROLLBACK, limite de linhas, credenciais locais em `~/.claude/`, nunca impressas) sobre o schema
   `knowledgeCenter`: busca em título, `ContentPlainText` e tags; devolve só trechos relevantes (não o artigo todo).
   **Configuração no PRMake** (nada fixo — 1.2): plugin *Knowledge Center Configurations* semeado por migração com o
   ambiente ativo (`dev` hoje; `prod` no futuro), o alias de conexão de cada ambiente e o schema; a skill lê pelo
   `GET /Skills/config`. Trocar para produção = mudar o ambiente no plugin + ter a credencial `prod` local.
   A tela do PRMake não consulta o KC (o Cloud Run não alcança a VPC).
4. **Chat de melhoria (2.5)**: só admin **e** com o plugin de IA configurado; o especialista recebe a seção + o
   índice; responde com sugestão; "Aplicar" cria nova versão (nada é sobrescrito sem o admin aceitar). O
   `IAIService` ganha conversa com histórico (hoje só prompt único).
5. **Retomar (item 4)** — em camadas, a primeira já resolve o dia a dia:
   - a skill grava `CLAUDE_CODE_SESSION_ID`, máquina e pasta no plano (`start`/`control`);
   - comando único **`prmake-card <card>`** (instalado com as skills): se o plano tem sessão nesta máquina →
     `claude --resume <id>` (volta ao raciocínio exato); senão → `claude -n "<card>" "/analisar-bug <card>"`. O botão
     no PRMake copia `prmake-card 74775` (igual para qualquer estado);
   - **Continuar sozinho em segundo plano** (decidido: opcional por pessoa, desligado por padrão): um vigia local único (`prmake-skills.sh agent`, launchd)
     percebe que as respostas chegaram (ou o clique em "Continuar") e roda `claude --bg --resume <id> "respostas
     chegaram, continue"`; o usuário acompanha no PRMake ou `claude attach`. Precisa de permissões pré-aprovadas
     para rodar sem ninguém olhando — proposta: só até a próxima pergunta/PR, nunca merge.

## Acesso ao Knowledge Center — verificado em 2026-09-30
- AWS CLI autenticada (conta 367983645102); clusters Aurora PostgreSQL `solvace-pstgdev` e `solvace-pstgprd`
  (endpoints de leitura `*.cluster-ro-cjsrhvr5mbhe.us-east-1.rds.amazonaws.com:5432`).
- Rede: a porta 5432 dos dois responde a partir da máquina do usuário (TCP ok).
- **Teste de login (2026-09-30)** com o segredo `multilingual/development` (usuário `usrmtlngdev`, gravado pelo
  usuário em `~/.claude/postgres-credentials-dev.json`): conecta no `solvace-pstgdev`; o KC fica no database
  **`KnowledgeCenter`**, schema **`knowledge_center`** (snake_case — a doc do repo diz `knowledgeCenter`), 25 tabelas;
  volume estimado pelas estatísticas: ~59 artigos, 35 categorias, 25 subcategorias, 32 tags, 28 categorias de tag.
- Esse usuário não tem USAGE/SELECT no schema do KC. **Credencial do KC fornecida pelo usuário** (usuário da
  aplicação `app_devadmin`, host `solvace-pstgdev-instance-1`, database `KnowledgeCenter`) — gravada só em
  `~/.claude/knowledgecenter-credentials.json` (600, formato `{ "dev": {...}, "prod": {...} }`). **Teste ok**: lê
  tudo; a sessão abre com `default_transaction_read_only=on` e o banco recusou escrita (`ReadOnlySqlTransaction`).
  Recomendação mantida: trocar por um usuário só-leitura dedicado quando o DBA puder (a do admin ficou na conversa).
- **Conteúdo real em DEV (2026-09-30)**: 65 artigos (25 publicados, 29 rascunhos, 5 arquivados), 41 categorias,
  68 tags — **a maior parte é dado de teste de QA** (categorias "teste", "123x", "Teste QA"...; títulos
  "title-Haroldo", "teste qa"). Conteúdo útil: ~13 artigos publicados sobre o **Action Plan** (permissões, kanban,
  calendário, analytics, notificações, histórico, feed, checklist) + visão geral do produto — ~15 mil caracteres
  (~4 mil tokens) no total. Consequências:
  - o `kc-query.sh` e a geração só usam **publicados** (`status_id = 30`, `is_deleted = false`) e aplicam um
    **filtro de ruído configurável no plugin** (categorias/títulos excluídos por padrão, tamanho mínimo do texto);
  - o valor do KC hoje é pequeno e concentrado no Action Plan; cresce quando apontar para produção (mesma estrutura,
    só troca o ambiente no plugin + credencial `prod` no arquivo local);
  - colunas úteis de `articles`: `article_id` (ART-n), `title`, `content_plain_text`, `subcategory_unique_id`,
    `last_update_date` (para o incremental); categorias via `subcategories` → `categories`; tags via `article_tags`.
- Cliente: `psycopg[binary]` no venv da skill (sem `psql` local).

## Knowledge Center — garantias (requisito do usuário, 2026-09-30)

**1. O filtro de dados de teste sempre existe** — não é opção de configuração, é código:
- Piso fixo no código (script de consulta/sincronização e backend, com a mesma regra): só `status_id = 30`
  (publicado) e `is_deleted = false`; descarta categoria/subcategoria/título que casem com o padrão de teste embutido
  (`teste`, `test`, `qa`, `123`, `tmp`, `title-`, `categoryName`, `modal editado`...) e texto útil abaixo de um mínimo.
- O plugin só **acrescenta** (padrões extras, artigos `ART-n` excluídos) — não existe chave para desligar o piso. O
  único jeito de deixar passar algo que o piso barra é uma lista explícita de `ART-n` liberados, auditada.
- Na tela do PRMake o admin pode marcar um artigo como "teste" → entra na lista de excluídos do plugin.
- Teste automatizado da regra (casos reais do DEV: "title-Haroldo", "teste qa", categorias "Teste QA" saem; os
  artigos do Action Plan ficam) roda no `kc-query.sh check` e nos testes do backend.

**2. As skills mantêm o KC sempre atualizado do nosso lado**:
- Cópia filtrada no PRMake (módulo de conhecimento): artigo (`ART-n`, título, texto puro, categoria/subcategoria,
  tags, `last_update_date`, ambiente). O Cloud Run não alcança o Aurora, então **quem sincroniza é a skill**, na
  máquina de quem tem a credencial: lê só o que mudou desde a marca d'água (`max(last_update_date)` + ids removidos/
  despublicados) e envia ao PRMake (`POST /Knowledge/sync`). Quem não tem credencial não sincroniza, mas usa a cópia.
- Gatilhos: toda execução da `analisar-bug`/`gerar-prmake`/`gerar-handover` (1 consulta barata à marca d'água; só
  envia se mudou) e o comando `kc.sh sync` (skill `base-solvace`). A tela mostra a última sincronização e o ambiente.
- Espelho local (`~/.claude/solvace-kb/knowledge/`) atualizado junto com a arquitetura (sync por hash) — a
  consulta na análise é leitura de arquivo, sem rede e sem credencial.
- Artigo novo/alterado reprocessa só as seções de arquitetura ligadas a ele (incremental).

**3. Consultar a base sempre que possível**:
- O `contexto <card>` já traz os artigos relacionados ao módulo/termos do card (do espelho local) — zero turno extra.
- Regra na SKILL.md: antes de perguntar ao usuário sobre regra de negócio ou concluir comportamento "esperado",
  consultar o KC; citar `ART-n` na análise, no RCA e no handover quando usado; registrar no plano quando não houver
  artigo (lacuna de documentação — vira sugestão para o time do KC).
- `gerar-prmake` e `gerar-handover` também consultam (regra de negócio no RCA/handover).

**4. Trocar para produção é uma mudança de configuração**:
- Plugin *Knowledge Center Configurations*: `Environment` = `dev` | `prod` (um campo). O arquivo local de credenciais
  tem uma entrada por ambiente (`{ "dev": {...}, "prod": {...} }`); host/database/schema vêm do plugin por ambiente.
- Ao trocar o ambiente, a próxima sincronização percebe e faz a carga completa do novo ambiente; a cópia do anterior
  fica guardada e oculta (volta se desfizer a troca). Sem deploy e sem mudar skill.
- Checklist da troca: credencial `prod` só-leitura no arquivo local de quem sincroniza → mudar `Environment` no
  plugin → rodar `kc.sh sync` (skill `base-solvace`) → conferir contagem na tela.

## Item 3 — skills mais baratas e assertivas (medido antes/depois)
- **SKILL.md enxuta** (~3 mil tokens): fluxo e regras essenciais; o resto vira `references/*.md` lido só na fase
  (catálogo de tratamentos, Cognito, SQL, correção/branches, fechamento no DevOps). Economia ≈ 9 mil tokens por
  turno de contexto (-8% a -10% do custo da análise, mais nas longas).
- **`contexto <card>`**: um comando devolve em texto compacto card + repro + comentários/anexos novos + estado do
  plano + trecho relevante do índice da arquitetura + artigos do KC — troca 5–10 turnos iniciais por 1.
- **Arquitetura primeiro**: índice → seção do módulo → arquivos certos; `grep` só dentro do módulo apontado.
- ~~Imagens reduzidas antes do Read~~ — descartado: a API já limita o custo de imagem pelo tamanho em pixels (~1,6 mil
  tokens por print), reduzir quase não economiza e piora a leitura de telas.
- **Saídas curtas por padrão**: `revamp-repos.sh grep` com limite por arquivo/total; `sql-query` com TOP e colunas
  truncadas; `prmake-plan.sh` silencioso quando dá certo.
- **Buscas amplas num subagente** (Explore) — o resultado volta resumido e o contexto principal não incha.
- **Aprendizado**: ao concluir, a skill propõe (não grava sozinha) acréscimos à seção do módulo ("regras
  descobertas", "armadilhas") — o admin aceita na tela. Próximos bugs do módulo ficam mais baratos.
- **Custo visível**: `prmake-plan.sh usage` lê o transcript da própria sessão e grava no plano turnos/tokens —
  o PRMake mostra o custo por análise para comparar antes/depois.
- `gerar-prmake`/`gerar-handover`: mesma técnica (referências sob demanda, `contexto` compartilhado).

## Fases
| Fase | Descrição | Depende |
|---|---|---|
| D0 | Decisões: KC pelo banco Aurora local; vigia em segundo plano opcional por pessoa | — |
| B1 | Plugin *Knowledge Center Configurations* (migração: `Environment`, host/db/schema por ambiente, exclusões extras) no `Skills/config` + módulo de conhecimento: cópia filtrada, `POST /Knowledge/sync`, marca d'água, piso de filtro no backend + testes | D0 |
| B2 | Módulo de arquitetura: projetos, seções versionadas, índice, `export` por hash para o espelho, permissões | — |
| B3 | `IAIService` com conversa + `POST /Architecture/sections/{id}/chat` (admin + plugin de IA) | B2 |
| B4 | Plano: sessão/máquina/pasta do executor, `usage` (custo), pedido de "continuar" | — |
| F1 | Tela **Arquitetura**: árvore (ecossistema, projetos, módulos, integrações, infra/AWS, terceiros, login), leitor markdown + mermaid, busca | B2 |
| F2 | Edição (admin), histórico/versões, chat "Sugerir melhoria" com aplicar/descartar, fila de sugestões da skill | B3, F1 |
| F3 | Plano de execução: botão "Retomar no Claude" (`prmake-card`), custo da análise, estado do vigia | B4 |
| S1 | Skill `base-solvace` (mapear/atualizar, template de seções, espelho local `kb.sh`, sync no `prmake-skills.sh`, `depends`) | B2 |
| S2 | Gerar a base: semente do `claude-global` + **artigos do Knowledge Center** + projetos por prioridade (legado, apps, API de integrações, revamp) | S1, KC |
| K1 | `kc-query.sh` (search/article/check) + sincronização incremental com piso de filtro fixo e teste da regra | B1 |
| S3 | `analisar-bug` enxuta: references, `contexto` (com KC do espelho), arquitetura primeiro, regra "consultar KC antes de perguntar", imagens, saídas curtas, aprendizado; KC também na `gerar-prmake`/`gerar-handover` | B1, K1, S1 |
| S4 | Retomar: `prmake-card`, sessão no plano; (opcional) vigia `agent` com `--bg --resume` | B4, D0 |
| Q1 | Medir: 2–3 cards reais antes/depois (turnos, tokens, tempo) + teste local ponta a ponta + PRs | todas |

Ondas: **0** KC: credencial local + `kc-query.sh` testado no DEV — **1** B1 · B2 · B4 · S1 (template) — **2** B1 · B3 · F1 · F3 · S3 (parte sem KC) · S4 — **3** F2 · S2 · S3 (KC) — **4** Q1.
S2 é a fase mais longa (um projeto por vez, incremental); a tela e a skill já funcionam com o que estiver publicado.

## Riscos
- **Base desatualizada** engana a análise: cada seção mostra o commit de origem e a skill avisa quando o repo local
  está muito à frente (e sugere `arch.sh stale <projeto> <pasta>` + reescrever as seções afetadas).
- **Dados sensíveis** na engenharia reversa (hosts, contas, segredos): template proíbe credenciais; só nomes de
  recursos. Acesso à tela: leitura para usuários logados, edição só admin.
- **Custo da geração inicial** (S2) é alto uma vez só; incremental depois. Priorizar pelos repositórios com mais bugs.
- **Credencial do Aurora do KC**: cada pessoa precisa dela na máquina (como as do SQL Server); sem ela a skill segue
  sem o KC e avisa.
- **Vigia em segundo plano** age sem ninguém olhando: limitado a análise e perguntas, nunca merge; opcional.
