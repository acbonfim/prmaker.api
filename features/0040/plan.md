# Plano — 0040: a Base Solvace responde perguntas de operação e configuração de todos os módulos

## Diagnóstico ("Como habilitar um módulo em uma planta de um cliente?")

- O "Pergunte" funcionou como foi feito: a IA gerou bons termos (habilitar/ativar módulo, ClientFuncionality,
  TB_WCM_SITE, administração), a busca achou o que existia e a IA disse `not-found`. **Falta o conteúdo.**
- A resposta existe, mas espalhada em 4 lugares do código: tela Angular `adm` (`/solvace-site/module`), API
  `revamp-Administration` (`PUT /Modules` → `TB_SYS_Application.IsActive` no **banco Local do site**), legado
  (`administration/Internal/Application`) e migrações de menu (`TB_WCM_MENU`, `ONLY_SOLVACE_ADMIN`). Mais as condições
  para o módulo aparecer: grupo de menu do `HOME_SCREEN_ID`, item de menu no Global, papéis por site, módulos privados.
- **Por quê a base não tem:** ela foi gerada do código por repositório (0033/0034: endpoints, tabelas, integrações) —
  documenta *o que o código é*, não *como se opera o produto*. Procedimentos de configuração ("como habilitar",
  "como dar acesso", "onde configura", "por que não aparece") não estão em nenhuma seção. O legado (onde está a
  administração) tem só 4 seções. O KC está em dev (13 artigos). O "Analisar a fundo" (0038) só relê a base e a lacuna
  fica parada — ninguém a resolve lendo o código.

## O que é "pergunta desse tipo"

Perguntas de **operação e configuração** — o que suporte, QA, implantação e gestores perguntam:

| Tema | Exemplos |
|---|---|
| Habilitar / contratar | Como habilitar o módulo X na planta? Por que o módulo aparece com cadeado? |
| Acesso e papéis | Como dar acesso ao X? Quem pode aprovar no X? O que o admin do módulo pode fazer? |
| Configuração do módulo | Onde configuro os tipos/status/categorias do X? Que parâmetros o X tem por site? |
| Cadastros necessários | O que precisa estar cadastrado antes de usar o X? |
| Notificações | Quando o X manda e-mail? Como ligar/desligar a notificação? |
| Diagnóstico | Por que o usuário não vê o X? Por que a ação Y não aparece? |
| Plataforma (transversal) | Como trocar de planta? O que é admin Solvace × admin do cliente × admin do módulo? Onde ficam os parâmetros globais × de site? Por que a mudança de parâmetro demora (cache)? |

## Solução — três frentes

**1. Cobrir a operação com antecedência (o conhecimento já pronto na base).**
- Projeto transversal **`operacao-plataforma`** (kind `business-rules`): o que vale para todos os módulos — habilitar
  módulo no site, menu/grupos/Home, papéis (Solvace admin, admin do cliente, admin do módulo, usuário), módulos
  privados, parâmetros globais × de site × do cliente (e o cache Redis), troca de planta, banco Global × Local por
  site, diagnóstico "não aparece" com SQL somente leitura. Seção técnica `operacao` + Guia `guia-como-configurar`.
- Em **cada módulo**, duas seções novas no template:
  - técnica `operacao` (085, "Configuração e operação"): telas de configuração do módulo (menu e rota), papéis do
    módulo e o que cada um libera, parâmetros (chave, onde muda, efeito), cadastros base, notificações configuráveis,
    diagnóstico "não aparece/não consigo" com SQL somente leitura — com `arquivo:linha`. Vai para o espelho (a análise
    de bugs ganha isso também).
  - Guia `guia-como-configurar` (545, "Como configurar e dar acesso"): o mesmo em passo a passo para suporte/QA.
- **Extrator determinístico** (`mapear.py operacao`, sem LLM) levanta os fatos baratos de cada módulo: itens de menu
  do módulo (migrações `TB_WCM_MENU`: nome, rota, `ONLY_SOLVACE_ADMIN`, grupo), papéis (`TB_WCM_ROLE` por aplicação),
  chaves de parâmetro usadas no código (`PARAM_KEY`, `TB_WCM_SITE_PARAMETER`, `TB_WCM_GLOBAL_PARAMETER`), rotas de
  configuração do app Angular (`settings`, `config`, `adm`), controllers de configuração. O LLM só lê o que o fato
  aponta e escreve — mesmo modelo de custo da 0034.

