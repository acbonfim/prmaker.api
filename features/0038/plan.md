# Plano — 0038: Base Solvace para pessoas (QA, gestores) e aprender com um card

## Diagnóstico

**A tela foi feita para a LLM ler, não para pessoas.** O conteúdo das seções é o que a skill lê: tabelas markdown
(~2 mil linhas na base), ~250 nomes `TB_*`, controllers, namespaces e caminhos em `code`. Não existe uma camada
"o que é / para que serve / como funciona" em linguagem de negócio. Na tela aparecem chave do projeto
(`revamp-actionplan`), commit/branch, "~1,2 mil tokens", "versão N · gerada pela skill"; as relações vêm como
"Evento (SNS → fila)", "Fila (SQS)"; o nome "Revamp — Action Plan" pressupõe conhecer o histórico técnico; o mapa
(Cytoscape com dezenas de nós) intimida; artigos do KC viram parágrafos soltos; não há sumário dentro da seção,
nem entrada por módulo de negócio.

**Backend:** a seção (`ArchitectureSection`) não tem público/visibilidade — o export para o espelho das skills
(`ExportAsync`, `RenderIndex`, `RenderProjectCard`, `Manifest`) leva **todas** as seções. Qualquer texto para
leigos entraria no índice da análise de bugs e mudaria o hash (download em todas as máquinas).

**Aprender com um card:** hoje o aprendizado só nasce quando a `analisar-bug` chega ao passo 9 e chama
`arch.sh suggest`. Cards tratados sem a skill (ou antes dela) nunca viram conhecimento na base.

## Solução

**1. Público da seção: `llm` (padrão) × `human`.** Seções técnicas continuam como estão (`llm`) e são as únicas
exportadas. Seções `human` (o **Guia**) aparecem só na tela, na busca e no "Pergunte". Retrocompatível: coluna nova
com default `llm`; `arch.sh`/`publicar.sh` sem mudança; INDEX.md, ficha e hash idênticos enquanto só o guia mudar.

Guia de cada projeto (chaves `guia-*`, ordem 500+, linguagem simples — sem nome de tabela/classe, frases curtas,
exemplos, glossário):

| Ordem | Chave | Título | Para quem |
|---|---|---|---|
| 510 | `guia-o-que-e` | O que é e para que serve | gestor, QA, suporte |
| 520 | `guia-como-funciona` | Como funciona, passo a passo | QA, suporte |
| 530 | `guia-regras` | Regras de negócio | todos (cita ART-n) |
| 540 | `guia-conexoes` | Com quem conversa | o que dispara o quê, quem fica escutando, filas, Lambdas, e-mails, o que acontece se falhar |
| 550 | `guia-como-testar` | Como testar | QA |
| 560 | `guia-perguntas` | Perguntas frequentes | opcional |

No projeto `ecossistema`: `guia-o-que-e` = "Como o Solvace funciona" e `guia-glossario`.

**2. Dados amigáveis do projeto** (fora do export e do hash): `DisplayName` ("Plano de Ação"), `Tagline` (uma frase
para leigo) e `BusinessArea` (agrupa legado + revamp + front do mesmo módulo de negócio na visão simples).

**3. Modo Simples × Técnico na tela.** Simples (padrão): nome amigável, frase, área, Guia em abas, "Com quem
conversa" em frases ("Quando um plano é criado, o Plano de Ação **avisa** o Notificações, que **envia** o e-mail"),
glossário em tooltip, sumário da seção; detalhes técnicos num bloco recolhido. Técnico: a tela de hoje.

**4. Guia gerado com a IA (admin).** Botão "Gerar guia com a IA" no projeto: a IA lê as seções técnicas, as
relações e os artigos do KC ligados e propõe o guia + dados amigáveis; o admin revisa e aplica (nada é gravado sem
aceite — mesmo padrão do chat de melhoria). A skill `base-solvace` também sabe escrever o guia (com o código à mão)
e publicar com `--audience human` — para a carga inicial do parque.

**5. Aprender com um card.** Qualquer usuário informa o número do card: o PRMake junta o card do DevOps (campos,
repro steps, root cause, comentários, histórico), o PR/RCA salvo no PRMake, a Timeline, os planos de execução
(achados/decisões) e as sugestões que já existem para o card; a IA propõe os aprendizados (projeto, seção, texto,
por quê, técnico ou guia). A pessoa revisa, edita e envia para a fila de sugestões (com o número do card) — o admin
incorpora como hoje. Se o card já tem sugestão (automática, da skill), a tela avisa e mostra quais.

