# Status — Feature 0033

| Fase | Descrição | Status |
|---|---|---|
| D0 | Decisões: KC pelo banco Aurora local; vigia opcional por pessoa | ✅ concluída |
| K0 | Credencial do KC DEV + leitura testada (db `KnowledgeCenter`, schema `knowledge_center`) | ✅ acesso ok (falta o `kc-query.sh`, fase S3) |
| B1 | Plugin Knowledge Center Configurations + cópia filtrada do KC (sync, estado, busca, artigo) | ✅ concluída |
| K1 | `kc.sh` (check/sync/search/article) + sincronização incremental + teste do filtro | ✅ concluída |
| B2 | Módulo de arquitetura (projetos, seções versionadas, índice, export) | ✅ concluída |
| B3 | Chat de IA com histórico + endpoint de sugestão | ✅ concluída |
| B4 | Sessão do executor no plano, custo da análise, pedido de continuar | ✅ concluída |
| F1 | Tela Arquitetura (leitura) | ✅ concluída |
| F2 | Edição, versões e chat de melhoria (admin) + fila de sugestões | ✅ concluída |
| F3 | Retomar no Claude + custo no plano de execução | ✅ concluída |
| S1 | Skill base-solvace (mapear/atualizar, template, espelho local, depends) | ✅ concluída |
| S2 | Geração da base (semente claude-global + projetos por prioridade) | ✅ 1ª carga pronta em `kb/` — publicar após o deploy |
| S3 | analisar-bug enxuta (references, contexto, arquitetura, KC, saídas) | ✅ concluída |
| S4 | prmake-card / resume; vigia opcional | ✅ concluída |
| Q1 | Medição antes/depois, teste local, PRs | 🔄 PRs abertos (#47 back, #28 front) — medição após o deploy |

## Log
- 2026-09-30: análise e plano (`plan.md`). Worktrees `prform.api-0033` e `prform-app-0033` na branch `feature/0033`.
  Linha de base de custo: card 74775 = 63 respostas, 6,68 M tokens de cache lidos, 33 mil de saída (valor corrigido —
  a 1ª medição contava linhas repetidas do transcript).
- 2026-09-30: D0 — KC pelo banco Aurora (somente leitura, máquina local, credencial em `~/.claude/`), ambiente no
  plugin do PRMake; vigia em segundo plano opcional por pessoa, desligado por padrão, nunca faz merge.
- 2026-09-30: acesso ao KC verificado só até a rede (porta 5432 do `solvace-pstgdev`/`pstgprd` responde); login
  pendente de credencial. KC passa a entrar já na primeira geração da engenharia reversa (S2).
- 2026-09-30: login testado com `multilingual/development`: conecta, acha o database `KnowledgeCenter` /
  schema `knowledge_center` (~59 artigos), mas sem USAGE/SELECT. Precisa de credencial com leitura no schema.
- 2026-09-30: credencial do KC (`app_devadmin`) testada: leitura ok, escrita recusada pela sessão somente leitura.
  DEV tem 25 artigos publicados, a maioria teste de QA; útil: ~13 do Action Plan + visão geral. Filtro de ruído no plugin.
- 2026-09-30: garantias do KC definidas pelo usuário (filtro de teste fixo, sync pelas skills, consultar sempre,
  troca para prod por configuração) — seção própria no `plan.md`.
- 2026-09-30: **onda 1 concluída** (B1, B2, B4, S1 + K1). Módulo `Solvace.Knowledge` (schema `knowledge`): cópia
  filtrada do KC e engenharia reversa; piso do filtro em `KnowledgeNoiseFilter` (C#) e `kc.py` (mesma regra, 36 testes
  xUnit + autoteste `kc.sh check`); plugin semeado; `Skills/config.knowledge`. Plano de execução: `Sessions` (jsonb),
  `resume-request`/`resume-ack`/`resume-candidates`, custo por sessão. Skill `base-solvace` (kc.sh, kb.sh, arch.sh,
  template e guia de mapeamento); `analisar-bug`/`gerar-prmake`/`gerar-handover` declaram `depends: base-solvace`;
  `prmake-skills.sh` instala dependências e sincroniza o espelho no `update`.
  Teste local (`.t0033`, Postgres isolado + KC real de DEV): carga inicial 65 lidos → 13 aceitos (exatamente os
  legítimos); incremental; exclusão pelo plugin remove na hora e desfazer traz de volta sem `--full` (hash do filtro);
  troca dev→prod sem credencial pula o sync e a cópia dev volta ao desfazer; escrita de arquitetura 403 para não-admin;
  seção igual não versiona; `kb.sh` baixa só quando o hash muda; B4 ponta a ponta (sessão, custo, continuar, candidatos
  por máquina, respostas pela tela viram candidato, planos antigos com `Sessions = []`).
  Achado: no banco real `article_unique_id` é inteiro (a doc do KC diz GUID) — `SourceId` virou texto.
- 2026-09-30: **onda 2 concluída** (B3, F1, F3, S3, S4).
  - B3: chat "Sugerir melhoria" (admin + plugin de IA configurado), conversa inteira por turno (provedores só aceitam
    prompt único), proposta entre marcadores `<<<SECAO`/`SECAO>>>` (a seção pode ter ```mermaid```); nada é gravado sem
    o admin aplicar. `GET Architecture/chat/status`.
  - F1: tela **Base Solvace** (`/auth/architecture`, menu para todos): árvore por tipo + regras de negócio do KC, busca,
    projeto com seções em abas, markdown + **mermaid** (CDN sob demanda, modo strict), artigo do KC, links diretos
    (`?p=&s=`, `?art=`), celular.
  - F3: no plano, botão **Retomar no Claude** (copia `prmake-card.sh <card>`), **Continuar sozinho** (pedido ao vigia,
    ampulheta enquanto pendente) e **custo** (respostas e tokens, detalhe no tooltip); aviso de "sem sinal" com o comando
    de terminal.
  - S3: `analisar-bug/SKILL.md` 49,3 KB → 8,6 KB (~12,3 mil → ~2,1 mil tokens por resposta); detalhes em `references/`;
    `prmake-plan.sh contexto` (card, repro, plano, comentários, sync KC/base, trechos do índice e artigos do KC num
    comando); `revamp-repos.sh grep` limitado (3/arquivo, 80 linhas); KC antes do RCA/handover nas outras skills.
  - S4: sessão registrada no plano pela própria skill; `usage` lê o transcript (deduplicado, só desde o início do
    plano); `prmake-card.sh` (retoma/abre, `--bg`) e vigia `agent run|install|uninstall|status` (LaunchAgent).
  - Teste local (`.t0033`): tela com KC real e diagrama renderizado; plano com sessão real desta conversa e custo;
    `prmake-card.sh` com `claude` falso (retoma a sessão certa na pasta certa, abre nova sem plano, `--bg`, vigia pega
    o "Continuar" e confirma); `contexto` com a base e o KC.
  - Achados/corrigidos: trecho Python embutido quebrado por escape de `\n` (visto no teste); instalador passa a exigir
    o manifesto da dependência (uma pasta vazia fazia pular a instalação — criada por engano no teste e removida).
- 2026-09-30: **onda 3 concluída** (F2, S2).
  - F2: fila de **sugestões** (backend `ArchitectureSuggestions`: análise propõe com `arch.sh suggest`, admin aplica/descarta;
    `analisar-bug` propõe aprendizados e divergências no passo 9/3); tela (admin): **Editar** (markdown com prévia),
    **Histórico** (ver e restaurar versão), **Sugerir melhoria** (chat com o especialista; proposta com Aplicar/Descartar),
    **Nova seção** (template), **Dados do projeto** (resumo/palavras-chave do índice), painel **Sugestões** com "Incorporar com a
    IA" (chat pré-preenchido; aplicar resolve a sugestão). Teste: chat pelo SDK real da Anthropic contra um simulador local;
    v1→v4 (IA, restauração, edição) e sugestão incorporada saindo da fila; 403 para não-admin na fila.
  - S2: engenharia reversa inicial em `kb/` (revisável no PR) — 9 projetos, 14 seções: `ecossistema` (mapa + mermaid, multi-tenant,
    como achar o projeto do card), `edv-solvace` (estrutura, módulos, dados/conexões, sessão ASP↔core, armadilhas),
    `edv-solvace-apps`, `edv-solvace-api` (ewcm-core-api: Ocelot, Cognito, dob_*), `revamp-actionplan` (estrutura, tabelas
    `TB_ACP_*`, regras do KC com ART-n), `revamp-modulos` (padrão + onde está cada módulo), `infra-aws` (RDS por cliente, Aurora,
    ~695 Lambdas de eventos, ECS/CodePipeline, Secrets Manager por NOME), `login`, `regras-de-negocio`. Semente: claude-global
    (`revamp/architecture.md`, ADR 0003). **Índice do parque inteiro + KC: ~2,5 mil tokens.** `kb/publicar.sh` publica tudo com o
    `arch.sh` (grava o commit de origem). Nenhum segredo nos textos (conferido).
  - Observações: commits de origem vêm da branch local de cada repo (edv-solvace estava em `hotfix/73821`); o monorepo `revamp`
    local é de 2025-10. **Próximos mapeamentos** (incrementais, admin com a skill): módulos revamp com mais cards (BOS, CIL, LPP,
    DefectTag, Users…) e seções `modulos`/`integracoes` do legado por módulo; siglas do ASP a confirmar pelo menu.
  - Achado de segurança (fora da base): `edv-solvace/solvace-asp/systems/includes/asp/all_conn.asp` tem credencial de banco no
    código-fonte.
