# Feature 0033 — Base de conhecimento Solvace, engenharia reversa e skills mais baratas

Objetivo (spec): análises mais assertivas, com regra de negócio e arquitetura à mão, **gastando menos tokens**, e
retomar um tratamento sem copiar/colar comando.

## Diagnóstico (levantado em 2026-09-30)

**Onde o token vai hoje** — sessão real do card 74775 (`7d4a89db`): 120 turnos, 12,7 M tokens lidos de cache
(~106 mil de contexto **por turno**), 72 mil de saída. O custo é *turnos × tamanho do contexto*:
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

## Decisões propostas (confirmar as marcadas com ❓)

1. **Engenharia reversa: PRMake é a fonte da verdade + espelho local nas máquinas** (responde 2.3).
   - Banco do PRMake: projetos → seções versionadas (markdown + mermaid), com o commit de origem de cada projeto;
     tela para ver/editar/sugerir (2.4/2.5); histórico de versões.
   - Espelho local `~/.claude/solvace-kb/` sincronizado por hash, igual às skills (hook `SessionStart` já existe):
     ler arquivo local não custa rede nem chamadas; a skill abre **primeiro um índice compacto** (~3–5 mil tokens
     para o parque inteiro: projeto → responsabilidade, módulos, pastas-chave, tabelas, integrações, palavras-chave)
     e só depois a seção que precisa. Sem espelho a skill cai para a API (`GET /Architecture/...`).
   - Por que não só no banco: cada consulta viraria um turno + resposta HTTP no contexto. Por que não só local:
     sem tela, sem edição revisada, sem versão única para o time.
2. **Geração pela skill `mapear-arquitetura`** (nova), rodada por quem tem os repositórios: um projeto por vez, com
   template fixo de seções; publica no PRMake (toda escrita pelo PRMake). **Incremental**: guarda o commit de cada
   projeto e depois só reanalisa o `git diff` desde ele. Ordem: repositórios com mais bugs/PRs no PRMake primeiro.
3. **Knowledge Center pelo PRMake** (config no plugin, nada fixo — 1.2): plugin *Knowledge Center Configurations*
   (ambiente `dev|prod`, BaseUrl por ambiente, modo de acesso) semeado por migração; a skill usa
   `kc.sh search|article` que respeita essa config. ❓ **Modo de acesso**:
   - **(A, recomendado) API do KC com um usuário de serviço** (login da plataforma no DEV, credencial no Secret
     Manager do PRMake): funciona do Cloud Run, respeita as regras do módulo; trocar para produção = trocar BaseUrl.
   - (B) Banco Aurora direto, somente leitura, pela máquina de quem roda a skill (como o `sql-query.sh`): não depende
     de login, mas o Cloud Run provavelmente não alcança a VPC — a tela do PRMake não conseguiria consultar.
4. **Chat de melhoria (2.5)**: só admin **e** com o plugin de IA configurado; o especialista recebe a seção + o
   índice; responde com sugestão; "Aplicar" cria nova versão (nada é sobrescrito sem o admin aceitar). O
   `IAIService` ganha conversa com histórico (hoje só prompt único).
5. **Retomar (item 4)** — em camadas, a primeira já resolve o dia a dia:
   - a skill grava `CLAUDE_CODE_SESSION_ID`, máquina e pasta no plano (`start`/`control`);
   - comando único **`prmake-card <card>`** (instalado com as skills): se o plano tem sessão nesta máquina →
     `claude --resume <id>` (volta ao raciocínio exato); senão → `claude -n "<card>" "/analisar-bug <card>"`. O botão
     no PRMake copia `prmake-card 74775` (igual para qualquer estado);
   - ❓ **Continuar sozinho em segundo plano** (opcional): um vigia local único (`prmake-skills.sh agent`, launchd)
     percebe que as respostas chegaram (ou o clique em "Continuar") e roda `claude --bg --resume <id> "respostas
     chegaram, continue"`; o usuário acompanha no PRMake ou `claude attach`. Precisa de permissões pré-aprovadas
     para rodar sem ninguém olhando — proposta: só até a próxima pergunta/PR, nunca merge.