## Contrato (backend)

- `ArchitectureSection.Audience` (`llm` | `human`); `PUT .../sections/{key}` aceita `audience` (null mantém; seção
  nova sem `audience` → `human` se a chave começa com `guia-`, senão `llm`). Respostas de seção e de busca trazem
  `audience`. Export/índice/ficha/manifest/`GET index` só com `llm`.
- `ArchitectureProject.DisplayName/Tagline/BusinessArea`; `PUT projects/{key}` aceita os três (null mantém, "" limpa).
- `POST Architecture/projects/{key}/guide` (admin) `{instructions?}` → `{provider, model, displayName, tagline,
  businessArea, sections:[{key,title,order,content}], notes}` — não grava.
- `POST Architecture/learn-from-card` (logado) `{cardNumber, instructions?}` → `{cardNumber, cardTitle, summary,
  sources:[…], existing:[sugestões do card], proposals:[{projectKey, sectionKey, audience, title, content, reason}],
  provider, model, aiUnavailableReason}` — não grava; a tela envia cada proposta aceita por `POST suggestions`.
- Chat de melhoria: seção `human` usa a persona de redator para leigos.

## Fases

| Fase | O quê | Depende de |
|---|---|---|
| **B1** | Público da seção: domínio + migração `AddSectionAudience` (default `llm`) + request/response + filtro no export/índice/ficha/manifest + busca com `audience` + persona do chat. Testes: seção `human` fora do zip/índice/hash. | — |
| **B2** | Dados amigáveis do projeto: domínio + migração `AddProjectFriendly` + request/response (fora do export/hash). | — |
| **B3** | `POST projects/{key}/guide` (IA, admin, não grava). | B1, B2 |
| **B4** | `POST learn-from-card` (IA, logado, não grava) juntando DevOps, PR/RCA, Timeline, planos e sugestões existentes. | B1 |
| **F1** | Modo Simples × Técnico; ficha amigável (cabeçalho em chips, Guia em abas, "Com quem conversa" em frases, técnico recolhido, sem chaves/commit/tokens); sumário da seção; artigo do KC formatado. | B1, B2 |
| **F2** | Visão geral para leigos: catálogo por área de negócio (nome amigável + frase), "Como o Solvace funciona", glossário; mapa simplificado (vizinhança do projeto, rótulos em português). | F1 |
| **F3** | Admin: "Gerar guia com a IA" (revisar/aplicar por seção), editor com público, dados amigáveis no "Dados do projeto", template com o Guia. | B3, F1 |
| **F4** | "Aprender com um card": diálogo (número → resumo do que foi feito, sugestões já existentes, propostas editáveis com projeto/seção/público) → enviar para a fila. | B4 |
| **S1** | Skill `base-solvace`: template do Guia (tom e regras), `arch.sh section --audience`, `publicar-pasta` reconhece `5NN-guia-*.md`, `arch.sh learn <card>` (chama o endpoint e mostra as propostas). `analisar-bug` não muda (o espelho não traz o Guia). | B1, B4 |
| **Q1** | Teste local (Postgres isolado, base semeada com o espelho): hash/índice iguais com guia novo; `kb.sh index` igual; tela Simples/Técnico; gerar guia e aprender com card (IA real se houver plugin no banco isolado, senão o caminho sem IA). | todas |
| **G1** | Carga inicial dos guias no parque (produção): pelos projetos mais usados, via botão ou skill — **depende de autorização** (escreve na base de produção). | deploy |

**Ondas:** 1 = B1 ‖ B2 ‖ S1(template) · 2 = B3 ‖ B4 ‖ F1 · 3 = F2 ‖ F3 ‖ F4 ‖ S1(learn) · 4 = Q1 → PRs.

## Decisões (assumidas — dá para trocar)
- **D1** Modo Simples é o padrão para todos; Técnico a um clique (lembrado no navegador).
- **D2** Guia gerado pela IA do PRMake (botão, admin revisa) e também pela skill (carga inicial com o código à mão).
- **D3** "Aprender com um card" aberto a qualquer usuário logado (só sugere; quem aplica é o admin).
- **D4** Busca e "Pergunte" incluem o Guia (é o conteúdo que o leigo entende); o "Pergunte" responde em linguagem simples.