**2. Responder bem na hora.**
- O "Pergunte" classifica a pergunta (`operacao` | `regra` | `tecnica` | `outra`) no passo de planejamento e, quando é
  de operação, puxa as seções `operacao`/`guia-como-configurar` do módulo e o `operacao-plataforma`, e responde em
  passo a passo (com "se não aparecer, confira…").

**3. Aprender com o que ninguém respondeu.**
- **Registro de perguntas**: toda pergunta do "Pergunte" fica registrada (texto normalizado, cobertura, seção sugerida,
  quem, quando, quantas vezes). `not-found`/`partial` viram a fila "Perguntas sem resposta" (admin vê na tela, com
  contagem).
- **Resolver lacunas lendo o código** (skill `base-solvace`): `arch.sh perguntas` lista as pendentes; o procedimento
  investiga o código (subagente), escreve/acrescenta a seção certa (técnica e Guia), publica e marca a pergunta como
  respondida com a seção. Opcional: rodar sozinho todo dia na máquina do admin (`claude -p` agendado).

## Contrato (backend)
- `ArchitectureQuestion` (schema `knowledge`): `Text`, `Normalized` (único), `Coverage` (último), `Kind`
  (`operacao|regra|tecnica|outra`), `SuggestedProject/Section`, `Times`, `FirstAskedAt/By`, `LastAskedAt/By`, `Status`
  (`open|answered|dismissed`), `AnsweredProject/Section`, `ResolvedBy/At`, `Note`. Migração `AddArchitectureQuestions`.
- `POST ask` e `POST ask/deep` registram a pergunta (sem bloquear a resposta). `ArchitectureAskResponse` ganha `kind`.
- `GET Architecture/questions?status=open|answered|all&coverage=` (admin) → lista com contagem;
  `POST Architecture/questions/{id}/resolve` (admin) `{status: answered|dismissed, projectKey?, sectionKey?, note?}`.
- Templates: técnico ganha `operacao` (85); Guia ganha `guia-como-configurar` (545).

## Fases

| Fase | O quê | Depende de |
|---|---|---|
| **B1** | Registro de perguntas (entidade, migração, ask/deep registram, GET/resolve) | — |
| **B2** | Pergunte de operação: `kind` no plano, reforço das seções `operacao`/`guia-como-configurar` e do `operacao-plataforma`, resposta em passo a passo; templates novos | — |
| **K1** | Projeto `operacao-plataforma` (técnica + Guia) escrito a partir do código (habilitar módulo, menu/Home, papéis, privados, parâmetros/cache, troca de planta, Global × Local, diagnóstico + SQL) | — |
| **S1** | `mapear.py operacao <repo|pasta>` — fatos de operação por módulo (menus, papéis, parâmetros, rotas e controllers de configuração) | — |
| **S2** | Skill: procedimento "mapear operação" (`references/operacao.md`: checklist dos temas, template das 2 seções, uso dos fatos, subagente por módulo) e "resolver perguntas" (`arch.sh perguntas` / `pergunta-respondida`); template e SKILL.md | B1, S1 |
| **F1** | Tela: painel "Perguntas sem resposta" (admin: contagem, quem perguntou, "analisar a fundo", marcar respondida/descartar); no Pergunte, aviso "registramos a pergunta — a base vai aprender" | B1 |
| **Q1** | **Bateria de 40 perguntas de operação** espalhadas pelos módulos (habilitar, acesso, configurar, notificação, "não aparece"…) — roda no Pergunte e mede a cobertura antes e depois | B2, K1 |
| **P1** | **Piloto**: `operacao` + `guia-como-configurar` em 3 módulos (Plano de Ação, Checklist, Usuários) + `operacao-plataforma`; mede tokens/tempo por módulo e a cobertura da bateria | K1, S1, S2 |
| **G1** | Geração em massa nos demais módulos (legado e apps incluídos) — **depende de autorização** (custo medido no P1) | P1 |

**Ondas:** 1 = B1 ‖ B2 ‖ K1 ‖ S1 · 2 = S2 ‖ F1 ‖ Q1(linha de base) · 3 = P1 → Q1(depois) · 4 = PRs → G1 (autorizado).

## Decisões
- **D1** Conhecimento de operação também vai para o espelho (seção técnica `operacao`) — a análise de bugs ganha o
  diagnóstico "não aparece". O Guia `guia-como-configurar` fica só na tela (0038).
- **D2** O registro de perguntas guarda o texto e quem perguntou (visível só para admin).
- **D3** Trocar o KC para produção fica fora (configuração do plugin + credencial — decisão do admin).
- **Achado de segurança** (fora do escopo, card/PR à parte): `PUT /Modules` do revamp-Administration não confere
  Solvace admin no servidor — só o menu esconde.