## Item 3 — skills mais baratas e assertivas (medido antes/depois)
- **SKILL.md enxuta** (~3 mil tokens): fluxo e regras essenciais; o resto vira `references/*.md` lido só na fase
  (catálogo de tratamentos, Cognito, SQL, correção/branches, fechamento no DevOps). Economia ≈ 9 mil tokens por
  turno de contexto (-8% a -10% do custo da análise, mais nas longas).
- **`contexto <card>`**: um comando devolve em texto compacto card + repro + comentários/anexos novos + estado do
  plano + trecho relevante do índice da arquitetura + artigos do KC — troca 5–10 turnos iniciais por 1.
- **Arquitetura primeiro**: índice → seção do módulo → arquivos certos; `grep` só dentro do módulo apontado.
- **Imagens reduzidas** antes do Read (`sips -Z 1568`, qualidade suficiente): prints de ~300 KB viram ~80 KB.
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
| D0 | Confirmar ❓: acesso ao KC (A/B + usuário de serviço) e o vigia em segundo plano | — |
| B1 | Plugin *Knowledge Center Configurations* (migração) + `GET /Knowledge/search`/`article` (proxy, cache curto) | D0 |
| B2 | Módulo de arquitetura: projetos, seções versionadas, índice, `export` por hash para o espelho, permissões | — |
| B3 | `IAIService` com conversa + `POST /Architecture/sections/{id}/chat` (admin + plugin de IA) | B2 |
| B4 | Plano: sessão/máquina/pasta do executor, `usage` (custo), pedido de "continuar" | — |
| F1 | Tela **Arquitetura**: árvore (ecossistema, projetos, módulos, integrações, infra/AWS, terceiros, login), leitor markdown + mermaid, busca | B2 |
| F2 | Edição (admin), histórico/versões, chat "Sugerir melhoria" com aplicar/descartar, fila de sugestões da skill | B3, F1 |
| F3 | Plano de execução: botão "Retomar no Claude" (`prmake-card`), custo da análise, estado do vigia | B4 |
| S1 | Skill `mapear-arquitetura` + template de seções + espelho local (sync no `prmake-skills.sh`) | B2 |
| S2 | Gerar a base: semente do `claude-global` + projetos por prioridade (legado, apps, API de integrações, revamp) | S1 |
| S3 | `analisar-bug` enxuta: references, `contexto`, arquitetura primeiro, KC (`kc.sh`), imagens, saídas curtas, aprendizado | B1, S1 |
| S4 | Retomar: `prmake-card`, sessão no plano; (opcional) vigia `agent` com `--bg --resume` | B4, D0 |
| Q1 | Medir: 2–3 cards reais antes/depois (turnos, tokens, tempo) + teste local ponta a ponta + PRs | todas |

Ondas: **1** D0 · B2 · B4 · S1 (template) — **2** B1 · B3 · F1 · F3 · S3 (parte sem KC) · S4 — **3** F2 · S2 · S3 (KC) — **4** Q1.
S2 é a fase mais longa (um projeto por vez, incremental); a tela e a skill já funcionam com o que estiver publicado.

## Riscos
- **Base desatualizada** engana a análise: cada seção mostra o commit de origem e a skill avisa quando o repo local
  está muito à frente (e sugere `mapear-arquitetura atualizar <projeto>`).
- **Dados sensíveis** na engenharia reversa (hosts, contas, segredos): template proíbe credenciais; só nomes de
  recursos. Acesso à tela: leitura para usuários logados, edição só admin.
- **Custo da geração inicial** (S2) é alto uma vez só; incremental depois. Priorizar pelos repositórios com mais bugs.
- **Vigia em segundo plano** age sem ninguém olhando: limitado a análise e perguntas, nunca merge; opcional.
